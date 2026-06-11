# Unity 内存管理系列 文章设计 Spec

## 元信息

- 系列标题：Unity 内存管理：从 GC 到 GPU
- 发布平台：知乎
- 目标读者：Unity 中级/进阶开发者（有实际项目经验，遇到过内存问题但不清楚根因）
- 语言：简体中文
- 语气：自然、专业，问题驱动，承接系列前几篇风格
- 校准等级：INTERMEDIATE → ADVANCED
- 篇数：6 篇独立文章，构成完整系列

---

## 系列叙事策略

**问题驱动，层层深入**：每篇从一个真实的开发症状切入（帧率抖动、内存泄漏、低端机 OOM 等），往原理方向挖，收尾落到可操作的判断标准或设计原则。不做工具操作教程，默认读者会使用 Unity Profiler / Memory Profiler；工具相关内容只说"看什么数字、识别什么特征"。

代码以 before/after 对比为主，关键数据用表格展示。每篇结尾给一个清晰的总结表或判断流程，方便读者快速复习。

---

## 第1篇：三层内存模型

### 标题（参考）
`搞懂 Unity 内存的第一步：分清三层`

### 叙事策略

用三种开发中常见的"迷惑症状"开篇：帧率周期性抖动、内存监控数字持续上涨、低端机特定场景闪退。三个症状根因完全不同，分布在三个内存层。文章目标是建立贯穿全系列的心智框架，让读者在遇到内存问题时首先知道去哪一层找根因。

### 核心内容

**三层的边界与归属**

| 内存层 | 别名 | 分配者 | 释放者 | 存什么 |
|---|---|---|---|---|
| Managed | GC 堆 | C# runtime | GC 自动回收 | C# 对象（class 实例、数组、字符串） |
| Native | 引擎堆 | Unity C++ 引擎 | 引用计数归零 | 资产原始数据（贴图像素、音频 PCM、网格顶点） |
| GPU VRAM | 显存 | 图形驱动 | 显式上传/释放 | 贴图 GPU 副本、顶点/索引缓冲、RenderBuffer |

**三层之间的关键误解**

- `new Texture2D()` 同时在 Managed 层分配 C# 包装对象，在 Native 层分配像素数据——两件事，两条释放路径
- `Destroy(gameObject)` 销毁 GameObject 和 Component，不销毁资产；GC 回收 C# 包装对象，Native 数据依然存活
- 贴图上传 GPU 后，CPU 侧（Native）和 GPU 侧（VRAM）可能同时存在两份数据

**实例化一个带贴图 Prefab 时三层发生了什么**

用一个具体的 Prefab 实例化过程，逐步说明三层各自的分配动作，建立直觉。

**系列路线图**：简要介绍后续五篇各自聚焦哪一层，帮助读者按需取用。

---

## 第2篇：GC 与托管内存

### 标题（参考）
`Unity GC 深度解析：为什么 GC Spike 总是找上你`

### 叙事策略

从"GC Spike 为什么周期性出现"切入，往下讲 GC 的工作机制，再往上回到"哪些代码在持续制造分配"，最后给出零分配的设计原则。GC 机制和分配来源相互呼应：理解了 GC 怎么工作，才知道为什么零分配重要。

### 核心内容

**GC 工作机制**

- Boehm GC 的 stop-the-world：GC 堆满时触发，主线程暂停扫描全堆，暂停时长从几毫秒到几十毫秒不等
- 增量 GC（Unity 2019+）：把 GC 工作拆分到多帧执行，把大卡顿变成小卡顿——但不减少总分配量，根本解法仍是少分配
- GC 触发条件：堆空间耗尽；也可手动调用 `GC.Collect()`（绝大多数情况不应该这样做）

**什么操作产生堆分配**

- `new` 一个 class 实例：明确分配
- 装箱（boxing）：值类型赋给 `object` 或 `interface` 变量时产生堆分配
- 闭包（closure）：lambda/匿名方法捕获外部变量时，编译器生成一个堆对象存储捕获的变量
- 迭代器状态机：`IEnumerator`/`yield return` 方法被编译为状态机 class，每次调用产生分配
- `params` 数组：调用带 `params` 参数的方法且传入多个参数时，编译器隐式 `new` 一个数组

**Unity 特有的隐藏分配源**

