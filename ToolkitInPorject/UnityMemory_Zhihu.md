# Unity 内存管理：从 GC 到显存，搞清楚三层内存

Unity 的内存问题通常以三种面貌出现：**游戏突然卡一帧**、**低端机莫名闪退**、**内存监控数字持续上涨但找不到原因**。三个症状看起来都是"内存问题"，但根因完全不同，分布在不同的内存层。

搞清楚这三层内存各自存什么、怎么出问题、如何释放，才能对症下药，而不是凭感觉加 `GC.Collect()` 或者随手 `Destroy` 一切。

---

## 第一层：托管内存与 GC

这是离开发者最近的一层，也是帧率抖动最常见的根源。

### GC 堆与分配

C# 里每次 `new` 一个引用类型对象，就在 GC 堆上分配一块内存。GC 堆不是无限的，当堆空间耗尽，GC 触发，扫描所有存活对象，回收无引用的内存块，然后继续执行。

关键在于"扫描所有对象"这一步。Unity 传统上使用 **Boehm GC**，这是一个 stop-the-world 的垃圾收集器——触发时主线程完全暂停，等 GC 完成才恢复执行。GC 扫描时间从几毫秒到几十毫秒不等，取决于堆里的对象数量。这段暂停直接表现为帧率尖刺，就是常说的 **GC Spike**。

### 增量 GC（Unity 2019+）

Unity 2019 引入了增量 GC，把 GC 工作拆分成小块，分散到多帧执行，把一次大停顿变成多次小停顿，减少单帧的影响。

但增量 GC 解决的是"停顿时长"问题，不解决"分配频率"问题。如果每帧都在大量分配对象，GC 还是会频繁触发，只是每次停顿短一点。根本解法是减少分配，而不是依赖 GC 策略。

### 高频分配来源

以下几种写法是项目里最常见的 GC 分配源，每一种单独看开销不大，但放在 `Update` 这类高频调用里就会积少成多。

**字符串拼接**

```csharp
// 每帧产生一个新的 string 对象
void Update()
{
    hpText.text = "HP: " + currentHP + " / " + maxHP;
}
```

C# 的 `string` 是不可变类型，每次拼接都会创建一个新的 string 对象，旧的丢给 GC。高频调用时 GC 堆会快速积累碎片。

```csharp
// 用 StringBuilder 复用缓冲
private readonly StringBuilder _sb = new StringBuilder(32);

void Update()
{
    _sb.Clear();
    _sb.Append("HP: ").Append(currentHP).Append(" / ").Append(maxHP);
    hpText.text = _sb.ToString();
}
```

**Coroutine 里的 WaitForSeconds**

```csharp
// 每次循环分配一个 WaitForSeconds 对象
IEnumerator SpawnLoop()
{
    while (true)
    {
        SpawnEnemy();
        yield return new WaitForSeconds(2f);
    }
}
```

```csharp
// 缓存复用，零分配
private readonly WaitForSeconds _spawnInterval = new WaitForSeconds(2f);

IEnumerator SpawnLoop()
{
    while (true)
    {
        SpawnEnemy();
        yield return _spawnInterval;
    }
}
```

**LINQ 在热路径里**

LINQ 的每次查询都会在运行时创建闭包对象和迭代器，链式调用越长，中间对象越多。

```csharp
// Update 里用 LINQ，每帧大量分配
void Update()
{
    var targets = _enemies
        .Where(e => e.IsAlive && e.IsInRange(transform.position))
        .OrderBy(e => e.Distance)
        .ToList();
}
```

```csharp
// 用 for 循环手动过滤，零分配
void Update()
{
    _targets.Clear(); // _targets 是成员变量，提前声明
    for (int i = 0; i < _enemies.Count; i++)
    {
        var e = _enemies[i];
        if (e.IsAlive && e.IsInRange(transform.position))
            _targets.Add(e);
    }
    _targets.Sort(_distanceComparer); // 用缓存的 Comparer
}
```

**值类型装箱**

