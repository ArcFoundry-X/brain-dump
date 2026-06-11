# Unity 内存管理系列 写作计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 写完 6 篇 Unity 内存管理系列文章，发布到知乎，面向中级/进阶 Unity 开发者。

**Architecture:** 每篇独立成文，按"症状 → 原理 → 实践"叙事结构展开；代码以 before/after 对比为主；关键数据用表格。写作顺序与阅读顺序一致（第1篇先写，后续篇可引用前篇概念）。

**Tech Stack:** Markdown、简体中文、Unity C#

**Spec 位置:** `docs/superpowers/specs/2026-05-26-unity-memory-series-design.md`

---

## 输出文件

| 篇 | 输出路径 |
|---|---|
| 第1篇 | `ToolkitInPorject/Memory_ThreeLayers_Zhihu.md` |
| 第2篇 | `ToolkitInPorject/Memory_GC_Zhihu.md` |
| 第3篇 | `ToolkitInPorject/Memory_Native_Zhihu.md` |
| 第4篇 | `ToolkitInPorject/Memory_GPU_Zhihu.md` |
| 第5篇 | `ToolkitInPorject/Memory_JobSystem_Zhihu.md` |
| 第6篇 | `ToolkitInPorject/Memory_Burst_Zhihu.md` |

---

## 各篇通用验收标准

每篇完成后对照以下清单：

- [ ] 开篇第一段有具体的症状或痛点，不以"本文将介绍"开头
- [ ] 每个"错误写法"都有对应的"正确写法"代码（before/after 成对）
- [ ] 所有数据表格的数字有来源依据或明确标注"示意值"
- [ ] 没有遗漏 spec 中列出的核心内容点
- [ ] 结尾有总结表或判断清单

---

## Task 1：第1篇——三层内存模型

**输出文件：** `ToolkitInPorject/Memory_ThreeLayers_Zhihu.md`

**Spec 对应节：** 第1篇

**核心内容要求：**
- 三种开发症状作为开篇（帧率抖动 / 内存持续上涨 / 低端机闪退）
- 三层边界表格（Managed / Native / GPU，分配者、释放者、存什么）
- 三个关键误解（`new Texture2D()` 的双重分配、`Destroy` ≠ 释放资产、GC 回收 ≠ 贴图卸载）
- 实例化一个带贴图 Prefab 时三层各自发生了什么（具体化说明）
- 系列路线图（简要介绍后续五篇聚焦点）

- [ ] **Step 1：写文章骨架**

  创建文件，只写标题和各节 H2/H3，不填正文：

  ```markdown
  ## 三种症状，三个根因

  ## Managed、Native、GPU：三层的边界

  ### 三层是什么

  ### 三层之间的误解

  ## 实例化一个 Prefab，三层里发生了什么

  ## 系列路线图
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  打开 `docs/superpowers/specs/2026-05-26-unity-memory-series-design.md` 第1篇节，逐条对比核心内容要求，确认骨架无遗漏。

- [ ] **Step 3：展开正文**

  按骨架逐节写完正文。要点：
  - 开篇三个症状用一句话点出，不展开细节，留给后续篇
  - 三层表格直接放在"三层是什么"小节里
  - "误解"部分每条给一个具体代码反例，不超过 5 行
  - Prefab 实例化过程用有序列表逐步描述，不用流程图

- [ ] **Step 4：写结尾总结**

  文末给一张三行总结表：

  | 内存层 | 典型症状 | 释放路径 |
  |---|---|---|
  | Managed | 帧率周期性抖动 | GC 自动回收 |
  | Native | 内存持续上涨 | 引用计数归零 |
  | GPU | 低端机 OOM | 显式卸载/压缩格式 |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。

---

## Task 2：第2篇——GC 与托管内存

**输出文件：** `ToolkitInPorject/Memory_GC_Zhihu.md`

**Spec 对应节：** 第2篇

**核心内容要求：**
- GC Spike 症状作为开篇
- Boehm GC stop-the-world 机制 vs 增量 GC（增量 GC 不是银弹）
- 什么操作产生堆分配（装箱、闭包、迭代器状态机、params 数组）
- Unity 特有隐藏分配源，每种配 before/after 代码：
  - `foreach` + 非泛型集合
  - Coroutine `yield return new WaitForSeconds`
  - delegate/event 订阅与 lambda 捕获
  - LINQ 热路径
  - enum 做字典 key 的装箱
  - 字符串拼接
- 零分配设计模式：struct 原则、`Span<T>`/`stackalloc`、缓存复用、无分配事件系统
- Profiler 信号：GC Alloc 列，如何定位高频分配源

- [ ] **Step 1：写文章骨架**

  ```markdown
  ## GC Spike 是什么

  ## GC 怎么工作

  ### Boehm GC：stop-the-world

  ### 增量 GC：分片不是银弹

  ## 哪些代码在制造分配

  ### 显而易见的分配

  ### Unity 里的隐藏分配源

  ## 零分配设计原则

  ### struct 的适用边界

  ### Span<T> 与 stackalloc

  ### 缓存与复用模式

  ### 无分配事件系统

  ## 用 Profiler 定位分配热点
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  确认 6 种隐藏分配源全部在骨架中有对应位置。

