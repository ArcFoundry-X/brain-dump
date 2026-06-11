# Unity 内存管理 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 番外篇（内存管理）
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，有一定项目经验）
- 语言：简体中文
- 语气：自然、专业，承接前几篇风格
- 校准等级：INTERMEDIATE → ADVANCED（逐层深入）

---

## 文章标题（参考）

`Unity 内存管理：从 GC 到显存，搞清楚三层内存`

---

## 叙事策略

**由浅入深，逐层打开**：从读者最熟悉的 C# 对象和 GC 出发，往下到 Unity Native 层的资源生命周期，再到 GPU 显存配置，最后说如何识别三类问题。每层结束时有自然的钩子带入下一层。无独立框架实现，代码以 before/after 对比为主，服务于概念讲解。

---

## 文章结构

### 第一节：开篇

Unity 内存问题以三种面貌出现：

- **帧率周期性抖动**：GC Spike，托管内存层的问题
- **低端机闪退**：OOM，通常是 GPU 显存撑爆
- **内存监控数字持续上涨**：泄漏，Native 层资产未释放

三个症状根因不同，分布在不同的内存层。文章目标：搞清楚这三层各自存什么、怎么出问题、怎么发现。

---

### 第二节：托管内存与 GC

**2.1 GC 堆与分配**

C# `new` 一个对象就在 GC 堆上分配内存。GC 堆空间耗尽时，GC 触发，扫描所有对象，回收无引用的内存。Unity 传统上使用 Boehm GC，触发时整个主线程暂停（stop-the-world），持续时间从几毫秒到几十毫秒不等，直接表现为帧率抖动。

**2.2 增量 GC（Unity 2019+）**

Unity 2019 引入增量 GC，把 GC 工作拆分成小块，分散到多帧执行，减少单帧停顿时长。但增量 GC 不能消除 GC，只是把大卡顿变成小卡顿，根本解法仍然是减少分配。

**2.3 常见的隐藏分配来源**

每种配 before/after 对比代码：

- **字符串拼接**：`"HP: " + hp` 每次执行都产生新 string 对象，高频调用（如 Update）快速堆满 GC 堆。使用 `StringBuilder` 或字符串插值缓存替代。
- **foreach 装箱**：对非泛型集合（`ArrayList`、`Hashtable`）使用 `foreach`，每次迭代产生 `IEnumerator` 装箱分配。统一使用泛型集合（`List<T>`、`Dictionary<K,V>`）。
- **LINQ**：LINQ 表达式在运行时产生大量中间对象（闭包、迭代器），热路径（Update、碰撞回调）里禁用。
- **值类型装箱**：`int`/`float`/`struct` 传给 `object`/`interface` 参数时产生装箱。常见场景：`Debug.Log(intValue)`、字典用 `enum` 做 key（某些版本的 Unity）。
- **Coroutine 的 yield return**：每个 `yield return new WaitForSeconds(t)` 都分配一个对象。缓存 `WaitForSeconds` 实例复用。

**2.4 识别 GC Spike**

Profiler 的 CPU 时间轴上出现周期性尖峰，对应时刻 GC Alloc 列有大额数字。每帧 GC Alloc 理想值为 0，实际项目中控制在几 KB 以内是可接受的。

---

### 第三节：Native 内存与资源生命周期

**3.1 Native 内存是什么**

Unity 引擎底层（C++）维护的内存区域，C# GC 不可见，不会被 GC 自动回收。贴图的原始字节、音频 PCM 数据、网格的顶点/索引数组、物理碰撞体数据都驻留在这里。

**3.2 Unity 的引用计数**

Unity 引擎对每份资产维护内部引用计数，计数归零时才真正释放 Native 内存。C# 侧对 `Texture2D`、`AudioClip` 等对象的引用被 GC 回收，并不代表 Native 内存释放——引擎可能仍然持有该资产的引用。

**3.3 常见泄漏来源**

每种配说明，部分配 before/after 代码：

- **`Resources.Load` 不配对 `Unload`**：`Resources.Load` 加载的资产引用计数增加，不调用 `Resources.UnloadAsset` 或 `Resources.UnloadUnusedAssets`，资产永久驻留。
- **`renderer.material` 自动创建实例**：访问 `renderer.material` 时 Unity 自动创建一个 Material 实例（引用计数 +1），即使原始材质被卸载，实例仍然存活。读取材质属性应使用 `renderer.sharedMaterial`。
- **静态字段持有资源引用**：场景卸载后，挂在静态字段上的资源引用不会释放，引用计数无法归零。场景卸载时需要显式清空。

**3.4 `Destroy()` vs 失去引用**

