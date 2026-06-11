## Resource Kit

### 大致目录

Unity 资源管理涉及 6 大核心模块，学习建议按以下顺序推进：

**第一阶段：打好基础**

先理解 Asset 的本质。重点搞清楚 `Resources.Load` 的缺点（不能热更、会打入包体），以及为什么要用 AssetBundle。`ScriptableObject` 是配置数据的标准做法，要熟练掌握。

**第二阶段：深入 AssetBundle**

这是所有资源框架的底层。核心要搞清楚三件事：依赖关系（A 依赖 B，加载 A 前必须先加载 B）、压缩格式（LZ4 读取快适合运行时，LZMA 体积小适合网络传输）、卸载时机（`Unload(false)` 和 `Unload(true)` 的区别）。

**第三阶段：使用 YooAsset**

有了 AssetBundle 基础再看 YooAsset，会发现它就是把那些繁琐的事情封装好了。重点学 Package 配置、三种 PlayMode 的使用场景、以及你已经在实践的热更新流程。

**第四阶段：内存与异步**

ARPG 的性能瓶颈往往在这里。对象池、引用计数、UniTask 异步加载、预加载策略，这些要结合实际项目边做边学。

**第五阶段：构建管线**

项目成熟后再系统学，包括自动化打包脚本、Shader 变体收集、CI/CD 集成。



### AssetBundle

Unity AssetBundle 是一种将游戏资源（模型、贴图、预制件、场景等）打包成独立文件并在运行时按需加载的技术。它的工作流主要分为三步，打包、加载和卸载，对应的，它有三个问题

#### 重复打包

多个 AssetBundle 各自引用了同一个资源（比如一张纹理），而这张纹理没有被单独打成一个依赖包，导致它在每个引用它的包里都存了一份。

**举例**
假设有两个角色预制件：`Warrior.prefab` 和 `Mage.prefab`，它们都使用了同一张贴图 `Armor_Diffuse.png`。
我们将两个 Prefab 分别打包为 `warrior.bundle` 和 `mage.bundle`，而没有把这张共用贴图单独打包。
**结果**：

- `warrior.bundle` 包含了一份 `Armor_Diffuse.png`
- `mage.bundle` 也包含了一份 `Armor_Diffuse.png`

当同时加载这两个角色时，内存里会有两张完全一样的纹理，内存占用翻倍。如果这类贴图有几十张，项目内存会很快失控。包体大小也会因为重复存储而明显增加。

**正确做法**
将 `Armor_Diffuse.png` 打入一个单独的 `shared_textures.bundle`，让 `warrior.bundle` 和 `mage.bundle` 只记录对它的依赖，而不实际包含该纹理资源。加载时，必须先加载 `shared_textures.bundle`，再加载角色包，否则会出现引用丢失。

#### 依赖加载错误

如果在加载某个 AssetBundle 时，它所依赖的其他 Bundle 尚未加载，那么资源中的引用会断掉，

还是上面的例子：我们做好了依赖分离，`warrior.bundle` 依赖 `shared_textures.bundle`

现在代码这样写：

```c#
AssetBundle warriorBundle = AssetBundle.LoadFromFile("warrior.bundle");
GameObject warrior = warriorBundle.LoadAsset<GameObject>("Warrior");
Instantiate(warrior);
```

此时 `shared_textures.bundle` 还没有被加载，`warrior.bundle` 中依赖的贴图引用是断的。实例化出来的角色身上将缺少贴图，表现为紫色。

**正确做法**
必须先加载所有依赖包，再加载目标包：

```c#
AssetBundleManifest manifest = ... // 从主包获取依赖信息
string[] deps = manifest.GetAllDependencies("warrior.bundle");
foreach (string dep in deps)
    AssetBundle.LoadFromFile(dep);

AssetBundle warriorBundle = AssetBundle.LoadFromFile("warrior.bundle");
```

这就是著名的 **“依赖加载顺序”** 问题，复杂的项目里 AssetBundle 依赖树可能很深，手动管理极易出错

#### 卸载问题

`bundle.Unload(true)` 会强制释放该 AssetBundle 以及所有从它加载出来的资源对象（贴图、网格、材质等）。如果场景中还有物体引用了这些资源，它们会立刻丢失引用，渲染出错。

#### 增量更新与版本管理混乱

AssetBundle 的更新依赖 `Hash` 和 `CRC` 校验。如果服务器上的新包和本地旧包的关系处理不当，容易出现“下载了错误版本”或“一直重复下载”的问题。

**举例**
游戏打包时生成了 `AssetBundleManifest` 文件，记录了每个包的哈希值。热更新时，客户端对比本地的 manifest 与服务器上的最新 manifest，发现有差异的包就下载。
但如果因为打包流程错误（比如只替换了某些 AssetBundle 而没有同步更新 manifest），会导致哈希永远不匹配，每次进入游戏都重新下载全部资源，流量消耗激增。
另一个常见坑是，Unity 旧版本中 AssetBundle 的压缩格式变化或序列化版本升级，导致旧客户端下载新格式包后加载失败。

基于这些问题，unity官方推出了Addressable，而GitHub上也有一个很火的框架YooAsset，它们本质都是AssetBundle 资源管理框架





### YooAsset

基于3.0版本

#### 资源分组

使用它自带的Bundle Collector工具，对资源进行管理，Collect Path可以制定文件夹或者单个资源文件



#### 资源打包

分组完成后使用Bundle Builder工具进行打包





#### 资源加载

资源加载的完整流程是

```c#
InitializePackageAsync      // 初始化文件系统
        ↓
RequestPackageVersionAsync  // 获取版本号（本地或远端）
        ↓
LoadPackageManifestAsync  // 加载资源清单
```

资源加载流程完成后，就可以正式加载资源

```c#
// Task加载方式
async void Start()
{
    AssetHandle handle = package.LoadAssetAsync<AudioClip>("Assets/GameRes/Audio/bgMusic.mp3");
    await handle;
    AudioClip audioClip = handle.AssetObject as AudioClip;	
}
```









