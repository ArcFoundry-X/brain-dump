# Unity GC 深度解析：为什么 GC Spike 总是找上你

战斗里跑得好好的，每隔几秒帧时间柱状图突然窜起一根 12 ms 的尖刺。Profiler 抓一帧下来，最长的那条横道写着 `GC.Collect`。手感能感受到——子弹和命中数字密集出现的时刻，画面就会顿一下；boss 释放大招、生成几十个特效对象的瞬间，准会再来一次。

这就是 GC Spike 最典型的表现：周期性、伴随高频对象分配的场景出现、抓帧能看到主线程在 `GC.Collect` 里停了几毫秒到几十毫秒。它不是 bug，而是 GC 在按规则工作。要把这种尖刺压下去，得先理解 GC 什么时候触发、它在做什么，再回头看代码里到底哪些写法在源源不断地往堆里塞东西。

---

## GC 在做什么：Boehm 的 stop-the-world

Unity 主线程上跑的 GC 是 Boehm GC——一种非分代、保守式的标记清除收集器。它的工作流程粗暴而直接：

1. 应用代码不停地 `new`，托管堆持续增长
2. 堆空间耗尽（达到当前堆上限），GC 触发
3. **主线程暂停**，GC 从所有根（栈、静态字段、寄存器）出发扫描整个堆，标记仍被引用的对象
4. 清扫未被标记的对象，回收空间
5. 主线程恢复

第 3 步就是所谓的 stop-the-world。暂停时长跟**堆总大小**线性相关，跟当前要回收多少对象关系不大——哪怕一帧只新增了 20 KB，只要那一帧触发了 GC，扫描的也是整个几十 MB 的堆。这就是为什么很多人发现"我明明只 new 了几个小对象啊，怎么会卡这么久"：暂停代价不在你这一帧的分配上，在历史累计的存活对象上。

Boehm 是非分代的——它不像 .NET CoreCLR 那样按对象寿命分代收集，没有 Gen0/Gen1/Gen2 之分。每次 GC 都是 full GC，整个堆扫一遍。对于游戏这种希望"每帧 16.6 ms 内完成所有事"的场景，full GC 几乎不可能不被看见。

GC 的触发时机几乎只有一个：堆空间耗尽。`GC.Collect()` 可以手动触发，但绝大多数情况下不应该这样做——手动 collect 会强行制造一次完整暂停，且并不会减少未来的暂停次数（只要分配模式没变，下次该卡还是卡）。少数合理场景是**关卡切换时**、玩家不会感知卡顿的过渡帧里主动 collect 一次，提前把堆压平。

---

## 增量 GC 不是银弹

Unity 2019 之后默认开启**增量 GC**（Incremental GC，在 Player Settings 里开关），思路是把原来的一次性 stop-the-world 拆分到多帧执行：每帧分一小片时间做标记，标记完成后再清扫，整个过程持续若干帧。

它的实际效果是：原本一次 15 ms 的 GC 暂停，会被切成 5 帧 × 3 ms 的小停顿。一次大卡顿变成几次小卡顿。对玩家来说，从"明显的顿一下"变成"帧时间稍微不太稳"，体感会好很多。

但**增量 GC 不减少总分配量、不减少总 GC 工作量**，它只是把账分期付了。每帧都付一点利息，对 60 fps 游戏来说意味着每帧多花 1–3 ms 做 GC 工作——如果你的项目本来就在 14 ms 上下挣扎，增量 GC 可能直接把你推到掉帧。

更隐蔽的问题是**写屏障**（Write Barrier）开销。增量 GC 为了保证标记的正确性，要在每次引用类型字段赋值时插入写屏障代码，这会让一部分托管代码变慢几个百分点。对于大量赋值的逻辑（比如 ECS 之外的大型对象池更新），这笔开销不一定能被"分摊卡顿"的收益抵消。

结论：增量 GC 是优化体验的工具，不是优化性能的工具。真正的解法永远是减少分配——少分配，GC 触发频率降低，每次 GC 工作量降低，写屏障开销也少。

---

## 堆分配从哪儿来：五类基础来源