每种给 before/after 代码对比：

- **`foreach` 装箱**：对实现了非泛型 `IEnumerable` 的集合（`ArrayList`、`Hashtable`）使用 `foreach`，每次迭代产生 `IEnumerator` 装箱。改用泛型集合（`List<T>`、`Dictionary<K,V>`）。
- **Coroutine yield 分配**：`yield return new WaitForSeconds(t)` 每次执行都分配新对象。缓存实例复用。
- **Delegate/event 订阅**：向 `event` 添加/移除 lambda 时，每次 `+=` 都可能产生一个 delegate 对象（取决于是否捕获变量）。
- **LINQ**：链式调用产生大量中间迭代器对象和闭包，热路径（Update、碰撞回调）里禁用。
- **Enum 做字典 key 的装箱**：部分 Unity 版本中 `Dictionary<MyEnum, T>` 使用默认 `EqualityComparer` 会产生装箱，需要自定义 `IEqualityComparer`。
- **字符串拼接**：`"HP: " + hp` 在热路径中每帧产生新字符串对象，改用 `StringBuilder` 或 `string.Format` 预分配缓冲区。

**零分配设计原则**

- **struct 使用原则**：轻量数据（≤ 16 字节）、无需多态、无需共享引用时选 struct；注意大 struct 的值拷贝开销，传参加 `in`/`ref`
- **`Span<T>` 与 `stackalloc`**：对临时小数组使用栈分配，避免堆分配；`Span<T>` 作为方法参数替代数组传递
- **缓存与复用**：`WaitForSeconds`、`StringBuilder`、委托实例的缓存模式
- **无分配事件系统**：用泛型接口 + struct 回调替代 delegate event，消除订阅时的分配

**Profiler 信号**

- GC Alloc 列：每帧新增分配量，理想值 0，实际项目控制在几 KB 以内
- 定位高频分配源：按 GC Alloc 列排序，找到分配大户所在的调用栈

---

## 第3篇：Native 内存与资产生命周期

### 标题（参考）
`Unity 资产生命周期：Destroy 了，内存为什么还在涨`

### 叙事策略

从"`Destroy` 了 GameObject，内存监控数字还在涨"这个经典困惑切入，揭示资产有独立于 GameObject 的生命周期，核心机制是引用计数。然后把三条加载路径（Resources / AssetBundle / Addressables）对引用计数的影响逐一讲清楚，最后总结常见泄漏模式。

### 核心内容

**Unity 引擎的引用计数机制**

Unity 对每份 Native 资产（`Texture2D`、`AudioClip`、`Mesh`、`Material` 等）维护内部引用计数：

- 引用计数 > 0：资产驻留 Native 内存
- 引用计数 = 0：资产可被释放（不一定立即释放）
- GC 回收 C# 包装对象，不影响引用计数；只有显式卸载调用才能让计数归零

**三条加载路径对引用计数的影响**

| 加载方式 | 引用计数 +1 时机 | 引用计数 -1 / 释放时机 |
|---|---|---|
| `Resources.Load` | 调用 `Load` 时 | `Resources.UnloadAsset` 或 `UnloadUnusedAssets` |
| AssetBundle | `LoadAsset` 时 | `bundle.Unload(true)` 或 `UnloadUnusedAssets` |
| Addressables | `LoadAssetAsync` 完成时 | `Addressables.Release(handle)` |

重点说明：Addressables 的 handle 必须显式 Release，忘记 Release 是最常见的 Addressables 泄漏原因。

**`Destroy()` 的解剖**

- `Destroy(gameObject)`：销毁场景中的 GameObject 和挂载的 Component，对资产引用计数无影响
- `Destroy(material)`：销毁动态创建的 Material 实例，引用计数 -1（这是对的用法）
- 调用时机：`Destroy` 是延迟的，实际销毁发生在当帧结束时

**常见泄漏模式（每种给说明 + 修复方式）**

