# Burst 为什么快：从缓存行到数据布局

给 Job 加上 `[BurstCompile]`，同样的代码跑出来快了五倍。但 Burst 并没有改动你的算法，循环还是那个循环，计算还是那些计算——快在哪？

答案不在 Burst 编译器里，在 CPU 缓存里。Burst 帮你把代码编译成 SIMD 向量指令，让 CPU 一次处理 4–8 个浮点数而不是 1 个；但它能这么做的前提，是你的数据在内存里是连续排列的——CPU 从内存里取数据，一次取一整块（Cache Line，64 字节），如果数据散落各处，CPU 大半时间在等内存，再快的指令也没有意义。

这一篇从 CPU 缓存行讲起，解释为什么数据布局决定性能上限，再给出 Burst 代码里的几个具体要求（Blittable 类型、struct 对齐），最后用 ECS 的 Archetype/Chunk 布局作为整个系列的收尾——它是把前五篇所有原则整合到极致之后的样子。

---

## 缓存行：CPU 访问内存的基本单位

CPU 访问内存不是按字节取的，而是按 **Cache Line** 取的，每个 Cache Line 64 字节。读一个 `float`（4 字节），CPU 实际从内存搬来 64 字节，把整个 Cache Line 塞进 L1 缓存。下次访问同一个 Cache Line 里的其他字节，直接从 L1 里拿，速度极快（约 4 个时钟周期）。如果要访问的地址不在缓存里（Cache Miss），CPU 必须去 L2、L3 甚至主存取，代价是 100–300 个时钟周期的等待。

```
L1 缓存命中：~4 周期
L2 缓存命中：~12 周期
L3 缓存命中：~40 周期
主存（Cache Miss）：~200 周期
```

在 3 GHz 的 CPU 上，200 周期约等于 66 ns。对于一个处理 100 万个元素的循环，如果每次访问都 Cache Miss，等待时间就是 66 ms——正好是 60 fps 的一整帧预算。

"超市货架"类比：每次取货，超市不管你要几件，都给你推一整托盘过来（64 字节的 Cache Line）。如果你要的货都在同一托盘上（顺序访问、数据紧凑），效率极高；如果每次取货都要等超市从不同仓库调不同托盘（随机访问、数据分散），你大部分时间在等托盘，不在用货。

---

## AoS vs SoA：数据布局决定缓存命中率

以粒子系统为例，每个粒子有三个属性：位置、速度、生命值。有两种存储方式：

**AoS（Array of Structs）**：每个粒子的所有属性放在一个 struct 里，数组里存 struct：

```csharp
// AoS
struct Particle
{
    public float3 Position; // 12 字节
    public float3 Velocity; // 12 字节
    public float  Lifetime; //  4 字节
}                           // 合计 28 字节（对齐后可能是 32 字节）

NativeArray<Particle> particles;
```

**SoA（Struct of Arrays）**：每种属性独立成一个数组：

```csharp
// SoA
NativeArray<float3> positions;
NativeArray<float3> velocities;
NativeArray<float>  lifetimes;
```

现在有一个 Job，每帧只需要更新 `Lifetime`（减去 deltaTime，生命值到 0 的粒子标记为死亡）：

```csharp
// 只访问 Lifetime 字段
struct UpdateLifetimeJob : IJobParallelFor
{
    public float DeltaTime;
    public NativeArray<Particle> Particles; // AoS

    public void Execute(int i)
    {
        var p = Particles[i];
        p.Lifetime -= DeltaTime;
        Particles[i] = p;
    }
}
```

**AoS 的缓存命中情况**：每个 `Particle` 是 32 字节。一个 Cache Line（64 字节）能装 2 个 `Particle`。但 Job 只需要每个 Particle 里的 4 字节 `Lifetime`，另外 28 字节（Position、Velocity）被搬进 Cache 但完全没用。Cache Line 有效利用率：4 / 64 = **6%**。

**SoA 的缓存命中情况**：`lifetimes` 是 `float` 数组，每个元素 4 字节。一个 Cache Line（64 字节）能装 16 个 `float`。Job 需要的每个字节都是有效数据，Cache Line 利用率：**100%**。

实测对比：1M 粒子只更新 Lifetime，SoA 比 AoS 快约 5–8 倍（典型测量值，随硬件和数据量浮动）。这就是 Burst 能发挥出几倍加速的前提——SIMD 指令一次处理 8 个 float，但如果数据利用率只有 6%，SIMD 也只是加速了 6% 的有效工作。