写 C# 时产生托管堆分配的操作归纳起来有五类，理清楚之后看代码会快很多。

**1. `new` 一个 class 实例**：最直白的分配。

```csharp
var enemy = new Enemy();    // 分配
var list  = new List<int>(); // 分配（List 本身 + 内部 array）
```

**2. 装箱（boxing）**：值类型赋给 `object` 或 `interface` 变量。

```csharp
int hp = 100;
object boxed = hp;          // 装箱，分配
IComparable cmp = hp;       // 装箱，分配
Debug.Log("hp = " + hp);    // hp 装箱后再拼字符串
```

**3. 闭包**：lambda/匿名方法捕获了外部变量，编译器会生成一个隐藏的 class 来存放被捕获的变量。

```csharp
int multiplier = 3;
Func<int, int> f = x => x * multiplier; // 捕获 multiplier，分配闭包对象
```

**4. 迭代器状态机**：用 `yield return` 写的方法被编译成一个状态机 class，每次调用产生一次分配。

```csharp
IEnumerator Count() {
    for (int i = 0; i < 10; i++) yield return i;
}
StartCoroutine(Count()); // 每次调用都 new 一个状态机对象
```

**5. `params` 数组**：调用带 `params` 的方法时，编译器隐式 `new` 一个数组承载参数。

```csharp
void Log(params object[] args) { /* ... */ }
Log("hp", 100, "mp", 50); // 隐式 new object[4]，且四个值类型全部装箱
```

这五类把"代码里到底什么在分配"覆盖了八成。剩下的两成是 Unity 特有的隐藏分配，单独拎出来讲。

---

## 六种 Unity 特有的隐藏分配源

### 1. `foreach` 装箱

C# 早期版本对实现非泛型 `IEnumerable` 的集合（`ArrayList`、`Hashtable`、`Queue`、`Stack` 非泛型版本）做 `foreach` 时，每次会把返回的 `IEnumerator` 装箱。泛型集合不会有这个问题——它们的 `GetEnumerator()` 返回 struct enumerator。

```csharp
// before：每次 foreach 装箱 IEnumerator
ArrayList items = GetItems();
foreach (var item in items) {
    Process(item);
}
```

```csharp
// after：用泛型集合
List<Item> items = GetItems();
foreach (var item in items) {
    Process(item);
}
```

老项目里偶尔还能见到 `ArrayList`/`Hashtable`——大概率是从更老的代码迁移过来的。统一换成 `List<T>`/`Dictionary<K,V>` 就能消除这类分配。

### 2. Coroutine 的 `yield return new WaitForSeconds`

`WaitForSeconds` 是 class，每次 `yield return new WaitForSeconds(1f)` 都是一次堆分配。在一个每秒触发几次的协程里，这种写法每秒制造几次小垃圾。

```csharp
// before：每次循环 new 一个 WaitForSeconds
IEnumerator SpawnLoop() {
    while (true) {
        SpawnEnemy();
        yield return new WaitForSeconds(1f);
    }
}
```

```csharp
// after：缓存复用
static readonly WaitForSeconds OneSecond = new WaitForSeconds(1f);
IEnumerator SpawnLoop() {
    while (true) {
        SpawnEnemy();
        yield return OneSecond;
    }
}
```

`WaitForSeconds` 是无状态的（只持有目标时长），完全可以全局共享。`WaitForEndOfFrame`、`WaitForFixedUpdate` 同理。注意 `WaitUntil` 和 `WaitWhile` 持有 delegate，不适合这样缓存。

### 3. 捕获变量的 Delegate 订阅

一个 lambda 如果没捕获任何外部变量，编译器会把它缓存成静态 delegate，每次 `+=` 不再分配。一旦捕获了外部变量，编译器就要生成新的闭包对象 + delegate 对象，每次订阅都是一次分配。

```csharp
// before：捕获了 this 的字段 _id，每次订阅都分配
void Subscribe() {
    EventBus.OnHit += dmg => Process(_id, dmg);
}
```