- **`renderer.material` 自动实例化**：访问 `renderer.material` 时 Unity 自动创建一份 Material 实例，引用计数 +1，原材质卸载后实例仍驻留。读取材质属性只用 `renderer.sharedMaterial`；确实需要独立实例时，场景/对象销毁时显式 `Destroy(renderer.material)`
- **静态字段跨场景持有**：挂在静态字段上的资产引用在场景卸载后不会自动清除，引用计数无法归零。场景卸载回调（`SceneManager.sceneUnloaded`）里显式置空
- **异步加载后取消但 handle 未释放**：Addressables 异步操作被取消，但 handle 没有 Release，资产永久驻留
- **动态创建的资产未销毁**：运行时 `new Texture2D()`、`new Material()` 等，对象离开作用域后 C# 包装被 GC 回收，Native 数据不释放。显式调用 `Destroy(texture)` / `Destroy(material)`

**`Resources.UnloadUnusedAssets()` 的原理与局限**

全资产扫描，释放所有引用计数为零的资产；但扫描开销大（毫秒级到几十毫秒级），只适合场景切换等非实时场景，不能作为常规内存管理手段。

---

## 第4篇：GPU 显存实战

### 标题（参考）
`GPU 显存账本：为什么低端机总在这个场景崩`

### 叙事策略

从"CPU 内存显示正常，低端机特定场景还是 OOM"切入，说明 GPU 显存是独立账本。先讲显存里住着什么，再把贴图（最大头）的内存数学讲透，最后给其他常见显存问题的判断方法。

### 核心内容

**GPU 显存的构成**

- **贴图**：通常占 GPU 显存的 60–80%，是优化的首要目标
- **网格缓冲**（顶点/索引 buffer）：静态场景几乎可忽略，骨骼动画 + 大量蒙皮网格时才显著
- **RenderBuffer**：深度缓冲、颜色缓冲、Shadow Map、后处理 RT；分辨率越高占用越大

**贴图内存数学**

公式：`内存 = 宽 × 高 × 每像素字节数 × mip 系数`

mip 系数：开启 Mip Maps 时约为 1.33（1 + 1/4 + 1/16 + … ≈ 4/3）

| 格式 | 每像素字节数 | 1024×1024（无 mip） | 1024×1024（有 mip） |
|---|---|---|---|
| RGBA32（无压缩） | 4 字节 | 4 MB | 5.3 MB |
| DXT5 / BC3 | 1 字节 | 1 MB | 1.3 MB |
| ETC2（RGBA） | 1 字节 | 1 MB | 1.3 MB |
| ASTC 4×4 | 1 字节 | 1 MB | 1.3 MB |
| ASTC 8×8 | 0.25 字节 | 0.25 MB | 0.33 MB |

**压缩格式选择原则**

- iOS：优先 ASTC（4×4 质量好，8×8 省显存）
- Android：ASTC（高端机）/ ETC2（OpenGL ES 3.0+ 设备）/ ETC1（不支持透明通道，需分离）
- PC：DXT5（BC3）或 BC7
- UI 贴图：关闭 Mip Maps（像素对齐，无透视缩放，mip 无意义）
- 3D 场景贴图：开启 Mip Maps（避免过采样伪影，实际省显存带宽）

**`Read/Write Enabled` 的代价**

勾选后 Unity 在 Native 内存保留贴图完整副本，导致同一份贴图同时占用 Native 和 GPU 两份内存。只有运行时需要通过 `GetPixels` / `SetPixels` 读写像素时才开启，其他情况关闭。

**`RenderTexture` 的生命周期**

临时 RenderTexture 用完不调用 `RenderTexture.ReleaseTemporary` 的泄漏模式：每帧申请不释放，显存持续增长。正确用法：`RenderTexture.GetTemporary` 配对 `RenderTexture.ReleaseTemporary`，或手动 `rt.Release()` + `Destroy(rt)`。

**`Mesh.UploadMeshData(true)`**

调用后 Unity 把网格数据上传 GPU 并释放 Native 侧副本，节省约一半网格内存。代价：之后无法通过 CPU 读取顶点数据（`mesh.vertices` 返回空）。适用场景：运行时不再修改的静态网格。

**Sprite Atlas vs 散图**

散图每张独立上传，GPU 切换贴图有 Draw Call 开销；Atlas 合并为一张，减少 Draw Call 同时减少显存碎片。注意 Atlas 尺寸上限（4096×4096），超出后自动分包。

---

## 第5篇：Job System 与 NativeContainer

### 标题（参考）
`走出 GC 堆：Job System 的内存模型`

### 叙事策略