SoA 的 Job 写法：

```csharp
struct UpdateLifetimeSoAJob : IJobParallelFor
{
    public float DeltaTime;
    public NativeArray<float> Lifetimes;

    public void Execute(int i)
    {
        Lifetimes[i] -= DeltaTime;
    }
}
```

更简洁，更快。代价是属性分散在多个数组，代码组织上不如 AoS 直觉。需要同时访问多个属性（比如同时用 Position 和 Lifetime 做渲染剔除）时，SoA 要同时传入多个数组参数，接口稍显繁琐——这是两种布局的真实权衡，按实际访问模式选择。

---

## Blittable 类型：Burst 能处理什么

Burst 只接受 **Blittable** 类型——在托管内存和非托管内存中二进制布局完全一致、不含托管指针的类型。

| 类型 | Blittable？ | 说明 |
|---|---|---|
| `int`、`float`、`double`、`long` 等基础数值类型 | ✅ | 直接映射 |
| `float2`、`float3`、`float4`（Unity.Mathematics） | ✅ | 纯 struct，全是 float |
| 只包含 Blittable 字段的 `struct` | ✅ | 递归满足即可 |
| `bool` | ❌ | C# 中 `bool` 的大小不保证为 1 字节，Burst 拒绝 |
| `char` | ❌ | 托管类型，编码不确定 |
| 含 `string`、`class` 字段的 `struct` | ❌ | 包含托管指针 |
| `T[]`（托管数组） | ❌ | 托管对象，用 `NativeArray<T>` 替代 |
| `List<T>` | ❌ | 托管对象，用 `NativeList<T>` 替代 |

**`bool` 的替代方案**：

```csharp
// 错误：Burst Job struct 里用 bool
struct BadJob : IJob
{
    public bool IsActive; // Burst 编译报错
    public void Execute() { /* ... */ }
}

// 正确：用 byte 代替 bool，0 = false，1 = true
struct GoodJob : IJob
{
    public byte IsActive; // Blittable
    public void Execute()
    {
        if (IsActive != 0) { /* ... */ }
    }
}
```

Unity.Mathematics 提供了 `bool`-like 的向量类型（`bool4` 等），它们内部实现是整数，是 Blittable 的，可以在 Burst Job 里使用。

---

## struct 内存对齐：隐藏的填充字节

C# 编译器对 struct 字段做内存对齐：每个字段的起始地址必须是该字段大小的整数倍。如果字段排列不当，编译器会插入填充字节（Padding），导致 struct 实际大小大于字段之和。

```csharp
struct Bad
{
    public byte  A; //  1 字节，偏移 0
                    //  3 字节 padding（等待 float 的 4 字节对齐）
    public float B; //  4 字节，偏移 4
    public byte  C; //  1 字节，偏移 8
                    //  3 字节 padding（struct 整体对齐到 4 字节）
}
// sizeof(Bad) = 12 字节（字段实际只有 6 字节）

struct Good
{
    public float B; //  4 字节，偏移 0
    public byte  A; //  1 字节，偏移 4
    public byte  C; //  1 字节，偏移 5
                    //  2 字节 padding（对齐到 4 字节）
}
// sizeof(Good) = 8 字节（字段 6 字节，padding 减少到 2 字节）
```

字段按**大小降序排列**可以最小化 Padding：先放最大的字段，再放小的，编译器需要插入的填充字节最少。

对于需要精确控制内存布局的场景（比如和 C 侧结构体互操作），可以用 `[StructLayout]` 显式指定：

```csharp
[StructLayout(LayoutKind.Explicit)]
struct Packed
{
    [FieldOffset(0)] public float X;
    [FieldOffset(4)] public float Y;
    [FieldOffset(8)] public byte  Flags;
    // 明确指定每个字段的偏移，不依赖编译器对齐规则
}
```

在 Burst Job 里，NativeArray 里每个 struct 元素的大小直接影响 Cache Line 利用率——Bad 版本每个 Cache Line 只装 5 个元素，Good 版本能装 8 个。对于 100 万粒子的批处理，这个差异可能占到 10–20% 的性能差距。

---

## UnsafeUtility：最后的手段

`Unity.Collections.LowLevel.Unsafe.UnsafeUtility` 提供了跳过 Safety Handle、直接操作 Native 内存指针的能力：

```csharp
// 直接内存拷贝，跳过所有安全检查
UnsafeUtility.MemCpy(dest, src, byteCount);

// 获取 NativeArray 的底层指针
void* ptr = NativeArrayUnsafeUtility.GetUnsafePtr(array);
```