```csharp
// after：抽成普通方法，订阅时只 new 一个 method group delegate
void Subscribe() {
    EventBus.OnHit += OnHit; // 仍会 new 一个 delegate，但消除了闭包
}
void OnHit(int dmg) {
    Process(_id, dmg);
}
```

更进一步，如果同一个订阅对象会反复挂载/卸载，把 delegate 实例缓存到字段里，连这一次 delegate 分配也能省掉：

```csharp
Action<int> _onHit;
void Awake() {
    _onHit = OnHit; // 一次性 new
}
void OnEnable()  => EventBus.OnHit += _onHit;
void OnDisable() => EventBus.OnHit -= _onHit;
```

这也是 `+=` / `-=` 配对时必须用同一个 delegate 实例的根本原因——否则 `-=` 找不到对应订阅，也就移除不掉。

### 4. LINQ 出现在热路径

LINQ 链式调用每一步都会 new 一个中间迭代器对象，配合 lambda 捕获再 new 闭包对象。在 `Update`、物理回调、AI tick 这类每帧都跑的地方使用 LINQ，分配量会很可观。

```csharp
// before：Update 里 LINQ，每帧分配 Where 迭代器 + 闭包 + ToArray
void Update() {
    var visible = _enemies
        .Where(e => e.Distance < _viewRange)
        .OrderBy(e => e.Distance)
        .ToArray();
    DrawMarkers(visible);
}
```

```csharp
// after：手写 for 循环 + 复用缓冲
readonly List<Enemy> _visibleBuf = new List<Enemy>(64);
void Update() {
    _visibleBuf.Clear();
    for (int i = 0; i < _enemies.Count; i++) {
        var e = _enemies[i];
        if (e.Distance < _viewRange) _visibleBuf.Add(e);
    }
    _visibleBuf.Sort(EnemyDistanceComparer.Instance);
    DrawMarkers(_visibleBuf);
}
```

LINQ 在初始化、关卡加载、配置预处理这类一次性场景里照常用——可读性收益远大于一次分配的代价。**热路径上禁用**才是规则。

### 5. Enum 做字典 Key 的装箱

部分 Unity 版本（特别是老一些的 Mono 后端）对 `Dictionary<TEnum, TValue>` 的默认 `EqualityComparer<TEnum>` 实现不够好，比较 enum key 时会把 enum 装箱成 `object` 再走 `Equals`。每次 `dict[someEnum]` 调用产生 1–2 次装箱。

```csharp
// before：依赖默认 comparer，每次访问装箱
Dictionary<SkillType, SkillConfig> _skills = new Dictionary<SkillType, SkillConfig>();
var cfg = _skills[skillType]; // 可能装箱
```

```csharp
// after：自定义 IEqualityComparer，按 int 直接比较
sealed class SkillTypeComparer : IEqualityComparer<SkillType> {
    public static readonly SkillTypeComparer Instance = new SkillTypeComparer();
    public bool Equals(SkillType a, SkillType b) => (int)a == (int)b;
    public int GetHashCode(SkillType v) => (int)v;
}

Dictionary<SkillType, SkillConfig> _skills =
    new Dictionary<SkillType, SkillConfig>(SkillTypeComparer.Instance);
```

新版本 Unity（IL2CPP + 较新的 Mono）已经修了这个问题，但跨版本兼容时显式提供 comparer 仍然是最稳的做法。

### 6. 字符串拼接

字符串是不可变的，`"HP: " + hp` 这种写法每次执行都 new 一个新 string。`int` 还会装箱（先转字符串）。一个挂在 Update 里的 HUD 文本，每帧都在制造垃圾。

```csharp
// before：Update 里拼字符串
void Update() {
    _hpText.text = "HP: " + _hp + " / " + _maxHp;
}
```

```csharp
// after：StringBuilder 缓存 + 只在值变化时重写
readonly StringBuilder _sb = new StringBuilder(32);
int _lastHp = -1;
void Update() {
    if (_hp == _lastHp) return;
    _lastHp = _hp;
    _sb.Clear();
    _sb.Append("HP: ").Append(_hp).Append(" / ").Append(_maxHp);
    _hpText.SetText(_sb); // TMP_Text.SetText(StringBuilder) 不产生分配
}
```

