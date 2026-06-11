# 搞懂 Unity 内存的第一步：分清三层

项目跑到中后期，几乎所有团队都会撞到同样三类内存问题：

- **战斗场景每隔几秒掉一次帧**，帧率曲线呈周期性尖刺，规律得像心电图。
- **进同一个场景反复进出十几次后，内存监控数字一路爬高**，从 400 MB 涨到 700 MB，再也回不去。
- **低端 Android 机一进某个 BOSS 关就闪退**，崩溃日志只有一句 `low memory`，PC 和高端机完全复现不出来。

三个症状都被习惯性归为"内存问题"，但根因分布在三个完全不同的内存层：第一个在托管堆，第二个在 Native 堆，第三个在 GPU 显存。把它们混在一起调，就是抓着 `GC.Collect()` 反复试、把 `Destroy` 撒满代码、最后只能祈祷低端机不再闪退。

要让排查有方向，第一步是把这三层内存彻底分清楚——谁分配、谁释放、装的是什么东西。这一篇先搭好这套心智框架，后面的五篇会逐层挖细节。

---

## 三层内存的边界

Unity 运行时同时持有三块互相独立的内存区域。它们的分配者、释放者、典型内容都不一样：

| 内存层 | 别名 | 分配者 | 释放者 | 存什么 |
|---|---|---|---|---|
| Managed | GC 堆 | C# runtime（Mono / IL2CPP） | GC 自动回收 | C# 对象（class 实例、数组、字符串、委托） |
| Native | 引擎堆 | Unity C++ 引擎 | 引用计数归零后由引擎释放 | 资产原始数据（贴图像素、音频 PCM、网格顶点、Shader 编译产物） |
| GPU VRAM | 显存 | 图形驱动（GPU 侧） | 显式上传/释放，由引擎触发 | 贴图 GPU 副本、顶点/索引缓冲、RenderBuffer（颜色/深度/Shadow Map） |

三层各自的特点：

**Managed 层**由 C# runtime 管理。你写的每一行 C# 代码、`new` 出来的每个引用类型对象，都落在这里。GC 自动扫描和回收，但代价是周期性停顿。这一层的典型问题是 GC Spike，表现为帧率抖动。

**Native 层**由 Unity C++ 引擎管理。所有真正的资产数据——贴图的像素字节、音频的 PCM 采样、Mesh 的顶点数组——都住在这里，C# 那边拿到的 `Texture2D`、`AudioClip`、`Mesh` 只是一层薄薄的包装对象。这一层用引用计数管理生命周期，GC 完全管不到。漏释放就是常说的"资产泄漏"。

**GPU VRAM 层**是显卡（或集成显卡共享内存）上的独立账本。CPU 侧的内存监控完全看不见这块。贴图、网格、RenderTexture 在被渲染前需要上传到显存，之后 CPU 和 GPU 两边可能同时各存一份。低端机 OOM 大多发生在这一层。

三层互不知道对方的存在。GC 回收 C# 包装对象的那一刻，Native 那边的像素数据没有任何反应；同样，Native 把一份贴图上传给 GPU 之后，CPU 这边并不会自动把 Native 副本清掉。**释放路径不通用**，是后面所有误解的根源。

---

## 三个最常见的误解

### 误解一：`new Texture2D()` 只是分配一个 C# 对象

实际上这一行同时在两个层做了分配：

```csharp
// Managed 层分配一个约几十字节的 C# 包装对象（Texture2D 实例）
// Native 层分配 width * height * 4 字节的像素缓冲（这里是 4 MB）
var tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
tex.SetPixels(pixels);
tex.Apply();
```

包装对象约几十字节，与 4 MB 像素缓冲相比可忽略不计；真正占空间的是 Native 那份数据。

释放路径同样有两条，必须显式走 Unity 的销毁 API：

```csharp
// 错：让 tex 离开作用域，等 GC
tex = null; // GC 届时回收 C# 包装对象；Native 那 4 MB 永远不会释放

// 对：显式销毁，引擎释放 Native 数据，GC 届时回收变成空壳的包装对象
UnityEngine.Object.Destroy(tex);
```

