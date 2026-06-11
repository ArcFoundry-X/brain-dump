# Unity UIForm MVC/MVVM 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 第六篇（UIKit）续篇
- 前置文章：第六篇 UIKit（UIManager、IUIForm、UIFormBasic、UIFormAttribute）
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，读过前篇 UIKit）
- 语言：简体中文
- 语气：自然、专业，承接系列风格
- 校准等级：INTERMEDIATE

---

## 文章标题（参考）

`Unity UIForm 里的 MVC 与 MVVM：从混乱到数据驱动`

---

## 叙事策略

**问题驱动递进**：同一个角色属性面板贯穿全文。从"上帝 MonoBehaviour"开始，逐步推导出 MVC 三层结构，再暴露 MVC 处理实时数据时的局限，引入 ReactiveProperty，最终形成 MVVM。每一步都由"现在还有什么问题"驱动。

---

## 范围约定

- MVVM 使用 **UniRx / R3**（API 相同，R3 是现代替代版）
- MVC 无第三方库依赖
- 示例界面：**角色属性面板**（HP 条、金币数、等级）
- 不覆盖：响应式编程完整教程、Rx 操作符大全、网络请求绑定、列表/集合绑定

---

## 文章结构

### 第一节：开篇（Hook）

- 场景：角色属性面板，显示 HP 条、金币、等级
- 展示"上帝 MonoBehaviour"反面示例：`CharacterPanelForm` 数据、UI 刷新、按钮逻辑全混在一起

```csharp
public class CharacterPanelForm : UIFormBasic
{
    [SerializeField] private Slider _hpBar;
    [SerializeField] private Text _hpText;
    [SerializeField] private Text _goldText;

    private int _hp = 100;
    private int _maxHp = 100;
    private int _gold = 500;

    public override void OnOpen(IUIFormData data = null)
    {
        base.OnOpen(data);
        _hp = GameManager.Instance.PlayerHp;
        _gold = GameManager.Instance.PlayerGold;
        _hpBar.value = (float)_hp / _maxHp;
        _hpText.text = $"{_hp} / {_maxHp}";
        _goldText.text = _gold.ToString();
    }

    public void OnBuyButtonClicked()
    {
        int cost = 100;
        if (_gold >= cost)
        {
            _gold -= cost;
            GameManager.Instance.PlayerGold = _gold;
            _goldText.text = _gold.ToString();
        }
    }
}
```

- 三个痛点：
  1. 数据和显示耦合：换 UI 框架要重写所有逻辑
  2. 逻辑无法复用：购买逻辑和 UI 刷新混在一起
  3. 测试困难：验证业务逻辑必须跑完整 UI 场景

---

### 第二节：MVC 第一步——抽出 Model

- 把数据和业务规则剥离为纯 C# 类 `CharacterModel`
- 展示 `TrySpend`、`TakeDamage` 是纯逻辑，无 Unity API 依赖，可单元测试

```csharp
public class CharacterModel
{
    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public int Gold { get; private set; }
    public int Level { get; private set; }

    public CharacterModel(int maxHp, int gold, int level)
    {
        MaxHp = maxHp;
        Hp = maxHp;
        Gold = gold;
        Level = level;
    }

    public bool TrySpend(int cost)
    {
        if (Gold < cost) return false;
        Gold -= cost;
        return true;
    }

    public void TakeDamage(int damage)
    {
        Hp = Mathf.Max(0, Hp - damage);
    }
}
```

---

### 第三节：MVC 第二步——View 与 Controller 职责划分

- View 只持有 UI 元素引用，只提供 `Refresh(CharacterModel)` 方法

```csharp
public class CharacterPanelView : MonoBehaviour
{
    [SerializeField] private Slider _hpBar;
    [SerializeField] private Text _hpText;
    [SerializeField] private Text _goldText;

    public void Refresh(CharacterModel model)
    {
        _hpBar.value = (float)model.Hp / model.MaxHp;
        _hpText.text = $"{model.Hp} / {model.MaxHp}";
        _goldText.text = model.Gold.ToString();
    }
}
```

- Controller（UIFormBasic 子类）连接两者：`OnInit` 绑按钮，`OnOpen` 触发首次刷新

```csharp
public class CharacterPanelForm : UIFormBasic
{
    private CharacterPanelView _view;
    private CharacterModel _model;

    public override void OnInit(string formAssetName, Transform transform)
    {
        base.OnInit(formAssetName, transform);
        _view = GetComponent<CharacterPanelView>();
        GetComponentInChildren<Button>().onClick.AddListener(OnBuyClicked);
    }

    public override void OnOpen(IUIFormData data = null)
    {
        base.OnOpen(data);
        _model = (data as CharacterPanelData)?.Model;
        _view.Refresh(_model);
    }

    private void OnBuyClicked()
    {
        if (_model.TrySpend(100))
            _view.Refresh(_model);
    }
}
```