- [ ] **Step 3：展开正文**

  要点：
  - Boehm GC 用类比（"扫描全城找垃圾"）帮助建立直觉，不超过两段
  - 增量 GC 明确说明"不减少分配量，只减少单帧停顿"，避免读者误解
  - 6 种隐藏分配源每种：一句话点问题，错误代码（3–8 行），正确代码（3–8 行）
  - struct 原则给一个判断流程：数据 ≤ 16 字节 + 无需多态 + 无需共享 → 考虑 struct
  - `Span<T>` 给一个栈分配临时缓冲区的完整示例

- [ ] **Step 4：写结尾总结**

  给一张"分配来源 → 修复模式"速查表：

  | 分配来源 | 修复模式 |
  |---|---|
  | `foreach` 非泛型集合 | 改用泛型集合 |
  | `yield return new WaitForSeconds` | 缓存实例 |
  | lambda 捕获外部变量 | 抽成方法或缓存 delegate |
  | LINQ 热路径 | 改用 for 循环 |
  | enum 字典 key | 自定义 IEqualityComparer |
  | 字符串拼接 | StringBuilder / 插值缓存 |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。

---

## Task 3：第3篇——Native 内存与资产生命周期

**输出文件：** `ToolkitInPorject/Memory_Native_Zhihu.md`

**Spec 对应节：** 第3篇

**核心内容要求：**
- "`Destroy` 了 GameObject，内存还在涨"作为开篇
- Unity 引擎内部引用计数机制（何时 +1、何时 -1、归零才释放）
- 三条加载路径对引用计数的影响对比表（Resources / AssetBundle / Addressables）
- `Destroy()` 的解剖：销毁 GameObject ≠ 销毁资产
- 常见泄漏模式，每种给说明 + 修复代码：
  - `renderer.material` 自动实例化
  - 静态字段跨场景持有
  - 异步加载取消但 handle 未释放
  - 动态创建的 Material/Texture2D 未 Destroy
- `Resources.UnloadUnusedAssets()` 的原理与局限

- [ ] **Step 1：写文章骨架**

  ```markdown
  ## 现象：Destroy 了，内存还在涨

  ## Unity 资产的引用计数

  ## 三条加载路径：引用计数怎么变

  ## Destroy() 到底销毁了什么

  ## 四种常见泄漏模式

  ### renderer.material 的自动实例化陷阱

  ### 静态字段跨场景持有

  ### 异步加载取消但 handle 未释放

  ### 动态创建的资产未销毁

  ## UnloadUnusedAssets：能用，但有限制
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  确认 4 种泄漏模式全部在骨架中，且 Addressables handle 生命周期有覆盖。

- [ ] **Step 3：展开正文**

  要点：
  - 引用计数机制：用一个 Texture2D 的完整生命周期（`Resources.Load` → 使用 → `UnloadAsset`）串联，不抽象讲
  - 三条加载路径表格放在"三条加载路径"节开头，表格后逐条补充说明 Addressables handle 的特殊性
  - `Destroy()` 解剖：给一个代码示意，明确哪一行销毁了什么、没销毁什么
  - 每种泄漏模式：问题代码（标注哪里出错）→ 修复代码

  `renderer.material` 示例：
  ```csharp
  // 错误：每次访问 renderer.material 都创建新实例
  void Update()
  {
      renderer.material.color = Color.red; // 每帧产生一个 Material 实例
  }

  // 正确：只读属性用 sharedMaterial；需要独立实例时缓存并在销毁时 Destroy
  void Start()
  {
      _mat = renderer.material; // 只创建一次
  }
  void OnDestroy()
  {
      Destroy(_mat);
  }
  ```

- [ ] **Step 4：写结尾总结**

  给一张泄漏诊断速查表：

  | 症状 | 可能根因 | 修复方向 |
  |---|---|---|
  | 场景切换后内存不降 | 静态字段持有资产引用 | sceneUnloaded 回调里置空 |
  | Material 数量只增不减 | renderer.material 未销毁 | 缓存实例 + OnDestroy Destroy |
  | Addressables 内存持续增长 | handle 未 Release | 每次 LoadAsset 配对 Release |
  | 动态贴图内存泄漏 | new Texture2D 未 Destroy | 显式调用 Destroy(texture) |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。

---

## Task 4：第4篇——GPU 显存实战

**输出文件：** `ToolkitInPorject/Memory_GPU_Zhihu.md`

**Spec 对应节：** 第4篇

**核心内容要求：**
- "CPU 内存正常，低端机 OOM"作为开篇
- GPU 显存构成（贴图、网格缓冲、RenderBuffer，贴图是最大头）
- 贴图内存数学：公式 + 格式对比表（RGBA32 / DXT5 / ETC2 / ASTC 4×4 / ASTC 8×8）
- 压缩格式选择原则（iOS / Android / PC 分别怎么选）
- `Read/Write Enabled` 为什么让内存翻倍
- RenderTexture 生命周期：`GetTemporary` 配对 `ReleaseTemporary`
- `Mesh.UploadMeshData(true)` 适用场景和代价
- Sprite Atlas vs 散图显存对比

- [ ] **Step 1：写文章骨架**

  ```markdown
  ## 现象：CPU 内存正常，低端机还是 OOM

  ## GPU 显存里住着什么

  ## 贴图内存怎么算

  ### 内存公式

  ### 格式对比表

  ### 压缩格式选择原则

  ## 三个高频显存问题

  ### Read/Write Enabled：内存翻倍的开关

  ### RenderTexture 泄漏

  ### Mesh.UploadMeshData(true)

  ## Sprite Atlas vs 散图
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  确认格式表数据与 spec 一致（5 种格式 × 1024×1024 占用数字）。