两个关键点：一是 `StringBuilder` 复用，二是**做脏检查**——血量不变就不更新文本，连 `SetText` 都省了。TextMeshPro 提供了 `SetText(StringBuilder)` 重载，专门为零分配文本而设计，配合使用效果最好。

普通 `Text` 组件没有这个 API，只能赋 string，但 `Clear` + `Append` 后再 `ToString()` 仍比纯字符串拼接好一些（至少减少了中间 string 对象）。

---

## 零分配设计原则

知道分配从哪来之后，再看怎么从设计层面把分配压到 0。

### struct 何时优于 class

struct 是值类型，赋值是拷贝，栈上分配（作为局部变量时）或内联在持有它的对象内存里（作为字段时）——都不走 GC 堆。判断标准：

- **数据轻量**（一般 ≤ 16 字节，包含到 `double` 量级的几个字段）
- **不需要多态**（不打算继承、不打算装到 `interface` 变量里）
- **不需要共享引用语义**（不希望多个地方持有"同一个"实例并互相看到修改）

经典例子：坐标 `Vector3`、颜色 `Color`、范围 `Range`、伤害事件 `DamageInfo`、网络包头 `PacketHeader`。这些都该是 struct。

大 struct（超过 16 字节）传参时的值拷贝开销可能反而比 class 大，这时用 `in` 参数让编译器按引用传递、且保证调用方不修改：

```csharp
struct DamageInfo { /* 40 字节 */ }

// 不好：每次调用都拷贝 40 字节
void Apply(DamageInfo info) { /* ... */ }

// 好：按引用传递只读
void Apply(in DamageInfo info) { /* ... */ }
```

需要修改时用 `ref`。注意一个常见坑：把 struct 装到 `List<T>` 里之后，`list[i].field = x` **能编译通过，但修改的是临时拷贝**——`list[i]` 返回的是值拷贝，对它赋值不影响 List 内部的元素。这类静默逻辑错误比编译错误更难发现。要么整体赋值（`list[i] = newValue`），要么改用数组（`array[i].field = x` 直接修改底层内存）。

### `Span<T>` 与 `stackalloc`

`stackalloc` 在栈上分配数组，离开作用域自动释放，零 GC 压力。`Span<T>` 是栈上引用，可以指向 `stackalloc` 区域、堆数组、native 内存，作为方法参数时和数组形参等价：

```csharp
// 临时计算 8 个角点的世界坐标，用 stackalloc 避免堆分配
void TransformCorners(in Bounds b, in Matrix4x4 m)
{
    Span<Vector3> corners = stackalloc Vector3[8];
    corners[0] = m.MultiplyPoint3x4(new Vector3(b.min.x, b.min.y, b.min.z));
    corners[1] = m.MultiplyPoint3x4(new Vector3(b.max.x, b.min.y, b.min.z));
    // ... 其余 6 个角
    UpdateBoundingBox(corners); // 传 Span，不复制数据
}
```

接收方用 `ReadOnlySpan<T>` 声明参数，可以同时接受 `stackalloc` 区域和堆数组，不限制调用方：

```csharp
void UpdateBoundingBox(ReadOnlySpan<Vector3> points)
{
    var min = points[0]; var max = points[0];
    for (int i = 1; i < points.Length; i++) {
        min = Vector3.Min(min, points[i]);
        max = Vector3.Max(max, points[i]);
    }
    _bounds = new Bounds((min + max) * 0.5f, max - min);
}
```

两个约束：`stackalloc` 大小**必须是小常量**（一般不超过 1 KB，否则栈溢出）；`Span<T>` 是 `ref struct`，不能存到字段、不能跨 `await`/`yield`、不能装箱。它专门是为热路径上短生命周期的栈数据设计的。

### 缓存与复用

把所有"无状态或状态可重置"的对象做成静态或字段持有，避免反复 new。前面的 `WaitForSeconds`、`StringBuilder`、`delegate` 都属于此类。常见缓存模式：