- 总结三层职责，引出局限：数据实时变化时 Controller 怎么办？

`CharacterPanelData` 的定义（MVC 版传 Model，MVVM 版传 ViewModel）：

```csharp
// MVC 版
public class CharacterPanelData : IUIFormData
{
    public CharacterModel Model;
}

// MVVM 版（第六节起用这个）
public class CharacterPanelData : IUIFormData
{
    public CharacterViewModel ViewModel;
}
```

---

### 第四节：MVC 的局限

三种应对实时数据变化的做法，逐一分析代价：

1. **`Update()` 轮询**：每帧刷新所有 UI，哪怕数据没变
2. **给 Model 加 `event`**：Controller 订阅，但生命周期管理麻烦，忘记退订内存泄漏
3. **每个字段单独加事件**：字段多了 Controller 订阅一堆，Model/Controller/View 三处同步修改

核心矛盾：Controller 始终是中间人，既管用户输入又管数据同步，职责边界模糊。引出 MVVM 思路：让 View 直接"盯着"数据。

---

### 第五节：引入 ReactiveProperty

- 一句话介绍 UniRx/R3，说明安装方式
- 三行代码展示核心用法：赋值即触发订阅

```csharp
var hp = new ReactiveProperty<int>(100);
hp.Subscribe(value => Debug.Log($"HP changed: {value}"));
hp.Value = 80;  // 自动触发回调
```

- 改写 `CharacterModel`，字段换成 `ReactiveProperty`
- 点出：`TakeDamage` 不再需要手动通知任何人

---

### 第六节：MVVM——View 直接订阅 ViewModel

- ViewModel 职责：把 Model 数据转成 View 需要的格式，不持有 View 引用

```csharp
public class CharacterViewModel
{
    private readonly CharacterModel _model;

    public IReadOnlyReactiveProperty<float> HpRatio { get; }
    public IReadOnlyReactiveProperty<string> HpText { get; }
    public IReadOnlyReactiveProperty<string> GoldText { get; }

    public CharacterViewModel(CharacterModel model)
    {
        _model = model;
        HpRatio = model.Hp.Select(hp => (float)hp / model.MaxHp.Value)
                         .ToReadOnlyReactiveProperty();
        HpText = model.Hp.Select(hp => $"{hp} / {model.MaxHp.Value}")
                         .ToReadOnlyReactiveProperty();
        GoldText = model.Gold.Select(g => g.ToString())
                             .ToReadOnlyReactiveProperty();
    }

    public void Buy(int cost) => _model.TrySpend(cost);
}
```

- View 在 `OnOpen` 订阅，`CompositeDisposable` 管理生命周期，`OnClose` 一次性释放
- 两个关键设计点：`IReadOnlyReactiveProperty`（只读，数据流单向）、`CompositeDisposable`（解决第四节内存泄漏问题）

---

### 第七节：完整示例

- 三层代码完整展示（CharacterModel + CharacterViewModel + CharacterPanelForm）
- 加一段展示外部触发：战斗系统调 `model.TakeDamage(30)`，HP 条自动更新，界面代码不需要改动
- 一个 drawio 数据流向图：`Model → ViewModel → View`，以及用户输入方向 `View → ViewModel → Model`

---

### 第八节：小结

MVC vs MVVM 对比表：

| | MVC | MVVM |
|--|-----|------|
| 数据层 | Model（纯 C# 类） | Model（带 ReactiveProperty） |
| 显示层 | View（提供 Refresh 方法） | View（订阅 ViewModel） |
| 中间层 | Controller（主动调 Refresh） | ViewModel（不持有 View 引用） |
| 数据变化响应 | Controller 被通知后手动刷新 | View 自动响应，无需中间人 |
| 适合场景 | 交互为主、状态变化少 | 数据驱动、实时更新频繁 |
| 依赖 | 无第三方库 | UniRx / R3 |

选型建议：简单界面用 MVC，实时数据界面用 MVVM，两者可混用。

末尾预告下一篇（配置表 / ScriptableObject）。

---

## 代码规范

- 所有代码块加 `csharp` 语言标识
- 示例统一用角色属性面板（CharacterModel/CharacterViewModel/CharacterPanelForm）
- 不展示 UniRx/R3 完整操作符，只用 Subscribe、Select、ToReadOnlyReactiveProperty、AddTo
- drawio 文件放 `ToolkitInPorject/assets/`，文章留占位符

---

## 不在范围内

- 响应式编程（Rx）完整教程
- 集合类型绑定（ReactiveCollection）
- 网络请求与 MVVM 结合
- Unity UI Toolkit 的原生绑定系统
- 多 ViewModel 组合
