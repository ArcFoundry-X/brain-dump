# Unity EventBus 知乎文章设计 Spec

## 元信息

- 系列：Unity 游戏框架从零搭建 第二篇
- 发布平台：知乎
- 目标读者：Unity 初中级开发者（熟悉 C# 基础，了解 MonoBehaviour 生命周期）
- 语言：简体中文
- 语气：自然、专业，承接第一篇风格
- 校准等级：INTERMEDIATE

---

## 文章标题（参考）

`Unity EventBus：让系统之间不再互相认识`

---

## 叙事策略

**问题驱动 + 从零推导**：玩家死亡场景引出耦合痛点，逐步推导出枚举事件、泛型 EventBus、完整用法，最后指出枚举局限并对比类型标识方案。

---

## 文章结构

### 第一节：开篇

- 场景：玩家死亡时，`Player` 需要通知 UI 更新、音效播放、成就解锁
- 展示"堆引用"的直接调用写法，`Player` 持有所有系统引用
- 点出问题：`Player` 不该知道这些系统的存在，每加一个需求就要改 `Player`
- 一句话引出 EventBus 思路：发送者只管广播，接收者自己决定监听

```csharp
// 反面示例：Player 堆满了其他系统的引用
public class Player : MonoBehaviour
{
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private AchievementManager achievementManager;

    private void Die()
    {
        uiManager.ShowGameOver();
        audioManager.PlayDeathSound();
        achievementManager.UnlockAchievement("FirstDeath");
        // 每加一个新需求，就要在这里加一行
    }
}
```

### 第二节：观察者模式

- 点名：这是**观察者模式（Observer）**，《游戏编程模式》第四章
- 核心角色：**发布者**不知道谁在监听，**订阅者**自己决定关注什么
- EventBus 是观察者模式的全局变体，发布者和订阅者彻底解耦，连引用都不需要互相持有
- Unity 的 `UnityEvent` 和 C# 的 `event` 关键字背后都是这个思想

### 第三节：定义事件 — 枚举

- 用枚举统一管理所有事件类型，避免魔法字符串，IDE 有补全

```csharp
public enum GameEvent
{
    OnPlayerDead,
    OnPlayerHurt,
    OnEnemyDead,
    OnScoreChanged,
}
```

### 第四节：EventBus 核心实现（泛型）

- 先说明仅用 `Action`（无参）不够用，事件往往需要携带数据
- 不同事件的参数类型不同，泛型是让一套结构支持任意参数类型的最小代价方案
- 用 `Dictionary<GameEvent, Action>` 存无参事件，`Dictionary<GameEvent, Delegate>` 存带参事件
- 设计成静态类，全局可访问，不需要持有引用

```csharp
public static class EventBus
{
    private static readonly Dictionary<GameEvent, Action> _events
        = new Dictionary<GameEvent, Action>();

    private static readonly Dictionary<GameEvent, Delegate> _eventsWithArgs
        = new Dictionary<GameEvent, Delegate>();

    // 无参订阅
    public static void Subscribe(GameEvent eventType, Action handler)
    {
        if (!_events.ContainsKey(eventType))
            _events[eventType] = null;
        _events[eventType] += handler;
    }

    public static void Unsubscribe(GameEvent eventType, Action handler)
    {
        if (_events.ContainsKey(eventType))
            _events[eventType] -= handler;
    }

    public static void Publish(GameEvent eventType)
    {
        if (_events.TryGetValue(eventType, out var handler))
            handler?.Invoke();
    }

    // 带参订阅
    public static void Subscribe<T>(GameEvent eventType, Action<T> handler)
    {
        if (!_eventsWithArgs.ContainsKey(eventType))
            _eventsWithArgs[eventType] = null;
        _eventsWithArgs[eventType] =
            Delegate.Combine(_eventsWithArgs[eventType], handler);
    }

    public static void Unsubscribe<T>(GameEvent eventType, Action<T> handler)
    {
        if (_eventsWithArgs.ContainsKey(eventType))
            _eventsWithArgs[eventType] =
                Delegate.Remove(_eventsWithArgs[eventType], handler);
    }

    public static void Publish<T>(GameEvent eventType, T arg)
    {
        if (_eventsWithArgs.TryGetValue(eventType, out var handler))
            (handler as Action<T>)?.Invoke(arg);
    }
}
```

