# Unity YooAsset 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 第四篇
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（读过第三篇 AssetBundle，了解 AB 基本概念）
- 语言：简体中文
- 语气：自然、专业，承接前三篇风格
- 校准等级：INTERMEDIATE
- YooAsset 版本：3.0

---

## 文章标题（参考）

`Unity YooAsset：用对框架，资源管理才算做对了`

---

## 叙事策略

**线性递进**：YooAsset 简介（解决了什么问题）→ 原生三步初始化 → 原生加载/释放 → 引出封装必要性 → ResourceManager 实现 → 完整示例。先建立对底层 API 的理解，再展示封装层的价值。

---

## 文章结构

### 第一节：开篇

- 一段说明 YooAsset 是什么：基于 AssetBundle 的资源管理框架，替开发者处理了依赖加载、缓存管理、版本校验这些重复且易错的工作
- 三种 PlayMode 各一句话：

| PlayMode | 说明 |
|---|---|
| `EditorSimulateMode` | 编辑器模拟模式，无需打包，开发阶段使用 |
| `OfflinePlayMode` | 单机模式，资源随包体发布，不支持热更 |
| `HostPlayMode` | 联机模式，支持从远端服务器热更新资源，本文以此为例 |

---

### 第二节：初始化流程

说明 YooAsset 使用前必须完成三步初始化，对应第三篇 AB 的三个核心问题的解决入口。

**第一步：InitializeAsync** — 创建 Package，配置 PlayMode 和远端地址

```csharp
YooAssets.Initialize();

var package = YooAssets.CreatePackage("DefaultPackage");
YooAssets.SetDefaultPackage(package);

var initParameters = new HostPlayModeParameters
{
    BuildinQueryServices = new GameBuildinQueryServices(),
    RemoteServices       = new GameRemoteServices("http://your-cdn.com/")
};

var initOperation = package.InitializeAsync(initParameters);
await initOperation.Task;
```

`GameBuildinQueryServices` 和 `GameRemoteServices` 是需要实现的接口，负责告诉 YooAsset 如何查询内置资源和如何拼接远端 URL，在 ResourceManager 一节中给出完整实现。

**第二步：RequestPackageVersionAsync** — 向服务器请求最新版本号

```csharp
var versionOperation = package.RequestPackageVersionAsync();
await versionOperation.Task;
string packageVersion = versionOperation.PackageVersion;
```

**第三步：LoadPackageManifestAsync** — 加载该版本的资源清单

```csharp
var manifestOperation = package.LoadPackageManifestAsync(packageVersion);
await manifestOperation.Task;
```

文字说明：版本对比后若有差异，还需要走补丁下载流程（`PreDownloadContentAsync`），本文不展开，完整热更流程参考 YooAsset 官方文档。

---

### 第三节：资源加载与释放

展示 YooAsset 原生的加载和释放方式：

```csharp
// 加载
AssetHandle handle = package.LoadAssetAsync<Sprite>("Assets/GameRes/UI/icon.png");
await handle;
Sprite sprite = handle.AssetObject as Sprite;

// 释放
handle.Release();
```

说明 `AssetHandle` 的生命周期：`Release` 并非立刻销毁资源，而是递减内部引用计数，计数归零时 YooAsset 才真正回收该资源。调用方需要自行持有 handle 并在合适时机 Release，管理不当容易造成泄漏或提前回收。

---

### 第四节：封装 ResourceManager

**引出问题**：原生用法中，三步初始化需要散落在业务启动代码里，`AssetHandle` 的持有和释放由调用方负责，项目规模一大，handle 散落各处、释放时机难以统一。

**设计目标**：
- 全局唯一入口，调用方不需要持有 `package` 引用
- 内部缓存 `AssetHandle`，同一路径不重复加载
- 统一释放接口，调用方只需要关心路径

**核心数据结构**：`Dictionary<string, AssetHandle>`，以资源路径为 key 缓存 handle。

**完整实现**：

```csharp
public class ResourceManager : SingletonManager<ResourceManager>
{
    private ResourcePackage _package;
    private readonly Dictionary<string, AssetHandle> _handles = new();

    public async Task InitializeAsync(string hostServerURL)
    {
        YooAssets.Initialize();

        _package = YooAssets.CreatePackage("DefaultPackage");
        YooAssets.SetDefaultPackage(_package);

        var initParameters = new HostPlayModeParameters
        {
            BuildinQueryServices = new GameBuildinQueryServices(),
            RemoteServices       = new GameRemoteServices(hostServerURL)
        };

        var initOperation = _package.InitializeAsync(initParameters);
        await initOperation.Task;

        var versionOperation = _package.RequestPackageVersionAsync();
        await versionOperation.Task;

        var manifestOperation = _package.LoadPackageManifestAsync(versionOperation.PackageVersion);
        await manifestOperation.Task;
    }

    public async Task<T> LoadAssetAsync<T>(string assetPath) where T : UnityEngine.Object
    {
        if (_handles.TryGetValue(assetPath, out var cachedHandle))
            return cachedHandle.AssetObject as T;

        var handle = _package.LoadAssetAsync<T>(assetPath);
        await handle;
        _handles[assetPath] = handle;
        return handle.AssetObject as T;
    }

    public void Release(string assetPath)
    {
        if (_handles.TryGetValue(assetPath, out var handle))
        {
            handle.Release();
            _handles.Remove(assetPath);
        }
    }
}
```

**配套接口实现**：

```csharp
// 内置资源查询：检查 StreamingAssets 中是否存在该文件
private class GameBuildinQueryServices : IBuildinQueryServices
{
    public bool QueryStreamingAssets(string packageName, string fileName)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, packageName, fileName);
        return File.Exists(filePath);
    }
}

// 远端地址服务：拼接 CDN URL
private class GameRemoteServices : IRemoteServices
{
    private readonly string _hostServer;
    public GameRemoteServices(string hostServer) => _hostServer = hostServer;

    public string GetRemoteMainURL(string fileName)     => $"{_hostServer}/{fileName}";
    public string GetRemoteFallbackURL(string fileName) => $"{_hostServer}/{fileName}";
}
```

---

### 第五节：完整示例

```csharp
public class GameLauncher : MonoBehaviour
{
    private async void Start()
    {
        await ResourceManager.Instance.InitializeAsync("http://your-cdn.com/");

        Sprite icon = await ResourceManager.Instance.LoadAssetAsync<Sprite>(
            "Assets/GameRes/UI/icon.png");

        GetComponent<Image>().sprite = icon;
    }

    private void OnDestroy()
    {
        ResourceManager.Instance.Release("Assets/GameRes/UI/icon.png");
    }
}
```

---

### 第六节：小结

| 方法 | 职责 |
|---|---|
| `InitializeAsync` | 封装三步初始化，对外隐藏 YooAsset 细节 |
| `LoadAssetAsync<T>` | 加载资源，内部缓存 handle，避免重复加载 |
| `Release` | 按路径释放 handle，统一管理资源生命周期 |

末尾一句预告第五篇（HFSM 多层状态机）。

---

## 代码规范

- 所有代码必须基于 YooAsset 3.0 API，写前核对官方文档确保可运行
- 错误写法：文字描述，不给代码块
- 正确写法：完整可运行代码
- 所有代码块加 `csharp` 语言标识

---

## 不在范围内

- 补丁下载流程（`PreDownloadContentAsync`）的完整实现
- `EditorSimulateMode` 和 `OfflinePlayMode` 的初始化代码
- 资源引用计数的手动管理
- Addressable 与 YooAsset 对比
- 打包配置（Bundle Collector / Bundle Builder）