任何继承自 `UnityEngine.Object` 的类型（`Texture`、`Material`、`Mesh`、`AudioClip`、`GameObject`、`Component` 等）都遵循这条规则。把它们当成普通 C# 对象处理，Native 内存就一定泄漏。

### 误解二：`Destroy(gameObject)` 会释放挂在它身上的资产

它只销毁 GameObject 本身和挂载的 Component，资产引用计数不变：

```csharp
var go = Instantiate(prefab); // 引用 prefab 里那张 1024x1024 贴图
// ... 用一会
Destroy(go);
// GameObject 和 MeshRenderer 被销毁，GC 届时回收 C# 包装对象
// 但贴图、Material、Mesh 这些资产的引用计数没有任何变化
// 因为 prefab（或 AssetBundle / Addressables handle）依然持有它们
```

这是 Unity 设计上的合理选择：资产可能被很多对象共用，销毁一个使用者不应该牵连资产本身。但这意味着场景里所有 GameObject 都销毁后，背后那一堆贴图、Mesh、Material 全部还驻留在 Native 内存里，等待显式的卸载调用（`Resources.UnloadAsset`、`bundle.Unload(true)`、`Addressables.Release` 之一）。

也正因为如此，反复进出同一个场景时内存数字越爬越高——每次进场景都加载一遍资产，引用计数 +1；离场景只销毁了 GameObject，资产的引用没断，新一轮的资产又加载进来，旧的留在 Native 里挤占空间。

### 误解三：贴图上传 GPU 后，Native 那份就没了

默认情况下两份都在。Native 那份是 CPU 侧的源数据，GPU 那份是渲染时实际采样的副本：

```csharp
// 从磁盘加载一张 1024x1024 RGBA32 贴图
// Native 占 ~4 MB（像素缓冲），GPU 上传后 VRAM 再占 ~4 MB
var tex = Resources.Load<Texture2D>("UI/Logo");

// 这张贴图被一个 Renderer 引用，第一次渲染时引擎自动上传到 GPU
someRenderer.material.mainTexture = tex;
```

什么时候只剩一份、什么时候两份都在，由导入设置决定。Texture Importer 里那个 **Read/Write Enabled** 复选框就是开关：勾上时 Native 保留完整副本（用于 `GetPixels` 这类 CPU 读写），不勾上时上传完成后 Native 副本会被丢弃，只留 GPU 那份。

Mesh 也有同样的开关（`Mesh.isReadable` / `Read/Write Enabled`），并且额外提供 `Mesh.UploadMeshData(true)` 来手动释放 Native 副本。

记住这个事实就能避免一个很常见的误判：在 Memory Profiler 里看到 Native 贴图占用很高，第一反应不是"贴图太多"，而是"哪些贴图开了 Read/Write 把副本留下来了"。

---

## 实例化一个带贴图 Prefab 时，三层各自发生了什么

把上面三个误解放在一起看，最好的方式是跟一次完整的 Prefab 实例化。假设 Prefab 是一个角色，引用了一张 1024×1024 RGBA32 贴图、一个 Mesh、一个 Material。

调用 `Instantiate(characterPrefab)` 时，三层依次发生这些事：

**第一次访问 Prefab 时（不是 Instantiate，是更早的 `Resources.Load` / AssetBundle / Addressables 加载）：**

- Native 层：贴图像素（4 MB）、Mesh 顶点和索引（按顶点数算，假设 200 KB）、Material 参数块（几 KB），全部从磁盘读出加载到 Native 内存。引用计数从 0 变成 1。
- Managed 层：为每份资产分配 C# 包装对象（`Texture2D`、`Mesh`、`Material` 实例），每个几十到几百字节。
- GPU 层：还没上传，VRAM 占用为 0。

**调用 `Instantiate(characterPrefab)`：**

- Managed 层：新建一个 `GameObject` 包装对象，以及它身上每个 Component（`Transform`、`MeshRenderer`、`MeshFilter`、若干脚本）的 C# 包装对象，加起来 1 KB 量级。
- Native 层：引擎复制一份 GameObject + Component 的 Native 数据结构（也是 1 KB 量级）。贴图、Mesh、Material 这些资产**不复制**——新实例只是引用它们，引用计数不会因为 Instantiate 而 +1（资产的引用计数与 GameObject 实例数无关，只与"有多少个加载来源"有关）。
- GPU 层：依然没动。