值类型（`int`、`float`、`struct`、`enum`）传给 `object` 或接口类型参数时，会在堆上创建一个包装对象，这就是装箱。

```csharp
// Debug.Log 接受 object，int 传入时装箱
void Update()
{
    Debug.Log(currentHP);      // 装箱
    Debug.Log("HP: " + currentHP); // 字符串拼接 + 装箱
}

// 改用字符串插值或显式 ToString
Debug.Log($"HP: {currentHP}"); // 仍有少量分配，但比拼接少
Debug.Log(currentHP.ToString()); // 避免装箱，string 由 ToString 产生
```

另一个常见场景：用 `enum` 作为 `Dictionary` 的 key。在部分 Unity/Mono 版本中，`Dictionary<TEnum, TValue>` 的 `GetHashCode` 实现会对 enum 值装箱。改用 `int` key 或自定义 `IEqualityComparer` 可以规避。

**`foreach` 遍历某些集合**

对 `List<T>`、数组使用 `foreach` 在现代 Unity/IL2CPP 中是安全的，不会装箱。但对 Unity 自己的一些集合类型（如旧版 `UnityEngine.UI` 里的某些列表）、或实现了 `IEnumerable` 但返回 `IEnumerator`（非泛型）的集合，`foreach` 会产生装箱分配。遇到可疑类型，用 `for` 循环替代是最保险的做法。

### 识别 GC Spike

在 Unity Profiler 的 CPU 时间轴上，GC Spike 表现为周期性出现的帧时间尖峰。在同一帧的 Hierarchy 视图里，能看到 `GC.Collect` 调用占据了大部分帧时间。

**GC Alloc 列**是更直接的线索：它显示该帧新增的 GC 分配字节数。每帧 GC Alloc 理想值为 0，实际项目允许有少量分配（加载期间、偶发事件），但稳定运行时出现持续的非零值就值得排查。

---

## 第二层：Native 内存与资源生命周期

GC 管不到的一层，也是泄漏最难察觉的地方。

### Native 内存是什么

Unity 引擎底层由 C++ 编写，维护着自己独立的内存区域，称为 Native Memory。贴图的原始像素字节、音频的 PCM 数据、网格的顶点和索引数组、物理碰撞体的数据结构都驻留在这里。

C# 的 GC 对这块内存完全不可见。`Texture2D`、`AudioClip`、`Mesh` 等 C# 对象只是 Native 资产的引用包装，GC 回收 C# 包装对象并不等于释放 Native 内存，两者是独立的生命周期。

### Unity 的引用计数

Unity 引擎内部对每份 Native 资产维护引用计数。`Resources.Load` 加载一份资产，计数加一；`Instantiate` 使用了某个贴图，计数加一。只有计数归零，Native 内存才会真正释放。

问题在于，很多情况下开发者以为资源已经"用完了"，但引擎的引用计数并没有归零，资产一直驻留在内存里。

### 常见泄漏来源

**`Resources.Load` 不配对释放**

```csharp
// 只加载，从不释放
Texture2D icon = Resources.Load<Texture2D>("Icons/sword");
// 用完之后没有任何释放调用
```

`Resources.Load` 加载的资产会一直驻留，直到显式调用 `Resources.UnloadAsset(icon)` 或 `Resources.UnloadUnusedAssets()`。如果在循环或频繁调用的地方加载不同资产而不释放，Native 内存会持续增长。

**`renderer.material` 自动创建实例**

这是一个非常隐蔽的问题：

```csharp
// 每次访问 renderer.material，Unity 自动创建一个新的 Material 实例
void Highlight()
{
    renderer.material.color = Color.red; // 悄悄创建了一个 Material 实例
}
```

`renderer.material` 属性的 getter 会检查当前 Material 是否是共享实例，如果是，就自动克隆一份新的，以防修改影响其他使用同一材质的对象。这个隐式克隆在 Native 层分配了新的 Material 资产，且不会自动释放。

