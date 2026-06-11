# Unity 资产生命周期：Destroy 了，内存为什么还在涨

场景里有一百个敌人，战斗结束，`Destroy` 一遍，GameObject 全消失了。Memory Profiler 打开一看：贴图、Material、Mesh 的占用一点没动。

换个角度看更明显——反复进出同一个关卡十几次，内存监控的数字从 400 MB 爬到 700 MB，始终回不去。每次进关卡资产都重新加载了，但上一波的资产不知道去哪了、也没释放。`GC.Collect()` 试过，没用；`Resources.UnloadUnusedAssets()` 调了，掉了一点点，过几分钟又涨回来。

这一篇讲清楚这个经典困惑：**为什么 Destroy 了 GameObject，资产内存还在涨**。核心是 Unity 用一套独立于 GameObject 的引用计数来管理 Native 层资产，GC 完全管不到。理解了引用计数的规则，这类泄漏就有了可操作的排查路径。

---

## Unity 资产的引用计数

Unity 对每份 Native 资产——`Texture2D`、`AudioClip`、`Mesh`、`Material`、`Shader` 等继承自 `UnityEngine.Object` 的类型——维护一个内部引用计数：

- **引用计数 > 0**：资产驻留 Native 内存，引擎不会释放
- **引用计数 = 0**：资产进入可释放状态（不是立即释放，取决于触发时机）

几个关键事实让很多人踩坑：

**GC 回收 C# 包装对象，不影响引用计数。** `Texture2D` 的 C# 对象只是一层几十字节的包装，里面持有一个指向 Native 资产数据的指针。GC 把包装回收了，Native 那份像素数据的引用计数纹丝不动。这两件事完全解耦。

**只有引擎认可的卸载调用才能让计数归零。** `Resources.UnloadAsset`、`bundle.Unload(true)`、`Addressables.Release` 这类 API 才是真正告诉引擎"我不再需要这份资产了"的方式。

**`Instantiate` 不增加资产的引用计数。** 实例化一个 Prefab，拷贝出来的 GameObject 和 Component 的引用计数会增加，但它们引用的贴图、Mesh、Material 这些共享资产，计数不变。

---

## 三条加载路径：引用计数怎么变

不同的加载方式对引用计数的影响和对应的释放调用都不一样：

| 加载方式 | 引用计数 +1 时机 | 引用计数 -1 / 释放时机 |
|---|---|---|
| `Resources.Load` | 调用 `Load` 时 | `Resources.UnloadAsset(asset)` 或 `UnloadUnusedAssets` |
| AssetBundle | `bundle.LoadAsset(name)` 时 | `bundle.Unload(true)` |
| Addressables | `LoadAssetAsync` 完成时 | `Addressables.Release(handle)` |

三条路径的关键区别：

**Resources** 路径最简单——一次 `Load` 对应一次 `UnloadAsset`。`UnloadAsset` 只能卸载非 GameObject 类型的资产（贴图、音频、Mesh 等），无法卸载 Prefab；想清除不用的 Prefab，只能靠 `UnloadUnusedAssets`。

**AssetBundle** 路径有一个常被忽略的参数：`bundle.Unload(true)` 和 `bundle.Unload(false)` 效果完全不同。`Unload(true)` 同时卸载 bundle 文件和所有从它加载的资产（引用计数强制归零）；`Unload(false)` 只卸载 bundle 文件，已经加载出来的资产依然驻留内存，彻底断开了 bundle 与资产之间的联系，之后再也无法通过 bundle 追踪和卸载这些资产。`Unload(true)` 是正确选项，代价是必须确保场景里已经没有对象在使用这些资产，否则会产生悬空引用。

**Addressables** 路径最容易产生泄漏：每次 `LoadAssetAsync` 完成之后，handle 必须显式 `Addressables.Release(handle)` 才能让引用计数 -1。忘记 Release、或者异步加载被取消后 handle 没有清理，资产就永久钉在 Native 内存里。这是 Addressables 项目里最高频的泄漏模式。

---

## Destroy() 到底销毁了什么

`Destroy` 在 Unity 里至少有两种含义，混用就会踩坑：

```csharp
// 情况 1：销毁 GameObject
Destroy(gameObject);
// 销毁：场景里这个 GameObject 本身，以及挂载的所有 Component
// 不销毁：GameObject 引用的任何资产（贴图、Material、Mesh、AudioClip）

// 情况 2：销毁资产对象
Destroy(material);
// 销毁：这个 Material 实例本身（引用计数 -1，如果归零则 Native 数据可被释放）
// 注意：仅对运行时动态创建的 Material/Texture2D 等有意义；不要对 Project 里的资产直接 Destroy
```

`Destroy` 是异步的——调用之后不立即执行，实际销毁发生在当帧末尾（`LateUpdate` 之后、下一帧 `Update` 之前）。在同一帧里 `Destroy(go)` 之后立刻检查 `go == null` 会得到 `true`（Unity 重载了 `==` 操作符），但对象实际上还活着直到帧末。

