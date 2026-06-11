# 走出 GC 堆：Job System 的内存模型

前两篇讲完了 GC 压力和 Native 资产泄漏，剩下一个问题：如果项目对性能要求极高，Update 里有大量每帧计算（万级粒子模拟、大规模寻路、物理代理），连 GC Alloc 都要压到 0，该怎么做？

答案是走出 GC 堆，把数据放进 Unity 的 Native 内存里，通过 Job System 在工作线程上处理。但"走出 GC 堆"不是一个优化技巧——它意味着进入一套完全不同的内存规则：数据必须是 Blittable 类型，分配时指定生命周期，使用后必须显式释放，GC 不替你兜底。

这一篇讲清楚这套规则的核心：NativeContainer 是什么、Allocator 怎么选、为什么 Dispose 必须手动、Safety Handle 在保护什么。

---

## Job System 为什么不能用 C# 对象

Job 在 Unity 的工作线程上执行，有两个根本约束让 C# 对象（class 实例、数组、`List<T>` 等 managed 类型）无法在 Job 里使用：

**线程安全问题。** GC 扫描堆时需要知道所有活跃线程的根（寄存器、栈帧），工作线程如果同时持有托管对象的引用，GC 必须暂停工作线程才能安全扫描。Unity 的 Job System 不做这个协同，访问托管对象会产生未定义行为。

**Burst 编译器的限制。** Burst 把 Job 代码编译成高度优化的原生机器码（SIMD 向量化、平台特定指令集），要求输入输出类型是 **Blittable**——在托管内存和非托管内存中布局完全一致、不含托管指针的类型。C# 的 class 对象包含指向 GC 堆的指针，不满足 Blittable，Burst 直接拒绝编译。

```csharp
// 这段代码编译不过：Job struct 里不能有 managed 类型字段
struct BadJob : IJob
{
    public List<float> Data; // 错误：List<T> 是 managed 类型
    public void Execute() { /* ... */ }
}
```

结论：Job 的数据必须放在 Native 内存里，通过 `NativeContainer` 系列类型访问。

---

## NativeContainer：Native 内存的容器

`NativeContainer` 是一组在 Native 堆上分配、可安全传入 Job 的集合类型。它们的 C# 外壳是 struct（栈上，零 GC），数据本体在 Native 堆。

| 类型 | 用途 |
|---|---|
| `NativeArray<T>` | 固定长度数组，最常用，开销最小 |
| `NativeList<T>` | 可变长度列表（需 `Unity.Collections` 包） |
| `NativeHashMap<K,V>` | 键值对映射，键值都须是 Blittable |
| `NativeQueue<T>` | 先进先出队列 |
| `NativeParallelMultiHashMap<K,V>` | 多线程并发写入场景 |

`NativeArray<T>` 是其中开销最低的，适合大多数"固定量的批量数据处理"场景。需要动态增减元素时才用 `NativeList`；键值查找才用 `NativeHashMap`。

创建方式：

```csharp
// 分配一个容纳 1024 个 float 的 NativeArray
var positions = new NativeArray<float3>(1024, Allocator.TempJob);

// 使用
for (int i = 0; i < positions.Length; i++)
    positions[i] = new float3(i, 0, 0);

// 必须显式释放
positions.Dispose();
```

---

## Allocator：三种生命周期

创建 NativeContainer 时必须指定 Allocator，它决定这块内存的生命周期和分配策略：

| Allocator | 生命周期 | 适用场景 | 忘记 Dispose 的后果 |
|---|---|---|---|
| `Allocator.Temp` | 单帧（严格来说最长 4 帧） | 帧内用完即弃的临时数据 | 编辑器下警告，框架自动释放 |
| `Allocator.TempJob` | 4 帧以内 | Job 执行期间的临时数据 | 超出 4 帧编辑器报错 |
| `Allocator.Persistent` | 手动管理，无上限 | 跨帧、跨场景长期存在的数据 | 内存泄漏；编辑器退出时报警告 |

**Temp** 分配速度最快（接近栈分配），但有严格的生命周期约束：只能在当前帧内使用，不能传给跨帧的 Job。

**TempJob** 是 Job 场景的标准选择：比 Temp 略慢，但允许 Job 在多帧内完成（最多 4 帧）。超过 4 帧编辑器会报错提示生命周期违规——这是框架在帮你抓 bug，不是 bug 本身。

**Persistent** 分配速度最慢（接近 `malloc`），但生命周期完全由你控制，忘记 Dispose 就是真正的内存泄漏。

### 用错 Allocator 的典型后果

```csharp
// 错误：Temp 分配的数组传给跨帧 Job
void Update()
{
    var data = new NativeArray<float>(100, Allocator.Temp);
    _longRunningHandle = new ProcessJob { Data = data }.Schedule();
    // 本帧结束，Temp 内存被框架回收
    // Job 可能在下一帧才执行，此时读到的是已释放的内存（脏数据或崩溃）
}
```

```csharp
// 正确：跨帧 Job 用 TempJob，Job 完成后自动释放
void Update()
{
    var data = new NativeArray<float>(100, Allocator.TempJob);
    var handle = new ProcessJob { Data = data }.Schedule();
    data.Dispose(handle); // Job handle 完成时自动释放，不阻塞主线程
    _handle = handle;
}

void LateUpdate()
{
    _handle.Complete(); // 等待 Job 完成（如果还没完）
}
```

---

## Safety Handle：编辑器下的安全网

每个 NativeContainer 内部持有一个 `AtomicSafetyHandle`，在编辑器和 Development Build 下（宏 `ENABLE_UNITY_COLLECTIONS_CHECKS` 开启时）追踪所有读写访问：