**第一次进入摄像机视野，引擎准备渲染这个角色：**

- GPU 层：贴图（4 MB）和 Mesh 顶点（200 KB）被上传到 VRAM，分配显存。如果 Read/Write 关闭并且 Mesh 调过 `UploadMeshData(true)`，Native 副本随后被释放；否则 Native 和 VRAM 同时各存一份。
- Native 层：根据上一条决定是否释放副本。
- Managed 层：无变化。

**调用 `Destroy(instance)`：**

- Managed 层：GameObject 和 Component 的 C# 包装对象失去引用，等待下次 GC 回收。
- Native 层：GameObject 和 Component 的 Native 数据被引擎释放。**贴图、Mesh、Material 的引用计数不变**，依然驻留 Native。
- GPU 层：无变化。资产对应的显存副本依然存在，引擎不会因为 GameObject 销毁而回收 VRAM。

只有当贴图、Mesh、Material 的加载来源（`Resources` / AssetBundle / Addressables）也被释放，引用计数归零，引擎才会回收 Native 和 GPU 上的实际数据。

框架已经建立。走完这趟流程，三层各自的责任就具体了：Managed 层负责包装对象的生命周期，由 GC 管；Native 层负责资产原始数据的生命周期，由引用计数管；GPU 层负责渲染所需的副本，由引擎根据资产可读性和显式上传/释放调用管。

---

## 系列路线图

这一篇搭框架，后续五篇逐层拆。每一篇都从一个具体症状切入，可以按需挑着看。

- **第 2 篇 · GC 与托管内存**：从"GC Spike 为什么周期性出现"切入，讲 Boehm GC 和增量 GC 的工作方式，盘点项目里最高频的隐藏分配源（装箱、闭包、`foreach`、`yield return`、LINQ、字符串拼接），收尾给一套零分配设计原则。
- **第 3 篇 · Native 内存与资产生命周期**：从"`Destroy` 了 GameObject 内存还在涨"切入，讲清楚引用计数机制，对比 Resources / AssetBundle / Addressables 三条加载路径对计数的影响，给出常见泄漏模式的识别与修复。
- **第 4 篇 · GPU 显存实战**：从"低端机特定场景 OOM"切入，把贴图内存数学（分辨率 × 像素字节 × mip 系数）讲透，对比 ASTC / ETC2 / DXT5 等压缩格式的取舍，再覆盖 RenderTexture 生命周期、`Mesh.UploadMeshData`、Sprite Atlas 等高频问题。
- **第 5 篇 · Job System 与 NativeContainer**：从"想彻底消除 GC 压力但 Job 里不能用 C# 对象"切入，讲 Job System 走出 GC 堆后进入的另一套内存体系，重点讲 Allocator 三种类型的适用场景和 Safety Handle 机制。
- **第 6 篇 · Burst 与内存布局**：从"加了 `[BurstCompile]` 快了数倍但不理解为什么"切入，从 CPU 缓存行讲起，对比 AoS / SoA 布局，覆盖 Blittable 类型、struct 字段对齐与填充，最后落到 ECS 的 Archetype/Chunk 模型作为整个系列的收束。

---

## 结尾：把三层装进脑子里

回到开头那三个症状。它们的归属和处理路径其实是一一对应的：

| 内存层 | 典型症状 | 释放路径 |
|---|---|---|
| Managed | 帧率周期性抖动（GC Spike） | GC 自动回收；治本靠减少高频分配 |
| Native | 内存监控数字持续上涨、Destroy 后不下降 | 引用计数归零；显式 `Destroy` 资产，或卸载加载源 |
| GPU | 低端机特定场景 OOM、CPU 内存正常 | 显式 `Release` / `Destroy`；导入阶段选对压缩格式 |

下一次再遇到内存问题，先别急着 `GC.Collect()` 或者到处加 `Destroy`。先问自己一个问题：症状落在哪一层？层定了，工具和打法才有方向。这套分层框架会贯穿后续五篇，每一篇都是在某一层往下挖。