`Destroy(gameObject)` 销毁的是场景中的 GameObject 和 Component，底层贴图、网格、音频等资产不会随之释放，它们有独立的生命周期。失去 C# 引用后 GC 只回收 C# 包装对象，Native 资产依然存活。

**3.5 `Resources.UnloadUnusedAssets()`**

能扫描并释放所有引用计数为零的资产，但本身开销大（全资产扫描），只适合在场景切换等非实时场景调用，不能作为常规内存管理手段。

---

### 第四节：GPU 内存

**4.1 GPU 内存存什么**

贴图数据（上传后的像素块）、顶点/索引缓冲（Mesh）、Render Buffer（深度缓冲、颜色缓冲、Shadow Map）。贴图通常是最大头，也是最容易优化的部分。

**4.2 贴图压缩格式**

用一张表展示同一张 1024×1024 贴图在不同格式下的 GPU 内存占用：

| 格式 | GPU 内存（1024×1024） | 平台支持 | 备注 |
|---|---|---|---|
| RGBA32（无压缩） | 4 MB | 全平台 | 开发调试用，不用于发布 |
| DXT5（BC3） | 1 MB | PC/主机 | 桌面端标准 |
| ETC2 | 0.5 MB | Android（OpenGL ES 3.0+） | Android 主流 |
| ASTC 4×4 | 1 MB | iOS / 高端 Android | 质量最好，可调块大小 |
| ASTC 8×8 | 0.25 MB | iOS / 高端 Android | 质量稍低，显存最省 |

选择原则：移动端优先 ASTC，不支持 ASTC 的 Android 设备回退 ETC2，PC 用 DXT。

**4.3 Read/Write Enabled**

勾选后 Unity 在 CPU 侧保留贴图的完整副本（Native Memory），导致同一份贴图同时占用 CPU 内存和 GPU 内存，总占用翻倍。只有运行时需要通过代码读写像素（`GetPixels`、`SetPixels`）才需要开启，其他情况一律关闭。

**4.4 Mip Maps**

开启 Mip Maps 会预生成多个分辨率层级，显存占用增加约 33%（1/4 + 1/16 + … ≈ 1/3）。3D 场景中的贴图推荐开启（GPU 会根据距离采样合适的层级，减少过采样开销）；UI 贴图像素对齐，不存在透视缩放，关闭即可。

**4.5 `Mesh.UploadMeshData(true)`**

调用后 Unity 把网格数据上传 GPU，并释放 CPU 侧的 Native Memory 副本。对运行时不再需要修改的静态网格主动调用，可以节省约一半网格内存。代价是之后无法通过 CPU 读取顶点数据。

---

### 第五节：发现问题

不讲 Memory Profiler 的操作流程，重点在于"看什么数字、识别什么特征"。

**三个关键数字**

- **Total Reserved**：Unity 向操作系统申请的总内存量。长期只涨不降是泄漏的信号。
- **Total Used**：实际在用的内存量。Reserved - Used 是空闲缓冲区，差值过大说明有内存碎片。
- **GC Alloc/frame**：每帧新增的 GC 分配量。理想值为 0；稳定不为零说明有持续分配源。

**GC Spike 的特征**：CPU 时间轴上出现周期性尖峰，峰值时刻 GC Alloc 有大额数字，通常每隔若干秒一次，周期与 GC 堆满的频率对应。

**内存泄漏的特征**：前后两次快照对比，某类对象（如 `Texture2D`、`Material`）的实例数量只增不减，且场景切换后仍然存在。

**GPU 内存问题的特征**：低端机特定场景崩溃，Total Reserved 数字合理但 GPU 单独超标；或贴图导入设置里 Read/Write 被意外勾选。

---

### 第六节：小结

| 内存层 | 存什么 | 典型症状 | 关键数字 |
|---|---|---|---|
| Managed（GC 堆） | C# 对象 | 帧率周期性抖动 | GC Alloc/frame |
| Native | 资产原始数据 | 内存持续上涨 | Total Reserved |
| GPU | 贴图、网格缓冲 | 低端机 OOM 闪退 | GPU Memory |

三层内存各有独立的释放路径，GC 管 Managed，引用计数管 Native，压缩格式和 `Unload` 管 GPU。搞混层次是内存问题难以排查的主要原因。

---

## 代码规范

- 错误写法（高频分配源）：给出具体代码示例
- 正确写法（优化后）：给出对应修复代码
- 所有代码块加 `csharp` 语言标识
- 贴图内存对比用表格展示，不用代码

---

## 不在范围内

- Memory Profiler 工具的具体操作步骤（截图教程）
- IL2CPP vs Mono 的内存差异
- Shader 变体内存
- 物理引擎内存（PhysX）
- 渲染管线（URP/HDRP）的额外内存开销
- 平台特定内存限制数字（iOS/Android 的具体 OOM 阈值）