```csharp
// 只读取属性时，用 sharedMaterial
Color originalColor = renderer.sharedMaterial.color;

// 需要修改时，明确管理实例并在销毁时清理
private Material _matInstance;

void Start()
{
    _matInstance = Instantiate(renderer.sharedMaterial);
    renderer.material = _matInstance;
}

void OnDestroy()
{
    Destroy(_matInstance); // 显式销毁
}
```

**静态字段持有资源引用**

场景卸载时，挂载在 MonoBehaviour 上的对象会被销毁，但静态字段不会：

```csharp
public class EnemyManager : MonoBehaviour
{
    // 场景卸载后这个引用仍然存在，贴图无法被释放
    private static Texture2D _sharedIcon;
}
```

场景卸载时需要显式清空静态引用，引用计数才能归零。

### `Destroy()` vs 失去引用

```csharp
GameObject obj = Instantiate(prefab);
obj = null; // 只是让 C# 引用失效，Native 层的 GameObject 仍然存在
```

```csharp
Destroy(obj); // 正确：通知引擎销毁 GameObject，Native 内存会在下一帧回收
```

`Destroy` 销毁的是场景中的 GameObject 和 Component，底层贴图、网格等资产**不会随之释放**——它们有独立的引用计数，只有计数归零才释放。`Destroy(gameObject)` 会减少该对象持有的资产引用，但如果资产还被其他地方引用，Native 内存仍然不会释放。

### `Resources.UnloadUnusedAssets()`

这个接口能扫描所有引用计数为零的 Native 资产并释放。但它的代价是对所有资产做全量扫描，通常耗时数十到数百毫秒，会明显卡顿。

正确的使用时机是**场景切换**——在加载新场景前，或在 `LoadSceneAsync` 完成后调用，作为一次性清理手段。不要在 Update 或频繁触发的逻辑里调用，也不要把它当作常规的内存管理手段来依赖。

---

## 第三层：GPU 内存

最容易被忽视，也最容易把低端机打爆的一层。

### GPU 内存存什么

贴图数据（上传到显卡的像素块）、顶点/索引缓冲（Mesh 数据）、Render Buffer（帧缓冲、深度缓冲、Shadow Map 等）都驻留在 GPU 内存里。贴图通常是最大头，也是最有优化空间的部分。

### 贴图压缩格式

未压缩的 RGBA32 贴图，每个像素占 4 字节。一张 1024×1024 的贴图就是 4MB 显存。UI 有几十张这样的图，角色有十几张，场景里再来几十张环境贴图，显存很快就撑不住了，尤其在只有 1-2GB 显存的移动设备上。

压缩格式通过牺牲少量画质换取大幅减少的显存占用：

| 格式 | 显存（1024×1024） | 平台支持 | 说明 |
|---|---|---|---|
| RGBA32（无压缩） | 4 MB | 全平台 | 开发调试用，不用于发布 |
| DXT5 / BC3 | 1 MB | PC / 主机 | 桌面端标准，压缩比 4:1 |
| ETC2 RGB | 0.5 MB | Android（OpenGL ES 3.0+） | 不含 Alpha 通道 |
| ETC2 RGBA | 1 MB | Android（OpenGL ES 3.0+） | 含 Alpha，压缩比 4:1 |
| ASTC 4×4 | 1 MB | iOS / 高端 Android | 画质最好，块大小可调 |
| ASTC 8×8 | 0.25 MB | iOS / 高端 Android | 显存最省，画质稍低 |

选择原则：**移动端优先 ASTC**，不支持 ASTC 的 Android 设备回退 ETC2；PC 用 DXT。Unity 的平台自适应压缩（Override for Platform）可以针对不同平台设置不同格式，打包时自动选择。

### Read/Write Enabled

贴图导入设置里有一个 **Read/Write Enabled** 选项，勾选后 Unity 在 CPU 侧的 Native Memory 里保留贴图的完整副本，供运行时通过 `GetPixels`、`SetPixels` 读写。

代价是同一份贴图**同时占用 CPU 内存和 GPU 内存**，总占用翻倍。1024×1024 ASTC 4×4 的贴图从 1MB 变成 2MB。有几十张贴图时，这个开销非常可观。

