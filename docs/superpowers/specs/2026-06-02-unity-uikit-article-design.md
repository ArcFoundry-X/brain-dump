# Unity UIKit 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 第六篇
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，了解 MonoBehaviour 生命周期）
- 语言：简体中文
- 语气：自然、专业，承接前几篇风格
- 校准等级：INTERMEDIATE

---

## 文章标题（参考）

`Unity UI 框架：给每个界面一份生命周期契约`

---

## 叙事策略

**问题驱动 + 从零推导**：开篇暴露直觉写法的三个痛点，逐步推导出接口契约（`IUIForm`）、模板基类（`UIFormBasic`）、管理器入口（`UIManager`），最后用完整示例串联。每一节都从"现在还有什么问题"出发，解释该设计决策存在的原因。

---

## 范围约定

- `UIFormPool` 类：**不提**，完全忽略
- 资源加载：统一用 `Resources.Load` 同步加载，不依赖 ResourceManager，文章独立成篇
- `IUIFormData`：简述一节，不展开完整示例

---

## 文章结构

### 第一节：开篇（Hook）

- 场景：中型游戏十几个界面，直觉写法是各处自己 `Instantiate`/`Destroy`/`SetActive`

```csharp
// 反面示例：按钮脚本自己管理 UI
public class ShopButton : MonoBehaviour
{
    [SerializeField] private GameObject shopPanelPrefab;
    private GameObject _shopInstance;

    public void Open()
    {
        _shopInstance = Instantiate(shopPanelPrefab);
    }

    public void Close()
    {
        Destroy(_shopInstance);
    }
}
```

- 点出三个痛点：
  1. **没有统一入口**：没人知道某个界面现在是不是开着的
  2. **重复实例化**：频繁打开关闭制造 GC 压力（呼应第一篇对象池结论）
  3. **层级失控**：弹窗出现在主界面下面，因为没有人管 Canvas 层级
- 引出方向：解决这三个问题需要一个统一 UI 管理器，前提是**每个界面都遵守同一套生命周期契约**

---

### 第二节：第一步——用接口定契约（IUIForm）

- 引出问题：`UIManager` 不能依赖每个具体 UI 类型，否则每加一个界面就要改管理器
- 解法：定接口，管理器只认接口

```csharp
public interface IUIForm
{
    string FormAssetName { get; set; }
    Transform Transform { get; set; }

    void OnInit(string formAssetName, Transform transform);  // 首次实例化，只调一次
    void OnOpen(IUIFormData uiFormData = null);              // 每次打开
    void OnClose();                                          // 每次关闭
    void OnRecycle();                                        // 销毁前清理
}
```

- 逐一解释四个方法职责：
  - `OnInit`：一次性初始化（绑按钮事件、获取子节点引用）
  - `OnOpen`：每次打开刷新数据、播动画
  - `OnClose`：每次关闭停协程、重置状态
  - `OnRecycle`：场景切换前释放资源
- 点出设计思想：**依赖倒置** —— `UIManager` 只依赖 `IUIForm` 抽象，和第一篇 `IPoolItem` 思路一致

---

### 第三节：第二步——模板基类（UIFormBasic）

- 引出问题：每个界面都实现四个接口方法，大多数是空实现，繁琐
- 解法：抽象基类提供默认空实现，子类只 `override` 需要的

```csharp
public abstract class UIFormBasic : MonoBehaviour, IUIForm
{
    public string FormAssetName { get; set; }
    public Transform Transform { get; set; }
    protected IUIFormData _uiFormData;

    public virtual void OnInit(string formAssetName, Transform transform)
    {
        FormAssetName = formAssetName;
        Transform = transform;
    }

    public virtual void OnOpen(IUIFormData uiFormData = null)
    {
        _uiFormData = uiFormData;
    }

    public virtual void OnClose() { }
    public virtual void OnRecycle() { }
}
```

- 两个设计决策：
  1. **为什么抽象类而不是直接实现接口**：需继承 `MonoBehaviour`，C# 单继承，接口不占继承位
  2. **模板方法模式**：基类定框架（四个方法按固定顺序被调用），子类填空

- 子类写法示意：

```csharp
public class ShopForm : UIFormBasic
{
    public override void OnOpen(IUIFormData uiFormData = null)
    {
        base.OnOpen(uiFormData);
        // 刷新商品列表
    }

    public override void OnClose()
    {
        // 停止滚动动画
    }
}
```

---

### 第四节：第三步——管理器入口（UIManager）

分三个子话题推进：

#### 4.1 层级系统

- 引出问题：所有 UI 挂同一 Canvas，谁后实例化谁在上面，层级不可控
- 解法：`UILevel` 枚举 + 自动创建层级子节点

```csharp
public enum UILevel { Normal, Popup, Item }
```

```csharp
private void CreateLevelRoots()
{
    foreach (UILevel level in Enum.GetValues(typeof(UILevel)))
    {
        var go = new GameObject(level.ToString(), typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _levelRoots[level] = go.transform;
    }
}
```

#### 4.2 两级缓存

