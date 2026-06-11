# GPU 显存账本：为什么低端机总在这个场景崩

Profiler 打开，CPU 内存显示 280 MB，一切正常。换台低端 Android 机运行，进了 BOSS 关卡，闪退。崩溃日志：`low memory`。

回头翻 PC 上的 Profiler，CPU 那边 280 MB 没动，但旁边那一栏写着 GPU Memory：476 MB。低端机的显存预算只有 256 MB。

这就是 GPU 显存的基本问题：它是一本独立的账，CPU 内存监控完全看不见。Unity 的 Profiler 统计项里，`Reserved Total` 和 `Used Total` 记录的是 CPU 侧（Managed + Native）的数字，不包含 GPU 显存。低端机 OOM 大多发生在这本账超限的时候——而开发机通常是高端机，显存预算宽裕，问题根本暴露不出来。

这一篇把 GPU 显存账本拆开来看：里面装的是什么、最大头在哪、最常见的几个高频问题怎么解。

---

## GPU 显存里住着什么

GPU 显存里的内容大致分三类：

**贴图**是最大头，通常占 GPU 显存的 60–80%。一张 1024×1024 RGBA32 的贴图，GPU 侧就是 4 MB（开 Mip Maps 的话约 5.3 MB）。场景里几十张这样的贴图叠起来，显存轻松过百兆。贴图是优化的首要目标，也是收益最大的地方。

**网格缓冲**（顶点/索引 buffer）在静态场景里几乎可以忽略，但骨骼动画的蒙皮网格、大量动态合批的对象、或者程序化生成的高密度网格，会让这部分显著增长。一个 10 万顶点的蒙皮角色，顶点 buffer 大概在 8–16 MB 量级（取决于顶点格式）。

**RenderBuffer** 包含颜色缓冲、深度缓冲、Shadow Map、后处理用的 RenderTexture。分辨率越高这部分越大——1080p 下一张带深度的颜色缓冲就是 8 MB 起步，复杂的后处理链（Bloom、TAA、景深）会再叠好几张全分辨率 RT。移动端特别要注意 Shadow Map 的分辨率，默认 4096×4096 的 Shadow Map 在 32 位精度下是 64 MB。

---

## 贴图内存怎么算

### 内存公式

```
显存占用 = 宽 × 高 × 每像素字节数 × mip 系数
```

mip 系数：开启 Mip Maps 时，完整 mip 链的总面积约为原始贴图的 4/3，所以 mip 系数 ≈ 1.33（1 + 1/4 + 1/16 + … = 4/3）。

### 格式对比表

| 格式 | 每像素字节数 | 1024×1024（无 mip） | 1024×1024（有 mip） |
|---|---|---|---|
| RGBA32（无压缩） | 4 字节 | 4.0 MB | 5.3 MB |
| DXT5 / BC3 | 1 字节 | 1.0 MB | 1.3 MB |
| ETC2（RGBA） | 1 字节 | 1.0 MB | 1.3 MB |
| ASTC 4×4 | 1 字节 | 1.0 MB | 1.3 MB |
| ASTC 8×8 | 0.25 字节 | 0.25 MB | 0.33 MB |

同样一张 1024×1024 贴图，RGBA32 和 ASTC 8×8 的显存占用差 16 倍。项目里如果有几十张用 RGBA32 导入的贴图，换成 ASTC 之后显存直接砍一个数量级。

### 压缩格式选择原则

- **iOS**：优先 ASTC（iPhone 6s 以上全部支持）。质量要求高用 ASTC 4×4，显存优先用 ASTC 8×8，两者都比 PVRTC 强。
- **Android**：高端机（2019 年后主流）用 ASTC；兼容较老设备用 ETC2（OpenGL ES 3.0+ 支持透明通道）；ETC1 不支持透明，需要额外分离 Alpha 通道贴图，现代项目一般不用。
- **PC / Console**：DXT5（BC3）是基线；BC7 质量更好，现代显卡全部支持。
- **UI 贴图**：关闭 Mip Maps。UI 是像素对齐显示的，透视缩放几乎不发生，mip 只会浪费 33% 的显存。
- **3D 场景贴图**：开启 Mip Maps。远处物体用低分辨率 mip，减少过采样伪影，GPU 采样带宽也降低（实际是省的）。

Unity 的 Texture Importer 可以按平台分别设置格式（Inspector → 切换到对应平台 Tab），推荐把每个平台的压缩格式作为项目规范写死，避免被默认的 RGBA32 悄悄留在包里。

---

## 三个高频显存问题

### Read/Write Enabled：内存翻倍的开关

Texture Importer 里有一个 **Read/Write Enabled** 复选框，勾上之后 Unity 在 Native 内存（CPU 侧）保留贴图的完整副本，同时 GPU 侧也有一份。也就是说，同一张贴图占了两份显存量级的内存：一份在 CPU 的 Native 堆，一份在 GPU 显存。

```
1024×1024 RGBA32 贴图：
  Read/Write 关闭：GPU 显存 4 MB，CPU Native ~0（上传后释放）
  Read/Write 打开：GPU 显存 4 MB + CPU Native 4 MB = 8 MB
```

这个选项只有在需要运行时通过 CPU 读写像素（`GetPixels`、`SetPixels`、`EncodeToPNG`）时才需要开启。其他情况一律关闭。

常见的误开场景：美术把贴图拖进 Unity 时默认没有勾选，但脚本里某处调用了 `texture.GetPixels()` 读取颜色——Unity 会抛错，于是开发者勾上 Read/Write 解错，之后没有关闭。全项目搜索 `GetPixels` / `SetPixels` 的调用点，确认是否真的必要，不必要的换成 GPU 侧操作（`Graphics.Blit`、Shader 处理）。