### 第五节：完整示例

- 用玩家死亡场景串联全部模块
- `Player` 只需一行 `Publish`，各系统在 `Start` 订阅、`OnDestroy` 取消订阅
- 代码里带上 `Unsubscribe`，注释一句"对象销毁时务必取消订阅，否则回调会访问到已销毁的对象"，点到为止

```csharp
// Player：只管发布，不认识任何其他系统
public class Player : MonoBehaviour
{
    private void Die()
    {
        EventBus.Publish(GameEvent.OnPlayerDead);
        EventBus.Publish<int>(GameEvent.OnPlayerHurt, 100);
    }
}

// UIManager：自己决定关注什么
public class UIManager : MonoBehaviour
{
    private void Start()
    {
        EventBus.Subscribe(GameEvent.OnPlayerDead, ShowGameOver);
        EventBus.Subscribe<int>(GameEvent.OnPlayerHurt, UpdateHP);
    }

    private void OnDestroy()
    {
        // 对象销毁时务必取消订阅，否则回调会访问到已销毁的对象
        EventBus.Unsubscribe(GameEvent.OnPlayerDead, ShowGameOver);
        EventBus.Unsubscribe<int>(GameEvent.OnPlayerHurt, UpdateHP);
    }

    private void ShowGameOver() { /* ... */ }
    private void UpdateHP(int damage) { /* ... */ }
}
```

### 第六节：枚举方案的局限与类型标识对比

- 枚举两个痛点：
  1. 每加一种新事件都要改 `GameEvent` 枚举文件，多人协作容易产生冲突
  2. `Publish<int>` 和 `Subscribe<string>` 订阅同一个枚举 key，编译不报错，运行时才崩
- 引出类型标识方案：定义 `struct` 作为事件类型，参数直接写在结构体里，类型安全

```csharp
// 事件即类型，参数在结构体里
public struct PlayerDeadEvent { }
public struct PlayerHurtEvent { public int Damage; }

// 用法示意（不展示完整实现）
EventBus<PlayerHurtEvent>.Subscribe(OnPlayerHurt);
EventBus<PlayerHurtEvent>.Publish(new PlayerHurtEvent { Damage = 30 });
```

- 利弊对比：类型标识更安全、扩展性更好，但每种事件类型需要独立的静态 `EventBus<T>`，架构更复杂
- 结论：按项目规模和团队习惯选择，两者都有人用

### 第七节：线程问题

- 触发场景：`Task.Run`、网络回调等子线程里调用 `Publish`，回调就在子线程执行，触碰 Unity 对象直接报错
- 方案一：`SynchronizationContext`（轻量，适合偶发的跨线程派发）

```csharp
private static readonly SynchronizationContext _mainThread =
    SynchronizationContext.Current; // 在主线程初始化时捕获

public static void PublishOnMainThread<T>(GameEvent eventType, T arg)
{
    _mainThread.Post(_ => Publish(eventType, arg), null);
}
```

- 方案二：`MainThreadDispatcher`（适合频繁跨线程派发，队列缓冲）

```csharp
public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _queue = new Queue<Action>();

    private void Update()
    {
        while (_queue.Count > 0)
            _queue.Dequeue()?.Invoke();
    }

    public static void Enqueue(Action action) => _queue.Enqueue(action);
}
```

- 区别：`SynchronizationContext` 更轻，直接用；`Dispatcher` 更可控，在 `Update` 里统一处理
- 对大多数初中级项目：只在主线程调用 `Publish` 可完全规避此问题

### 第八节：小结

- 表格：三列（层、职责、设计模式）
- ASCII 架构图：展示 EventBus 作为中间层，发布者和订阅者互不相识
- 末尾一句话预告第三篇（配置表）

---

## 代码规范

- 枚举实现为主线，类型标识仅作示意，不展示完整实现
- `Unsubscribe` 出现在示例代码中，注释点到为止，不单独展开
- 线程问题两种方案均展示代码，各配一句话说明适用场景
- 所有代码块加 `csharp` 语言标识

---

## 不在范围内

- C# `event` 关键字与 EventBus 的详细对比
- 异步事件（async handler）
- 事件优先级、事件拦截
- 性能基准测试
- 类型标识方案的完整实现