- 同一容器不能同时被两个 Job 写入（写写冲突）——Safety Handle 检测到后立即抛异常，Job 调度被拒绝
- 一个 Job 写入某容器时，另一个 Job 不能同时读（写读冲突），除非声明 `[ReadOnly]`
- 主线程在 Job 完成前（`JobHandle.Complete()` 调用前）不能读写该容器——违规抛 `InvalidOperationException`

```csharp
struct WriteJob : IJobParallelFor
{
    public NativeArray<float> Result; // 写入
    public void Execute(int index) => Result[index] = index * 2f;
}

struct ReadJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Input; // 声明只读，允许多 Job 并行读
    public NativeArray<float> Output;
    public void Execute(int index) => Output[index] = Input[index] + 1f;
}

void Schedule()
{
    var data = new NativeArray<float>(1000, Allocator.TempJob);
    var write = new WriteJob { Result = data }.Schedule(1000, 64);
    // ReadJob 依赖 WriteJob 的输出，通过 handle 建立依赖，Safety Handle 自动验证顺序
    var read = new ReadJob { Input = data, Output = _output }.Schedule(1000, 64, write);
    data.Dispose(read);
    _finalHandle = read;
}
```

**Safety Handle 只在编辑器和 Development Build 下生效**，Release Build 下不做任何检查（为了性能）。这意味着编辑器下能跑的不代表 Release 下没有竞态——必须在开发阶段把所有 Safety Handle 报出的冲突解决干净。

---

## 正确的 Dispose 模式

NativeContainer 的 Dispose 有三种场景，选错会导致泄漏或崩溃：

```csharp
// 场景 1：帧内用完立即释放（Temp / TempJob，同帧 Complete）
void ProcessThisFrame()
{
    var buf = new NativeArray<float>(256, Allocator.Temp);
    new QuickJob { Data = buf }.Run(); // Run() 同步执行，当前线程阻塞直到完成
    buf.Dispose(); // 立即释放，安全
}

// 场景 2：跨帧 Job，Job 完成后释放（推荐：Dispose(handle) 延迟释放）
NativeArray<float> _data;
JobHandle _handle;

void StartJob()
{
    _data = new NativeArray<float>(1024, Allocator.TempJob);
    _handle = new LongJob { Data = _data }.Schedule();
    _data.Dispose(_handle); // handle 完成时自动释放，主线程不阻塞
}

void LateUpdate() => _handle.Complete();

// 场景 3：生命周期绑定组件（Persistent，OnEnable/OnDisable 配对）
NativeArray<float3> _positions;

void OnEnable()
{
    _positions = new NativeArray<float3>(maxCount, Allocator.Persistent);
}

void OnDisable()
{
    if (_positions.IsCreated) _positions.Dispose();
}
```

`IsCreated` 属性在 Dispose 之后返回 `false`，用于防止重复 Dispose（重复 Dispose 会崩溃）。

---

## Coroutine vs Job：内存视角对比

| 对比维度 | Coroutine | Job |
|---|---|---|
| 数据存放 | GC 堆（class 对象、捕获变量） | Native 内存（NativeContainer） |
| 状态机 | 编译器生成 class，每次调用产生堆分配 | struct 实现 IJob，零堆分配 |
| 并发 | 单线程协作式（让出执行权，主线程调度） | 工作线程并行（真正的多核利用） |
| 适用场景 | 时序逻辑、UI 动画、等待异步操作 | 批量数据处理、物理代理、粒子模拟 |

Coroutine 没有 GC 代价的幻觉需要破除：每次 `StartCoroutine` 都 new 一个状态机对象，`yield return` 本身不分配，但协程框架的调度和 `WaitForSeconds` 等对象如果不缓存也会产生分配。对于每帧高频触发的轻量逻辑，Coroutine 的固定开销不可忽视。

Job 的零分配是结构性的：Job struct 本身在栈上，数据在 NativeContainer 里，调度器不产生堆分配。代价是数据必须符合 Native 内存的规则，逻辑也不能有分支复杂的状态机（Burst 不支持异常、不支持虚函数调用）。

---

## Leak Detection 模式

编辑器下，Unity 会在 NativeContainer 被 GC 回收（而不是 Dispose）时打印警告——这是 Native 内存泄漏最直接的信号。

更详细的泄漏定位：

```csharp
// 在 Awake 或初始化时开启，带调用栈的泄漏追踪
void Awake()
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
#endif
}
```

`EnabledWithStackTrace` 模式在泄漏的 NativeContainer 被 GC 回收时，打印分配时的完整调用栈——精确定位到是哪一行 `new NativeArray` 没有配对 `Dispose`。代价是每次分配都额外记录调用栈，有几个百分点的性能开销，只在调试时开启。

`JobsUtility.JobDebuggerEnabled`（编辑器默认 `true`）控制 Safety Handle 的启用，关闭后 Job 冲突检测也会关闭，通常不需要手动改动。

---

## 一张决策表

| 使用场景 | 推荐 Allocator |
|---|---|
| 帧内用完即弃，不传给 Job | `Temp` |
| 传给 Job，Job 在 4 帧内完成 | `TempJob` |
| 跨场景、组件生命周期级别的持久数据 | `Persistent` |

Job System 的内存模型要求比 C# 堆严格得多：分配时选对 Allocator，Job 完成后立即释放，生命周期由代码决定而不是由 GC 决定。但正是这种严格，让你可以在工作线程上做毫无 GC 压力的批量计算。

下一篇进入这个体系的性能上限：Burst 编译器为什么能让同样的 Job 快几倍，答案在 CPU 缓存行和数据布局里。