从"想彻底消除 GC 压力，但 Job 里不能用 C# 对象"切入，解释走出 GC 堆意味着进入 Unity 的 Native 内存分配体系，有一套完全不同的规则。重点讲清楚 Allocator 类型的适用场景和 Safety Handle 机制——这两块最容易踩坑。

### 核心内容

**Job System 为什么不能用 Managed 对象**

- Job 在工作线程上执行，访问 GC 堆需要 GC 感知（GC 扫描时工作线程需要暂停），破坏线程安全假设
- Burst 编译器要求输入输出类型是 Blittable（可直接内存映射），class 对象含托管指针，不满足 Blittable
- 结论：Job 的数据必须放在 Native 内存中，通过 NativeContainer 访问

**NativeContainer 主要类型**

| 类型 | 用途 |
|---|---|
| `NativeArray<T>` | 固定长度数组，最常用 |
| `NativeList<T>` | 可变长度列表（需 `Unity.Collections` 包） |
| `NativeHashMap<K,V>` | 键值对映射 |
| `NativeQueue<T>` | 先进先出队列 |
| `NativeParallelMultiHashMap<K,V>` | 并行写入场景 |

**Allocator 三种类型**

| Allocator | 生命周期 | 适用场景 | 忘记 Dispose 的后果 |
|---|---|---|---|
| `Allocator.Temp` | 单帧（最长 4 帧） | 帧内临时数据 | 编辑器下报警告，自动释放 |
| `Allocator.TempJob` | 4 帧以内 | Job 执行期间的临时数据 | 超出 4 帧报错 |
| `Allocator.Persistent` | 手动管理 | 跨帧长期存在的数据 | 内存泄漏，编辑器退出时报错 |

用错 Allocator 的典型后果：把 `Temp` 分配的数组传给跨帧的 Job，Job 执行时内存已被框架回收，导致读到脏数据。

**Safety Handle 系统**

Unity 给每个 NativeContainer 附加一个 Safety Handle，在编辑器下（`ENABLE_UNITY_COLLECTIONS_CHECKS` 宏）追踪读写访问：

- 同一容器不能同时被两个 Job 写入（AtomicSafetyHandle 检测并抛异常）
- 主线程在 Job 完成前不能读写该容器（`JobHandle.Complete()` 前访问会抛异常）
- Safety Handle 仅在 Editor 和 Development Build 下生效，Release Build 下不检查（性能原因）

**正确的 Dispose 模式**

```csharp
// 单帧用完立即释放
var array = new NativeArray<float>(1024, Allocator.Temp);
// ... 使用 array
array.Dispose();

// 跨帧：OnDestroy 或明确的生命周期点释放
NativeList<int> _list;
void OnEnable() => _list = new NativeList<int>(Allocator.Persistent);
void OnDisable() => _list.Dispose();

// Job 完成后释放：用 JobHandle 延迟 Dispose
NativeArray<float> data = new NativeArray<float>(count, Allocator.TempJob);
JobHandle handle = new MyJob { Data = data }.Schedule();
data.Dispose(handle); // handle 完成后自动释放，不阻塞主线程
```

**Coroutine vs Job 内存视角对比**

| 对比维度 | Coroutine | Job |
|---|---|---|
| 数据存放 | GC 堆（class 对象、捕获变量） | Native 内存（NativeContainer） |
| 状态机 | 编译器生成 class，每次 `yield` 产生分配 | struct IJob，无堆分配 |
| 并发 | 单线程协作式 | 工作线程并行 |
| 适用场景 | 时序逻辑、UI 动画 | 批量数据处理、物理/粒子更新 |

**Leak Detection 模式**

`JobsUtility.JobDebuggerEnabled`（编辑器默认开启）配合 Memory Leak Detection（`NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace`）可以定位到泄漏的分配点。

---

## 第6篇：Burst 与内存布局

### 标题（参考）
`Burst 为什么快：从缓存行到数据布局`

### 叙事策略

从"加了 `[BurstCompile]` 快了数倍但不理解为什么"切入，引出 CPU 缓存命中率这个核心概念。然后从物理原理到代码实践，讲清楚内存布局如何决定性能上限。最后以 ECS 的 Archetype/Chunk 布局作为"数据导向设计把前面所有原则整合起来"的收尾。