```csharp
// 1. 全局共享的常量对象
static readonly WaitForSeconds OneSecond = new WaitForSeconds(1f);

// 2. 字段持有、用前清空
readonly List<Collider> _overlapBuf = new List<Collider>(32);
void Scan() {
    _overlapBuf.Clear();
    Physics.OverlapSphereNonAlloc(/* ... */); // Unity 提供的 NonAlloc API
}

// 3. delegate 缓存
Action<int> _onHit;
void Awake() => _onHit = OnHit;
```

Unity 的物理、射线、`GetComponents` 都提供 `NonAlloc` 版本（`OverlapSphereNonAlloc`、`RaycastNonAlloc`、`GetComponents(List<T>)`），接收预分配的缓冲数组而不是返回新数组。在热路径上一律用 NonAlloc 版本。

### 无分配事件系统

C# 原生的 `event` + `delegate` 有两个分配点：订阅时新增 delegate 实例、调用时遍历 invocation list 不分配但 add/remove 操作会重新创建数组。频繁动态订阅的场景，这部分分配累积起来很可观。

零分配事件的核心思路：用**泛型接口 + struct 事件参数**替代 delegate。

```csharp
public interface IEventHandler<T> where T : struct {
    void OnEvent(in T evt);
}

// 订阅方实现接口，不需要 new delegate
public class HitSparkSpawner : MonoBehaviour, IEventHandler<HitEvent> {
    void OnEnable()  => EventBus<HitEvent>.Register(this);
    void OnDisable() => EventBus<HitEvent>.Unregister(this);
    public void OnEvent(in HitEvent evt) {
        Spawn(evt.Position);
    }
}

public struct HitEvent {
    public Vector3 Position;
    public float Damage;
}
```

EventBus 内部用 `List<IEventHandler<T>>` 存储订阅者，调用时遍历列表逐个 `OnEvent(in evt)`——struct 通过 `in` 传引用，零分配；订阅/取消订阅就是 List 的 Add/Remove，List 容量不足时偶尔触发一次扩容分配。这套模式在帧内频繁触发的事件（命中、伤害、AI 感知）上几乎不产生任何 GC 压力。

代价是订阅方必须实现接口（不能用 lambda 临时订阅），结构上没那么灵活。这是为零分配付的明确代价。

---

## 用 Profiler 找到分配大户

定位思路只有一条：**按 GC Alloc 列排序**。在 Profiler 的 CPU Usage 模块切到 Hierarchy 视图，找到 GC Alloc 列，点击列头按降序排，从最大的那一行开始往里展开调用栈。

理想状态是稳定运行（非加载、非场景切换）时 GC Alloc 列每帧为 0。实际项目里维持几 KB 也算可以接受，超过几十 KB 就意味着热路径上有持续分配，触发 GC 的频率会很高。

定位到具体方法之后，对照前面那六种隐藏分配源排查——大多数情况都能直接对上号。

---

## 速查表

| 分配来源 | 修复模式 |
|---|---|
| `foreach` 非泛型集合（`ArrayList`/`Hashtable`） | 改用泛型集合 `List<T>` / `Dictionary<K,V>` |
| `yield return new WaitForSeconds(t)` | 静态字段缓存 `WaitForSeconds` 实例 |
| lambda 捕获外部变量 | 抽成普通方法，或把 delegate 缓存到字段 |
| LINQ 出现在 Update / 物理回调 | 改写为 for 循环 + 复用缓冲 |
| `Dictionary<MyEnum, T>` 默认 comparer | 提供自定义 `IEqualityComparer<MyEnum>` |
| `"text" + value` 字符串拼接 | `StringBuilder` 复用 + 脏检查 + `SetText(StringBuilder)` |
| 物理/射线返回数组 | 改用 `*NonAlloc` 版本配合预分配缓冲 |
| `event` + 捕获变量 lambda 频繁订阅 | 泛型接口 + struct 事件参数的无分配事件总线 |

GC Spike 不是不可解的。它只是 Boehm 的工作机制和你代码里持续分配的两件事互相作用的结果——把分配压住，Spike 自然消失。下一篇会从托管层走到 Native 层，讨论 `Destroy` 之后内存为什么还在涨。