最关键的一条规则：**动态创建的资产，必须显式 Destroy**。

```csharp
// 错误：运行时 new 出来的 Texture2D，让 GC 处理
void CreateTexture()
{
    var tex = new Texture2D(512, 512);
    // ... 用完，tex 离开作用域
    // GC 届时回收 C# 包装对象，但 512×512 的像素数据（约 1 MB）永远不会释放
}

// 正确：显式销毁，Native 数据被引擎释放
Texture2D _tex;
void Start()  => _tex = new Texture2D(512, 512);
void OnDestroy() => Destroy(_tex);
```

---

## 四种常见泄漏模式

### 1. `renderer.material` 的自动实例化陷阱

这是 Unity 里最隐蔽的泄漏来源之一，很多人写了几年都不知道。

访问 `renderer.material` 时，Unity 会**自动创建一份 Material 的独立实例**，这样对它的修改不影响原始材质。听起来很贴心，但每次访问都 new 一个 Material，引用计数 +1，而原始 Material 的卸载并不会影响这些运行时实例——它们会一直驻留到被显式 Destroy。

```csharp
// 错误：每次访问 renderer.material 都可能创建新实例
void Update()
{
    renderer.material.color = Color.red; // 每帧产生一个 Material 实例，只增不减
}
```

```csharp
// 正确路径一：只读属性用 sharedMaterial，不产生实例
void UpdateShared()
{
    renderer.sharedMaterial.color = Color.red; // 修改会影响所有使用这个材质的对象
}

// 正确路径二：确实需要独立实例时，创建一次并在销毁时显式 Destroy
Material _mat;
void Start()     => _mat = renderer.material; // 只创建一次
void OnDestroy() => Destroy(_mat);            // 对象销毁时回收
```

区分原则：只读取材质属性（查看颜色、查看贴图），用 `sharedMaterial`；需要对某个对象独立改变外观，用 `renderer.material` 但缓存结果，并在 `OnDestroy` 里回收。

检测方法：在 Memory Profiler 里搜索 `Material`，如果看到大量名称带 `(Instance)` 后缀的 Material，且数量随时间只增不减，基本可以确定是这个问题。

### 2. 静态字段跨场景持有

静态字段（`static`）的生命周期和场景无关，场景卸载时它不会被清理。如果一个静态字段持有了资产引用，这份资产的引用计数就永远不会归零——`UnloadUnusedAssets` 也救不了，因为引用还在。

```csharp
// 错误：静态字段持有资产引用
public class ConfigManager : MonoBehaviour
{
    public static Texture2D AvatarTexture; // 跨场景存活

    void Awake()
    {
        AvatarTexture = Resources.Load<Texture2D>("UI/Avatar");
    }
    // 场景卸载，ConfigManager 的 GameObject 被销毁，但 AvatarTexture 这个引用仍然有效
    // UnloadUnusedAssets 看到 AvatarTexture != null，引用还活着，不释放
}
```

```csharp
// 正确：场景卸载时主动清除静态引用
public class ConfigManager : MonoBehaviour
{
    public static Texture2D AvatarTexture;

    void Awake()
    {
        AvatarTexture = Resources.Load<Texture2D>("UI/Avatar");
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnSceneUnloaded(Scene scene)
    {
        Resources.UnloadAsset(AvatarTexture);
        AvatarTexture = null; // 断开静态引用
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }
}
```

静态字段持有的不止有贴图，还有 ScriptableObject 的引用、用 `DontDestroyOnLoad` 的对象上挂的资产、单例里持有的数据。检查项目里所有的 `static` 字段，凡是 `UnityEngine.Object` 类型，都要问一句：它什么时候被释放？

### 3. 异步加载取消但 handle 未释放

Addressables 的异步操作返回一个 `AsyncOperationHandle`，只要加载成功，`handle` 就对资产持有引用。即使后来不再需要这份资产、甚至场景都卸载了，只要 handle 没有 Release，资产永远不走。

```csharp
// 错误：取消请求，但 handle 没有 Release
AsyncOperationHandle<Texture2D> _loadHandle;

void RequestTexture()
{
    _loadHandle = Addressables.LoadAssetAsync<Texture2D>("UI/Banner");
}

void CancelRequest()
{
    // 玩家退出了页面，不再需要这张贴图
    _loadHandle = default; // 只是把本地变量清了，Addressables 内部引用还在
    // 资产永久泄漏
}
```

```csharp
// 正确：无论加载是否完成，都通过 Release 归还 handle
AsyncOperationHandle<Texture2D> _loadHandle;
bool _handleValid;

void RequestTexture()
{
    _loadHandle = Addressables.LoadAssetAsync<Texture2D>("UI/Banner");
    _handleValid = true;
}

void CancelOrCleanup()
{
    if (_handleValid)
    {
        Addressables.Release(_loadHandle); // 不管加载完没完，都 Release
        _handleValid = false;
    }
}

void OnDestroy() => CancelOrCleanup();
```