- [ ] **Step 3：展开正文**

  要点：
  - GPU 显存构成：用百分比比例建立直觉（贴图通常占 60–80%）
  - 内存公式明确写出来：`内存 = 宽 × 高 × 每像素字节数 × mip 系数`，mip 系数 ≈ 1.33
  - 格式表：5 行（RGBA32 / DXT5 / ETC2 / ASTC 4×4 / ASTC 8×8），列：格式 / 每像素字节数 / 1024×1024 无 mip / 1024×1024 有 mip
  - 压缩格式选择做成决策树或有序列表（iOS → ASTC，Android → ASTC/ETC2，PC → DXT5）
  - RenderTexture 泄漏给 before/after 代码：

  ```csharp
  // 错误：申请了不释放
  void OnRenderImage(RenderTexture src, RenderTexture dest)
  {
      RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height);
      // ... 使用 rt
      Graphics.Blit(rt, dest);
      // 忘记 ReleaseTemporary，每帧泄漏一个 RT
  }

  // 正确：配对释放
  void OnRenderImage(RenderTexture src, RenderTexture dest)
  {
      RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height);
      Graphics.Blit(rt, dest);
      RenderTexture.ReleaseTemporary(rt);
  }
  ```

- [ ] **Step 4：写结尾总结**

  给一张 GPU 显存优化检查清单：

  | 检查项 | 目标状态 |
  |---|---|
  | 移动端贴图格式 | ASTC 或 ETC2，非 RGBA32 |
  | UI 贴图 Mip Maps | 关闭 |
  | 非必要 Read/Write Enabled | 全部关闭 |
  | 临时 RenderTexture | GetTemporary 配对 ReleaseTemporary |
  | 静态网格 | 考虑 UploadMeshData(true) |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。

---

## Task 5：第5篇——Job System 与 NativeContainer

**输出文件：** `ToolkitInPorject/Memory_JobSystem_Zhihu.md`

**Spec 对应节：** 第5篇

**核心内容要求：**
- "想彻底消除 GC 压力，Job 里不能用 C# 对象"作为开篇
- Job System 为什么不能用 managed 对象（线程安全 + Burst 限制）
- NativeContainer 主要类型表
- Allocator 三种类型表（Temp / TempJob / Persistent，生命周期、适用场景、忘记 Dispose 的后果）
- Safety Handle 系统（编辑器下检测读写冲突）
- 正确的 Dispose 模式（`using` / 手动 / `data.Dispose(handle)`）
- Coroutine vs Job 内存对比表
- Leak Detection 模式