只有确实需要运行时读写像素的场景才开启（比如动态生成贴图、截图处理），其他情况一律关闭。

### Mip Maps

开启 Mip Maps 后，Unity 会预生成一系列从原始分辨率到 1×1 的缩小版贴图（1/4、1/16、1/64……），显存增加约 33%（等比级数之和约为原贴图的 1/3）。

GPU 在渲染时根据屏幕上的实际像素密度自动选择合适的 Mip 层级，避免高分辨率贴图被缩小采样时的走样和性能损耗。**3D 场景中的贴图推荐开启**。

UI 贴图情况不同：UI 元素通常是像素对齐的，不存在透视缩放，不需要 Mip Maps，**UI 贴图一律关闭**，省下 33% 显存。

### `Mesh.UploadMeshData(markNoLongerReadable)`

网格数据在 CPU 侧（Native Memory）和 GPU 侧各存一份。对运行时不再修改的静态网格，CPU 侧的副本是纯冗余的。

```csharp
// 上传完成后释放 CPU 侧副本
mesh.UploadMeshData(true); // 参数 true：上传后标记为不可读，释放 CPU 副本
```

调用后该 Mesh 就无法再通过 CPU 读取顶点数据（`mesh.vertices` 会返回空数组）。场景里静态放置的环境模型、建筑、地形都适合在加载完成后调用，通常能节省约一半的网格内存。

---

## 发现问题

不同的症状对应不同的排查方向。

### 三个关键数字

在 Unity Profiler 的 Memory 模块，有三个数字最值得关注：

- **Total Reserved**：Unity 向操作系统申请的总内存量。这个数字只涨不降是泄漏的信号——正常情况下场景切换后应该能明显回落。
- **Total Used**：实际在使用的内存量。Reserved - Used 是空闲缓冲，差值过大说明内存碎片严重。
- **GC Alloc / frame**：每帧新增的 GC 分配量，在 Profiler 的 CPU 帧列表里可见。理想值是 0，稳定在几 KB 以内通常可以接受。

### GC Spike 的特征

Profiler 的 CPU 时间轴上出现**周期性**的帧时间尖峰，峰值可能是正常帧的 3-10 倍。在对应帧的 Hierarchy 里找到 `GC.Collect`，它上面的调用栈就是触发 GC 的分配源。

周期性是关键词——随机偶发的帧慢通常是业务逻辑问题，规律性的周期尖峰几乎一定是 GC。

### 内存泄漏的特征

Total Reserved 数字只涨不降，场景切换后也不回落。用 Memory Profiler 拍两次快照（进入场景后、重复几次操作后），对比 `Texture2D`、`Material`、`AudioClip` 等资产类型的实例数量——如果数量在增长而没有对应减少，说明有泄漏。

最常见的定位方式：在第二张快照里找实例数量最多的类型，查看它们的引用链，顺着引用链找到是谁持有了不该持有的引用。

### GPU 内存问题的特征

低端机在特定场景加载完成时崩溃，而高端机正常。此时检查贴图导入设置：查找 Read/Write Enabled 被意外开启的贴图、未压缩的 RGBA32 贴图、UI 贴图开了 Mip Maps 的情况。这三类配置错误通常能解释大部分移动端的 GPU 内存超标。

---

## 小结

三层内存各有独立的释放路径，混淆层次是内存问题难以排查的主要原因：

| 内存层 | 存什么 | 典型症状 | 释放手段 |
|---|---|---|---|
| Managed（GC 堆） | C# 对象 | 帧率周期性抖动（GC Spike） | 减少分配，GC 自动回收 |
| Native | 资产原始数据 | 内存持续上涨，不随场景切换释放 | 引用计数归零，`UnloadUnusedAssets` |
| GPU | 贴图、网格缓冲 | 低端机 OOM 闪退 | 压缩格式、关闭 Read/Write、`UploadMeshData` |

GC 管 Managed，引用计数管 Native，导入配置管 GPU——搞清楚问题在哪一层，排查时间能缩短一半。
