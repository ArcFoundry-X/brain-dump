# Unity 对象池知乎文章设计 Spec

## 元信息

- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，了解 MonoBehaviour 生命周期）
- 语言：简体中文
- 语气：自然、专业
- 校准等级：INTERMEDIATE

---

## 文章标题（参考）

`Unity 对象池：告别 Instantiate 的 GC 噩梦`

---

## 叙事策略

**问题驱动 + 从零推导**：每个设计决策都有前因，读者跟着"踩坑再填坑"的节奏走，不会感觉设计凭空冒出。

---

## 文章结构

### 第一节：开篇与问题引入

- 直接抛出场景：子弹 / 特效频繁创建销毁
- 一句话点出 `Instantiate` / `Destroy` 带来的 GC 开销
- 不展开论证，快速进入"那怎么办"

### 第二节：朴素写法与它的问题

- 展示用 `List` 手动攒池的反面示例
- 暴露三个问题：无状态约束、调用方耦合、对象不知道自己被回收
- 为后续接口设计埋下伏笔

```csharp
// 反面示例：无约束的朴素写法
private List<Bullet> _pool = new List<Bullet>();

public Bullet Get() {
    if (_pool.Count > 0) {
        var b = _pool[0];
        _pool.RemoveAt(0);
        b.gameObject.SetActive(true); // 调用方要自己记得激活
        return b;
    }
    return Instantiate(bulletPrefab);
}
```

### 第三节：用接口建立契约 — IPoolItem

- 引出问题：池不应该知道具体对象细节，但需要在取出/回收时触发行为
- 定义 `IPoolItem` 接口，两个方法：`OnAllocate` / `OnRecycle`
- 说明为什么用接口而不是基类：`MonoBehaviour` 已占继承位，接口不破坏继承链

```csharp
public interface IPoolItem
{
    void OnAllocate();  // 从池中取出时调用
    void OnRecycle();   // 回收进池时调用
}
```

### 第四节：泛型池 — ObjectPool\<T\>

- 用 `Stack<T>` 缓存对象（LIFO，缓存局部性好）
- 通过 `Func<T>` 注入创建逻辑，`Action<T>` 注入销毁逻辑，池本身零依赖
- 池为空时自动调用 `_onCreate` 扩容
- 展示 `Allocate` 和 `Recycle` 核心方法

```csharp
public class ObjectPool<T> where T : IPoolItem
{
    private readonly Stack<T> _stack = new Stack<T>();
    private readonly Func<T> _onCreate;
    private readonly Action<T> _onDestroy;

    public ObjectPool(Func<T> onCreate, Action<T> onDestroy = null)
    {
        _onCreate = onCreate;
        _onDestroy = onDestroy;
    }

    public T Allocate()
    {
        var item = _stack.Count > 0 ? _stack.Pop() : _onCreate();
        item.OnAllocate();
        return item;
    }

    public void Recycle(T item)
    {
        item.OnRecycle();
        _stack.Push(item);
    }
}
```

### 第五节：统一管理 — ObjectPoolManager

- 问题：多种类型的池分散持有，引用管理混乱
- 用 `Dictionary<Type, object>` 存储所有池，按类型索引
- 调用方只需要知道类型，不持有池的引用

```csharp
public class ObjectPoolManager
{
    private readonly Dictionary<Type, object> _pools = new Dictionary<Type, object>();

    public void Register<T>(Func<T> onCreate, Action<T> onDestroy = null) where T : IPoolItem
    {
        _pools[typeof(T)] = new ObjectPool<T>(onCreate, onDestroy);
    }

    public T Allocate<T>() where T : IPoolItem
    {
        return GetPool<T>().Allocate();
    }

    public void Recycle<T>(T item) where T : IPoolItem
    {
        GetPool<T>().Recycle(item);
    }

    private ObjectPool<T> GetPool<T>() where T : IPoolItem
    {
        return (ObjectPool<T>)_pools[typeof(T)];
    }
}
```

### 第六节：完整使用示例

- 用子弹场景串联全部模块
- 展示：实现接口 → 注册池 → 取出 → 回收
- 补充知识点：对象可持有 Manager 引用，在碰撞回调里自己触发回收，点到为止

```csharp
// 1. 实现接口
public class Bullet : MonoBehaviour, IPoolItem
{
    private ObjectPoolManager _manager;

    public void Init(ObjectPoolManager manager) => _manager = manager;

    public void OnAllocate() => gameObject.SetActive(true);
    public void OnRecycle() => gameObject.SetActive(false);

    private void OnTriggerEnter(Collider other)
    {
        _manager.Recycle(this); // 碰撞时自己归还
    }
}

// 2. 注册池
var manager = new ObjectPoolManager();
manager.Register<Bullet>(
    onCreate: () => Instantiate(bulletPrefab).GetComponent<Bullet>(),
    onDestroy: b => Destroy(b.gameObject)
);

// 3. 取出
var bullet = manager.Allocate<Bullet>();
bullet.Init(manager);
```

### 第七节：结尾 — Unity 内置方案一句话

- Unity 2021 起内置 `UnityEngine.Pool.ObjectPool<T>`，开箱即用
- 自定义实现的价值：接口约束行为更规范，Manager 统一管理多类型池，适合有规模的项目
- 两者不冲突，按需选择

---

## 代码规范

- 所有正式实现代码用 `Stack<T>` 缓存
- 反面示例可用 `List` / `Queue` 以形成对比
- 代码块均加语言标识符 `csharp`
- 关键行加单行中文注释，明显行不加

---

## 不在范围内

- Profiler 截图或性能数字
- 线程安全处理
- 预热（预先填满池）
- 最大容量限制
- 深入对比 Unity 内置方案
