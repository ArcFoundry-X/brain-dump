# Unity AssetBundle 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 第三篇
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，了解 MonoBehaviour 生命周期）
- 语言：简体中文
- 语气：自然、专业，承接前两篇风格
- 校准等级：INTERMEDIATE

---

## 文章标题（参考）

`Unity AssetBundle：资源热更的底层逻辑`

---

## 叙事策略

**问题驱动**：从 `Resources.Load` 的硬伤一句话引出 AssetBundle，介绍三步工作流建立基线，再逐节展示四个常见陷阱（错误写法一句话概括，重点放在问题原因和正确做法的代码），结尾一句话引出 YooAsset。

---

## 文章结构

### 第一节：开篇

- 点出 `Resources.Load` 的两个硬伤：所有资源全打入包体、不支持热更
- 不展开细节，一小段带过
- 引出 AssetBundle：按需加载、支持热更的标准方案

---

### 第二节：AssetBundle 是什么

- 介绍 AB 的三步工作流：**打包 → 加载 → 卸载**
- 每步给一段代码（正确用法基线，后续问题从这里延伸）

**打包**（Editor 脚本）：

```csharp
[MenuItem("Build/Build AssetBundles")]
static void Build()
{
    BuildPipeline.BuildAssetBundles(
        "Assets/StreamingAssets",
        BuildAssetBundleOptions.None,
        BuildTarget.StandaloneWindows
    );
}
```

打包时需要选择压缩格式，`BuildAssetBundleOptions` 对应三种选项，用一张表对比说明：

| 格式 | 选项 | 压缩率 | 解压速度 | 适用场景 |
|---|---|---|---|---|
| 不压缩 | `UncompressedAssetBundle` | ❌ 最大 | ✅ 最快 | 开发调试 |
| LZMA | `None`（默认） | ✅ 最小 | ❌ 慢，需整包解压 | 网络下载（减少流量） |
| LZ4 | `ChunkBasedCompression` | 中等 | ✅ 快，按块解压 | 运行时本地加载（推荐） |

重点说明：LZMA 体积最小适合网络传输，但运行时必须整包解压后才能读取，内存开销大；LZ4 按块解压，随机访问快，是本地缓存后运行时加载的推荐选择。实际项目通常做法是：下载时用 LZMA 节省流量，落盘后转换为 LZ4 再使用。

**加载**：

```csharp
AssetBundle bundle = AssetBundle.LoadFromFile(
    Path.Combine(Application.streamingAssetsPath, "warrior.bundle"));
GameObject prefab = bundle.LoadAsset<GameObject>("Warrior");
Instantiate(prefab);
```

**卸载**：

```csharp
bundle.Unload(false); // 释放 AssetBundle 文件，保留已加载的资源对象
```

---

### 第三节：重复打包

- **问题**：`warrior.bundle` 和 `mage.bundle` 都引用了 `Armor_Diffuse.png`，未单独打包，导致该纹理在两个 bundle 里各存一份，内存里出现两份相同贴图，包体也翻倍。
- **错误写法**：一句话概括——两个 prefab 直接各自打包，不做依赖分离。
- **正确做法**：将共用资源打入独立的 `shared_textures.bundle`；`warrior.bundle` 和 `mage.bundle` 只记录依赖，不实际包含该纹理。

```csharp
// 加载时：先加载依赖包，再加载目标包
AssetBundle sharedBundle = AssetBundle.LoadFromFile("shared_textures.bundle");
AssetBundle warriorBundle = AssetBundle.LoadFromFile("warrior.bundle");
```

---

### 第四节：依赖加载顺序

- **问题**：AB 的依赖关系由打包时生成的 `AssetBundleManifest` 记录。若依赖包未提前加载，目标包内的引用断开，实例化出的对象缺少贴图（渲染为紫色）。
- **错误写法**：一句话概括——直接加载 `warrior.bundle` 并实例化，跳过依赖加载。
- **正确做法**：从主包读取 manifest，用 `GetAllDependencies` 取得依赖列表，全部加载完毕后再加载目标包。

```csharp
AssetBundle manifestBundle = AssetBundle.LoadFromFile("AssetBundles");
AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");

string[] deps = manifest.GetAllDependencies("warrior.bundle");
foreach (string dep in deps)
    AssetBundle.LoadFromFile(dep);

AssetBundle warriorBundle = AssetBundle.LoadFromFile("warrior.bundle");
```

- 点出：复杂项目的依赖树可能很深，手动管理极易出错，这是框架存在的核心理由之一。

---

### 第五节：卸载时机

- **问题**：`Unload(true)` 会强制释放该 AssetBundle 及所有从它加载出来的资源对象；若场景中仍有物体引用这些资源，引用立刻断掉，渲染出错。`Unload(false)` 只释放 bundle 文件本身，不影响已加载的资源对象。
- **错误写法**：一句话概括——用完立刻 `Unload(true)`，场景中的角色贴图/网格瞬间丢失。
- **正确做法**：场景中仍有对象使用资源时，用 `Unload(false)`；确认无任何引用残留后再用 `Unload(true)` 彻底清理。

对比表：

| 参数 | 释放 Bundle 文件 | 释放已加载的资源对象 | 适用场景 |
|---|---|---|---|
| `Unload(false)` | ✅ | ❌ | 资源仍在使用中，只想关闭文件句柄 |
| `Unload(true)` | ✅ | ✅ | 确认无引用残留，彻底释放内存 |

---

### 第六节：版本管理混乱

- **问题**：热更新依赖 manifest 中的 hash 校验。若只上传了新的 bundle 文件，忘记同步更新 manifest，客户端对比 hash 永远不匹配，每次启动都触发全量重下载，流量消耗激增。
- **错误写法**：一句话概括——只替换部分 bundle 上传到服务器，manifest 仍是旧版。
- **正确做法**：每次打包必须整体重新生成 manifest，将新 manifest 与新 bundle 一并上传；客户端启动时下载最新 manifest，对比本地版本的 hash 差异，只下载有变动的包。

```csharp
// 伪代码示意：增量更新逻辑
string[] localHashes  = LoadLocalManifest();
string[] remoteHashes = await DownloadRemoteManifest();

foreach (var bundle in remoteHashes)
{
    if (localHashes[bundle] != remoteHashes[bundle])
        await DownloadBundle(bundle);
}
```

---

### 第七节：小结

- 总结四个问题对应 AB 生命周期的阶段：

| 问题 | 阶段 |
|---|---|
| 重复打包 | 打包 |
| 依赖加载顺序 | 加载 |
| 卸载时机 | 卸载 |
| 版本管理混乱 | 热更新 |

- 收尾一句话：

> 手动管理 AssetBundle，意味着要自己处理依赖树、缓存策略、版本校验——这正是 YooAsset 要替你做的事。

---

## 代码规范

- 错误写法：一句话文字概括，不单独给代码块
- 正确做法：给完整可运行的代码示例
- 所有代码块加 `csharp` 语言标识
- 伪代码（如版本管理示意）需注释说明"伪代码示意"

---

## 不在范围内

- YooAsset 的具体用法（留给第四篇）
- Addressable 的介绍与对比
- 打包策略的深入讨论（按场景/按类型/按模块）
- 加密与安全