- [ ] **Step 1：写文章骨架**

  ```markdown
  ## 想零 GC，但 Job 里不能用 C# 对象

  ## Job System 的内存模型

  ## NativeContainer：Native 内存的容器

  ### 主要类型

  ### Allocator：三种生命周期

  ## Safety Handle：编辑器下的安全网

  ## 正确的 Dispose 模式

  ## Coroutine vs Job：内存视角对比

  ## Leak Detection 模式
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  确认 Allocator 三种类型和 Safety Handle 机制均有对应节。

- [ ] **Step 3：展开正文**

  要点：
  - 开篇点出：从 GC 堆走到 Native 内存，不是"优化技巧"，是"换一套规则"
  - Job 不能用 managed 对象：给一段尝试在 Job 里用 `List<T>` 的编译错误，说明原因
  - Allocator 表格后给一个典型错误示例（`Temp` 传给跨帧 Job）：

  ```csharp
  // 错误：Temp 分配的数组在下一帧已失效
  void Update()
  {
      var data = new NativeArray<float>(100, Allocator.Temp);
      _longRunningJob = new ProcessJob { Data = data }.Schedule();
      // data 在本帧结束自动释放，但 Job 可能跨帧执行
  }

  // 正确：跨帧 Job 使用 TempJob 或 Persistent
  void Update()
  {
      var data = new NativeArray<float>(100, Allocator.TempJob);
      var handle = new ProcessJob { Data = data }.Schedule();
      data.Dispose(handle); // Job 完成后自动释放
  }
  ```

  - Safety Handle：说明只在 Editor/Development Build 生效，Release Build 不检查
  - Coroutine vs Job 对比表（4 行：数据存放 / 状态机 / 并发 / 适用场景）

- [ ] **Step 4：写结尾总结**

  给 Allocator 选择决策：

  | 使用场景 | 推荐 Allocator |
  |---|---|
  | 帧内用完即弃 | Temp |
  | Job 执行期间（4 帧以内） | TempJob |
  | 跨场景长期存在 | Persistent |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。

---

## Task 6：第6篇——Burst 与内存布局

**输出文件：** `ToolkitInPorject/Memory_Burst_Zhihu.md`

**Spec 对应节：** 第6篇

**核心内容要求：**
- "加了 `[BurstCompile]` 快了数倍但不理解为什么"作为开篇
- 缓存行（Cache Line）工作原理（64 字节，顺序 vs 随机访问代价）
- AoS vs SoA 对比（粒子系统示例，缓存命中率计算，实测速度差异）
- Blittable 类型定义（✅/❌ 列表，`bool` → `byte` 替换）
- struct 内存对齐与填充（错误排列 vs 正确排列的 sizeof 对比）
- `UnsafeUtility` 的适用边界和风险
- ECS Archetype + Chunk 布局作为系列收尾

- [ ] **Step 1：写文章骨架**

  ```markdown
  ## 加了 BurstCompile，快了五倍——为什么

  ## 缓存行：CPU 内存访问的基本单位

  ## AoS vs SoA：数据布局决定缓存命中率

  ## Blittable 类型：Burst 能处理什么

  ## struct 内存对齐：隐藏的填充字节

  ## UnsafeUtility：最后的手段

  ## ECS 内存布局：数据导向设计的终点
  ```

- [ ] **Step 2：对照 spec 验证骨架覆盖**

  确认 AoS/SoA 对比有具体数字，Blittable 列表有 `bool` 陷阱，ECS 节作为收尾。

- [ ] **Step 3：展开正文**

  要点：
  - 缓存行：用"超市货架"类比（每次取货拿一整托盘），然后立刻给数字（L1 缓存命中约 4 个周期，Cache Miss 约 200 个周期）
  - AoS vs SoA：

  ```csharp
  // AoS
  struct Particle { public float3 Position; public float3 Velocity; public float Lifetime; }
  NativeArray<Particle> particles;
  // 只更新 Lifetime：每个 Cache Line (64B) 只有 4B 有效数据，利用率 6%

  // SoA
  NativeArray<float3> positions;
  NativeArray<float3> velocities;
  NativeArray<float>  lifetimes;
  // 只更新 Lifetime：Cache Line 全是 float，利用率 100%
  ```

  - 在 SoA 示例后给出结论数字："1M 粒子只更新 Lifetime，SoA 比 AoS 快约 5–8 倍"（标注"典型测量值，随硬件浮动"）
  - Blittable 列表用 ✅/❌ 格式，`bool` 单独一行说明并给替换方案
  - struct 对齐给两个结构体（Bad 排列 / Good 排列），明确写出 sizeof 差异
  - `UnsafeUtility`：给一句定性结论"日常业务代码不应触碰"，说明风险（Safety Handle 不保护，出错直接 crash）
  - ECS 节：Archetype 定义 → Chunk 结构（16KB，SoA 布局）→ 一句收尾："这是把本系列所有原则整合起来之后，性能上限在哪里的答案"

- [ ] **Step 4：写结尾总结**

  给全系列回顾表，作为整个系列的收官：

  | 篇 | 核心原则 |
  |---|---|
  | 第1篇 | 遇到内存问题，先判断在哪一层 |
  | 第2篇 | 减少分配才能减少 GC，增量 GC 不是银弹 |
  | 第3篇 | 资产生命周期独立于 GameObject，引用计数归零才释放 |
  | 第4篇 | 贴图是 GPU 显存的最大头，压缩格式是最直接的杠杆 |
  | 第5篇 | 走出 GC 堆需要遵循 Native 内存的规则：分配器 + 显式 Dispose |
  | 第6篇 | 数据布局决定缓存命中率，命中率决定性能上限 |

- [ ] **Step 5：自检**

  对照通用验收标准逐条检查，修正后保存。