### 核心内容

**缓存行（Cache Line）工作原理**

- CPU 访问内存以 64 字节为单位（Cache Line），访问一个字节时会把整个 Cache Line 载入 L1 缓存
- 顺序访问（Sequential Access）：相邻数据在同一 Cache Line，缓存命中率高，内存带宽利用率高
- 随机访问（Random Access）：每次跳跃到新地址都可能触发 Cache Miss，L1 缓存失效，代价约 100–300 个 CPU 周期

**AoS vs SoA**

用一个粒子系统（位置 + 速度 + 生命值）说明两种布局：

```csharp
// AoS（Array of Structs）
struct Particle { float3 position; float3 velocity; float lifetime; }
Particle[] particles; // 每个 Particle 28 字节，顺序存储

// SoA（Struct of Arrays）
float3[] positions;
float3[] velocities;
float[] lifetimes;   // 只访问 lifetime 时，数据密集，Cache Line 全部有效
```

只更新 `lifetime` 时，AoS 方式每个 Cache Line（64 字节）只有 4 字节有效数据（lifetime），利用率 6%；SoA 方式 Cache Line 完全填满 lifetime 数据，利用率 100%。

实测对比：在 1M 粒子上只更新 lifetime，SoA 相比 AoS 快约 5–8 倍（缓存命中率差异）。

**Blittable 类型**

Burst 只接受 Blittable 类型（可直接内存映射到非托管内存，布局与 C/C++ 一致）：
- ✅ 基础数值类型（`int`、`float`、`double`）
- ✅ 只包含 Blittable 字段的 struct
- ❌ `bool`（在 C# 中不是 1 字节保证）→ 用 `byte` 替代
- ❌ 含引用类型字段的 struct（`string`、`class`）
- ❌ 数组（托管对象）→ 用 `NativeArray<T>` 替代

**struct 内存对齐与填充**

C# 编译器会对 struct 字段进行对齐填充，导致实际 sizeof 大于字段之和：

```csharp
struct Bad  { byte a; float b; byte c; } // sizeof = 12（中间有 3+3 字节填充）
struct Good { float b; byte a; byte c; } // sizeof = 8（填充减少）
```

字段按大小降序排列可减少填充。用 `[StructLayout(LayoutKind.Explicit)]` 手动控制偏移，适用于需要精确内存布局的场景。

**`UnsafeUtility`**

跳过 Safety Handle 直接操作 Native 内存指针的工具类。适用边界：

- 实现自定义 NativeContainer（框架级代码）
- Burst 内部实现性能极关键的内存操作（`UnsafeUtility.MemCpy`、`MemMove`）
- 日常业务代码不应触碰，一旦出错直接 crash，Safety Handle 不会保护

**ECS 内存布局：数据导向设计的终点**

ECS 的 Archetype + Chunk 模型是前面所有原则的实际应用：

- **Archetype**：具有相同 Component 组合的 Entity 归为同一 Archetype
- **Chunk**（16 KB）：每个 Archetype 的数据以 Chunk 为单位存储，Chunk 内每种 Component 的数组连续排列（SoA 布局）
- 系统（System）处理某 Archetype 时，顺序遍历 Chunk，缓存命中率接近理论上限

这一节作为系列收尾，展示把零 GC + Native 内存 + 数据布局全部整合后，性能上限在哪里。

---

## 代码规范（全系列适用）

- 错误写法：给具体代码示例，不只是文字描述
- 正确写法：给完整可运行的对比代码
- 所有代码块加 `csharp` 语言标识
- 内存数字对比用表格，不用代码
- 伪代码需注释 `// 示意` 或 `// 伪代码`

---

## 各篇范围边界

| 篇 | 不在范围内 |
|---|---|
| 第1篇 | 各层的具体优化手段（留给后续篇） |
| 第2篇 | IL2CPP vs Mono 的 GC 差异；DOTS NativeContainer |
| 第3篇 | Addressables 的完整用法（假设读者已了解）；AssetBundle 打包策略 |
| 第4篇 | Shader 变体内存；渲染管线（URP/HDRP）额外开销；平台具体 OOM 阈值 |
| 第5篇 | Burst 编译原理；ECS 完整用法 |
| 第6篇 | ECS 的 System 设计；DOTS 完整工作流 |