适用边界：

- 实现自定义 NativeContainer（框架级代码，日常业务不应涉及）
- Burst 内部做极致性能优化的内存操作（`MemCpy`、`MemMove`、`MemSet`）

日常业务代码**不应触碰** UnsafeUtility。它完全绕过 Safety Handle，出错不会有异常提示，直接内存踩踏，轻则数据错乱，重则进程 crash，且崩溃现场和根因之间可能隔了十几帧。NativeContainer 提供的 API 已经足够覆盖绝大多数场景，看到"这里用 UnsafeUtility 更方便"的想法时，先退一步想是不是数据结构设计有问题。

---

## ECS 内存布局：数据导向设计的终点

ECS（Entity Component System）的 Archetype + Chunk 布局是把本系列所有原则——零 GC、Native 内存、SoA 布局——整合起来之后，性能上限在哪里的答案。

**Archetype**：具有完全相同 Component 组合的 Entity，归为同一个 Archetype。比如所有同时拥有 `Position`、`Velocity`、`Health` 三种 Component 的 Entity，都属于同一个 Archetype。

**Chunk**：每个 Archetype 的数据以 Chunk 为单位存储，每个 Chunk 固定 16 KB。Chunk 内部采用 SoA 布局：所有 Entity 的 `Position` 数组连续排列，所有 Entity 的 `Velocity` 数组连续排列，依此类推。

```
Chunk（16 KB）内部布局示意（Archetype = Position + Velocity + Health）：

[Position[0], Position[1], ..., Position[N]]   ← 连续 float3 数组
[Velocity[0], Velocity[1], ..., Velocity[N]]   ← 连续 float3 数组
[Health[0],   Health[1],   ..., Health[N]]     ← 连续 float  数组

N ≈ 16384 / (12 + 12 + 4) bytes ≈ 585 个 Entity
```

System 处理某个 Archetype 时，按 Chunk 顺序遍历：先处理第一个 Chunk 里的所有 Entity，再处理第二个 Chunk，以此类推。对 CPU 来说，这是最理想的顺序访问模式——每个 Cache Line 里全部是有效数据，缓存命中率接近理论上限。

同样的 Entity 数量，传统 OOP（每个 MonoBehaviour 是一个堆对象，随机分布在 GC 堆里）vs ECS（SoA Chunk，连续内存）的性能差距可以达到一个数量级。这不是 ECS 框架的魔法，而是 CPU 缓存的物理规律——前五篇讲的所有原则都在这里汇集：
- **零 GC 分配**（第2篇）：Entity 和 Component 都在 Native 内存里，不产生 GC 压力
- **显式 Native 内存管理**（第5篇）：World 创建时分配 Persistent 内存，销毁时释放
- **SoA 数据布局**（本篇）：Chunk 的内部布局天然是 SoA
- **Blittable 类型**（本篇）：IComponentData 必须是 Blittable struct
- **Burst 编译**（本篇）：System 加 `[BurstCompile]`，在完美缓存利用率的基础上再叠 SIMD 向量化

---

## 全系列回顾

走到这里，六篇的核心原则可以压缩成一张表：

| 篇 | 核心原则 |
|---|---|
| 第1篇·三层内存模型 | 遇到内存问题，先判断在哪一层；三层的释放路径完全不同 |
| 第2篇·GC 与托管内存 | 减少分配才能减少 GC；增量 GC 不减少分配量，只分摊停顿 |
| 第3篇·Native 内存与资产生命周期 | 资产生命周期独立于 GameObject，引用计数归零才释放 |
| 第4篇·GPU 显存实战 | 贴图是显存最大头；压缩格式和 Read/Write 是最直接的杠杆 |
| 第5篇·Job System 与 NativeContainer | 走出 GC 堆需遵循 Native 内存规则：选对 Allocator，显式 Dispose |
| 第6篇·Burst 与内存布局 | 数据布局决定缓存命中率；缓存命中率决定 Burst 能发挥多少 |

内存问题的本质是分层的：Managed 层的 GC Spike 在于减少分配；Native 层的资产泄漏在于管好引用计数；GPU 层的 OOM 在于控制格式和显式释放；Native 分配体系的 Job/Burst 在于让数据对 CPU 友好。每一层有每一层的规则，混着处理就会南辕北辙。现在这套分层框架在手，再遇到内存问题，至少知道从哪一层开始挖。