`Addressables.Release` 对未完成的操作也是安全的——它会先取消操作再释放 handle。核心原则：**每一次成功的 `LoadAssetAsync` 都必须配对一次 `Release`**，不能依赖 GC 或场景卸载来自动处理。

### 4. 动态创建的资产未销毁

运行时 `new Texture2D()`、`new Material(shader)`、`new RenderTexture()` 创建的对象，C# 端的包装对象离开作用域、被 GC 回收后，Native 那份数据不会跟着释放。必须显式 `Destroy`。

```csharp
// 错误：动态创建的贴图没有 Destroy
void GeneratePreview()
{
    var rt = new RenderTexture(256, 256, 0);
    Camera.main.targetTexture = rt;
    Camera.main.Render();
    previewImage.texture = rt;
    // rt 的本地引用消失，GC 届时回收 C# 包装，256×256 RenderTexture 驻留 GPU 和 Native
}
```

```csharp
// 正确：持有引用，在合适时机 Destroy
RenderTexture _previewRT;

void GeneratePreview()
{
    if (_previewRT != null) Destroy(_previewRT); // 先释放旧的

    _previewRT = new RenderTexture(256, 256, 0);
    Camera.main.targetTexture = _previewRT;
    Camera.main.Render();
    previewImage.texture = _previewRT;
}

void OnDestroy()
{
    if (_previewRT != null) Destroy(_previewRT);
}
```

常见的动态创建资产清单：`Texture2D`、`RenderTexture`、`Material`（`new Material(shader)` 或 `Instantiate(srcMaterial)`）、`Mesh`（代码生成的程序化网格）、`AudioClip`（代码合成的音频）。只要运行时创建，就要运行时销毁。

---

## UnloadUnusedAssets：能用，但要知道它的局限

`Resources.UnloadUnusedAssets()` 扫描整个 Native 资产表，找到引用计数为 0 的资产并释放。听起来是个兜底方案，但有两个关键局限：

**1. 扫描开销大。** 这个调用是全量扫描，耗时从几十毫秒到几百毫秒不等，具体取决于项目里资产的数量。在游戏运行过程中调用会产生明显卡顿。适用场景是**场景切换时**（加载屏播放期间）、**战斗结束的结算页**（玩家在看结算数据时顺带清理），而不是在 Update 里定期调用。

**2. 它救不了引用还活着的资产。** 静态字段持有的资产、Addressables handle 未释放的资产、`renderer.material` 实例没被 Destroy 的资产——这些引用计数都不是 0，`UnloadUnusedAssets` 完全不会动它们。指望这个 API 代替正确的资产管理是不可能的。

```csharp
// 场景切换时的标准写法
IEnumerator LoadScene(string sceneName)
{
    // 1. 加载新场景（异步）
    var op = SceneManager.LoadSceneAsync(sceneName);
    op.allowSceneActivation = false;

    // 2. 等加载到 90%（等待激活）
    while (op.progress < 0.9f) yield return null;

    // 3. 卸载不再需要的资源（在玩家看不到的时候做）
    yield return Resources.UnloadUnusedAssets();

    // 4. 激活新场景
    op.allowSceneActivation = true;
}
```

`UnloadUnusedAssets` 返回的是 `AsyncOperation`，可以 `yield return` 等它完成再切场景，避免切换过程中的短暂内存峰值。

---

## 泄漏诊断速查

| 症状 | 可能根因 | 修复方向 |
|---|---|---|
| 场景切换后内存不降 | 静态字段持有资产引用 | `sceneUnloaded` 回调里显式 `UnloadAsset` + 置空 |
| Material 数量只增不减 | `renderer.material` 未 Destroy | 缓存实例，`OnDestroy` 里 `Destroy(_mat)` |
| Addressables 内存持续增长 | handle 未 Release | 每次 `LoadAssetAsync` 配对 `Addressables.Release` |
| 动态贴图/RenderTexture 泄漏 | `new Texture2D` / `new RenderTexture` 未 Destroy | 字段持有，`OnDestroy` 里 `Destroy(_tex)` |
| `UnloadUnusedAssets` 没效果 | 资产引用计数 > 0，泄漏根因未解决 | 先断开所有活跃引用，再 Unload |

Native 内存泄漏的排查比 GC 问题更麻烦，因为 GC Profiler 能直接给出 Alloc 列，而 Native 这边没有内置的"这次泄漏了多少"提示。最有效的工具是 **Memory Profiler 包**（Package Manager 搜索 `Memory Profiler`）——它能按类型列出所有 Native 对象，对比两个快照之间的增量，找到只增不减的资产。

引用计数归零才释放。找到谁持有了那个不该有的引用，泄漏就解决了。下一篇会把视线挪到 GPU 显存，讲低端机为什么总在某个场景崩——CPU 内存正常但 GPU 那边的账本早就满了。