检测方法：Memory Profiler 快照里，展开 Textures 列表，找 `isReadable = true` 的贴图，逐一评估是否必要。

### RenderTexture 泄漏

`RenderTexture.GetTemporary` 是 Unity 提供的临时 RT 池——申请时从池里拿，用完归还给池重复使用，比每次 `new RenderTexture` 效率高。但"申请必须配对归还"这条规则一旦被破坏，RT 就永久占着显存。

```csharp
// 错误：申请了不归还，每次调用都让 RT 数量 +1
void OnRenderImage(RenderTexture src, RenderTexture dest)
{
    RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height);
    Graphics.Blit(src, rt, _blurMaterial);
    Graphics.Blit(rt, dest);
    // 忘记 ReleaseTemporary，rt 永远回不了池
}
```

```csharp
// 正确：用 try/finally 保证归还
void OnRenderImage(RenderTexture src, RenderTexture dest)
{
    RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height);
    try
    {
        Graphics.Blit(src, rt, _blurMaterial);
        Graphics.Blit(rt, dest);
    }
    finally
    {
        RenderTexture.ReleaseTemporary(rt);
    }
}
```

手动 `new RenderTexture` 的对象，销毁时要先调 `rt.Release()` 释放显存，再 `Destroy(rt)` 释放 C# 包装和 Native 数据：

```csharp
RenderTexture _rt;

void OnEnable()
{
    _rt = new RenderTexture(1024, 1024, 24);
    _rt.Create();
}

void OnDisable()
{
    _rt.Release(); // 释放 GPU 显存
    Destroy(_rt);  // 释放 Native 对象
}
```

只调 `Destroy` 不调 `Release`，显存在 `Destroy` 同步执行（帧末）时也会被释放，两种方式最终结果一样——但显式调 `Release` 语义更清晰，也方便在对象池场景里单独管理显存的释放时机。

### Mesh.UploadMeshData(true)

默认情况下，Mesh 上传 GPU 之后 CPU 侧（Native）仍然保留一份顶点/索引数据副本，用于运行时的射线检测、蒙皮计算等 CPU 侧操作。对于永远不会被 CPU 访问的静态网格，这是纯粹的浪费——同一份顶点数据在 CPU 和 GPU 各占一份。

`Mesh.UploadMeshData(true)` 把顶点数据上传 GPU 并释放 CPU 侧副本：

```csharp
// 适用于运行时不再修改、不在 CPU 侧读取的静态网格
void Start()
{
    var mesh = GetComponent<MeshFilter>().mesh;
    mesh.UploadMeshData(true); // 上传后释放 CPU 副本
}

// 此后调用 mesh.vertices 返回空数组
// Physics.Raycast 仍然有效（射线检测走 Collider，不走 Mesh 顶点数据）
```

代价：调用后无法再通过 `mesh.vertices`、`mesh.GetTriangles` 等 CPU 侧 API 读取网格数据；也不能再修改顶点（`mesh.SetVertices` 不起作用）。适用于确定不会在运行时被修改的装饰性静态网格——地形装饰物、建筑、道具等。角色、动态变形物体不要用。

---

## Sprite Atlas vs 散图

散图导入时每张独立上传到 GPU，GPU 每次切换绑定的贴图都有开销，渲染一批使用不同贴图的 Sprite 需要多个 Draw Call。Sprite Atlas 把多张散图合并为一张大贴图，渲染同一 Atlas 里的所有 Sprite 只需一个 Draw Call。

显存角度：

```
5 张 256×256 RGBA32 散图：5 × 0.25 MB = 1.25 MB，每张独立上传

合并为 1 张 512×512 Atlas（够装 4 张）+ 1 张 256×256 溢出：
  总显存 ≈ 0.25 MB（512×512）+ 0.25 MB（溢出）= 相近
  但 Draw Call 从 5 次降到 2 次
```

Atlas 不总是省显存——合并后的大贴图如果有大量空白区域（Padding），实际利用率可能比散图更低。Atlas 的主要收益在 Draw Call 和 GPU 贴图切换次数，显存上可能略有增加。

注意 Atlas 尺寸限制：单张 Atlas 上限一般是 2048×2048（移动端）或 4096×4096（PC），超出后 Unity 自动分成多个 Atlas 包。大量 UI 图标统一放一个 Atlas 是常规做法，但要避免把从不同时出现的图标（如不同场景的 UI）强行塞进同一个 Atlas——那样两个场景加载时 Atlas 都要驻留显存，比散图占用更多。

---

## GPU 显存优化检查清单

| 检查项 | 目标状态 |
|---|---|
| 移动端贴图压缩格式 | ASTC（高端机）/ ETC2（兼容机），不用 RGBA32 |
| UI 贴图 Mip Maps | 全部关闭 |
| 不需要 CPU 读写的贴图 Read/Write | 全部关闭 |
| 临时 RenderTexture | `GetTemporary` 配对 `ReleaseTemporary` |
| 自建 RenderTexture | `OnDisable` 里 `Release` + `Destroy` |
| 运行时不修改的静态网格 | 考虑 `Mesh.UploadMeshData(true)` |
| Shadow Map 分辨率 | 移动端降到 1024 或 2048，按质量需求调整 |

GPU 显存超限没有运行时的缓冲空间——系统不会帮你把显存数据 swap 到内存，满了就是 OOM。所以这部分最值得在开发阶段就建立规范：在 Texture Importer 里锁死平台压缩格式、禁止不必要的 Read/Write，比上线前做 Memory Profiler 救火轻松得多。

下一篇走出 GPU 显存，去看 Job System 的 Native 内存体系——彻底消除 GC 压力需要遵循一套完全不同的内存规则。