- 引出问题：关闭就 `Destroy`，再开就 `Instantiate`，制造 GC
- 解法：关闭不销毁，`SetActive(false)` 藏起来
- 两个字典职责：
  - `_activeForms`：当前可见界面
  - `_cachedForms`：已实例化但隐藏的界面，场景切换前才真正销毁
- 状态流转示意：`首次打开 → 实例化 → 进 cached + 进 active` → `关闭 → 出 active，留 cached，SetActive(false)` → `再次打开 → 从 cached 取出，SetActive(true)，进 active`

#### 4.3 OpenUIForm / CloseUIForm

```csharp
public void OpenUIForm(string formAssetName, UILevel level = UILevel.Normal,
    IUIFormData uiFormData = null)
{
    if (_activeForms.TryGetValue(formAssetName, out var activeForm))
    {
        activeForm.OnOpen(uiFormData);  // 已激活：只刷新数据
        return;
    }

    if (!_cachedForms.TryGetValue(formAssetName, out var form))
    {
        // 首次打开：实例化并初始化
        string path = Path.Combine(_rootPath, level.ToString(), formAssetName);
        var prefab = Resources.Load<GameObject>(path);
        var go = Instantiate(prefab, _levelRoots[level]);
        form = go.GetComponent<IUIForm>();
        form.OnInit(formAssetName, go.transform);
        _cachedForms[formAssetName] = form;
    }

    form.Transform.gameObject.SetActive(true);
    _activeForms[formAssetName] = form;
    form.OnOpen(uiFormData);
}
```

- 简短展示 `CloseUIForm`（调 `OnClose`，`SetActive(false)`，从 `_activeForms` 移除）
- 简短展示 `ClearCache`（场景切换时调，调 `OnRecycle`，销毁所有缓存）

---

### 第五节：传参机制（简述）

- 引出问题：`OnOpen` 为什么不直接传 `object` 或具体参数？
- 解法：`IUIFormData` 空接口作类型标记，具体界面自己做类型转换

```csharp
public interface IUIFormData { }

public class ShopFormData : IUIFormData
{
    public int DefaultTabIndex;
}
```

- 用法一行带过：

```csharp
UIManager.Instance.OpenUIForm("ShopForm", UILevel.Normal,
    new ShopFormData { DefaultTabIndex = 1 });
```

- 设计意图：`UIManager` 签名统一接收 `IUIFormData`，不需要知道每种界面的参数格式

---

### 第六节：完整使用示例

串联所有模块，展示 `ShopForm` 完整实现：

```csharp
public class ShopForm : UIFormBasic
{
    [SerializeField] private Text _titleText;

    public override void OnInit(string formAssetName, Transform transform)
    {
        base.OnInit(formAssetName, transform);
        // 绑定按钮事件，只做一次
    }

    public override void OnOpen(IUIFormData uiFormData = null)
    {
        base.OnOpen(uiFormData);
        var data = uiFormData as ShopFormData;
        _titleText.text = data != null ? $"商店 Tab {data.DefaultTabIndex}" : "商店";
    }

    public override void OnClose()
    {
        // 停止动画、重置滚动位置
    }
}
```

调用方示例：

```csharp
// 打开商店，默认停在 Tab 1
UIManager.Instance.OpenUIForm("ShopForm", UILevel.Popup,
    new ShopFormData { DefaultTabIndex = 1 });

// 关闭
UIManager.Instance.CloseUIForm(UIManager.Instance.GetUIForm("ShopForm"));
```

预制体放置规范：路径为 `Resources/UIPrefabs/Popup/ShopForm.prefab`，`UIManager` 自动拼接路径。

---

### 第七节：小结

生命周期表格：

| 方法 | 调用时机 | 典型用途 |
|------|----------|----------|
| `OnInit` | 首次实例化，只调一次 | 绑定按钮事件、获取子节点引用 |
| `OnOpen` | 每次打开 | 刷新数据、播开场动画 |
| `OnClose` | 每次关闭 | 停协程、重置 UI 状态 |
| `OnRecycle` | 场景切换前销毁 | 释放大资源、取消订阅事件 |

架构关系图：

```
调用方
  │  OpenUIForm("ShopForm", ...)
  ▼
UIManager
  ├── _activeForms  ──► 当前可见界面
  ├── _cachedForms  ──► 已实例化、暂时隐藏
  └── _levelRoots   ──► Normal / Popup / Item 层级节点
          │
          ▼
      IUIForm（ShopForm、BagForm、…）
```

末尾一句预告下一篇（配置表 / ScriptableObject）。

---

## 代码规范

- 所有代码块加 `csharp` 语言标识
- 文章内代码用 `Resources.Load` 同步加载，不引入 ResourceManager
- `UIFormPool` 完全不提
- `IUIFormData` 只展示定义和一行用法，不写完整示例
- 注释只在非显而易见的约束处出现

---

## 不在范围内

- `UIFormPool` 类
- 异步加载 / ResourceManager 集成
- UI 动画、过渡效果
- 弹窗堆栈（按顺序关闭多个弹窗）
- 多 Canvas / Camera 模式
- `IUIFormData` 完整示例
