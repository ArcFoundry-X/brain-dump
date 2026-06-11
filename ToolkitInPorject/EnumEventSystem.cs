using System;
using System.Collections.Generic;
using UnityEngine;

public interface IUnSubscribe
{
    void UnSubscribe();
}

public struct CustomUnSubscribe : IUnSubscribe
{
    private Action _onUnSubscribe;

    public CustomUnSubscribe(Action onUnSubscribe)
    {
        _onUnSubscribe = onUnSubscribe;
    }

    public void UnSubscribe()
    {
        _onUnSubscribe?.Invoke();
        _onUnSubscribe = null;
    }
}

public abstract class UnSubscribeTrigger : UnityEngine.MonoBehaviour
{
    private readonly HashSet<IUnSubscribe> mUnSubscribes = new HashSet<IUnSubscribe>();

    public IUnSubscribe AddUnSubscribe(IUnSubscribe unSubscribe)
    {
        mUnSubscribes.Add(unSubscribe);
        return unSubscribe;
    }

    public void RemoveUnSubscribe(IUnSubscribe unSubscribe) => mUnSubscribes.Remove(unSubscribe);

    public void UnSubscribeAll()
    {
        foreach (var unSubscribe in mUnSubscribes)
        {
            unSubscribe.UnSubscribe();
        }

        mUnSubscribes.Clear();
    }
}

public class UnSubscribeOnDestroyTrigger : UnSubscribeTrigger
{
    private void OnDestroy()
    {
        UnSubscribeAll();
    }
}

public class UnSubscribeOnDisableTrigger : UnSubscribeTrigger
{
    private void OnDisable()
    {
        UnSubscribeAll();
    }
}

public static class UnSubscribeExtension
{
    static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        var trigger = gameObject.GetComponent<T>();

        if (!trigger)
        {
            trigger = gameObject.AddComponent<T>();
        }

        return trigger;
    }

    public static IUnSubscribe UnSubscribeWhenGameObjectDestroyed(this IUnSubscribe unSubscribe,
        UnityEngine.GameObject gameObject) =>
        GetOrAddComponent<UnSubscribeOnDestroyTrigger>(gameObject)
            .AddUnSubscribe(unSubscribe);

    public static IUnSubscribe UnSubscribeWhenGameObjectDestroyed<T>(this IUnSubscribe self, T component)
        where T : UnityEngine.Component =>
        self.UnSubscribeWhenGameObjectDestroyed(component.gameObject);

    public static IUnSubscribe UnSubscribeWhenDisabled<T>(this IUnSubscribe self, T component)
        where T : UnityEngine.Component =>
        self.UnSubscribeWhenDisabled(component.gameObject);

    public static IUnSubscribe UnSubscribeWhenDisabled(this IUnSubscribe unSubscribe,
        UnityEngine.GameObject gameObject) =>
        GetOrAddComponent<UnSubscribeOnDisableTrigger>(gameObject)
            .AddUnSubscribe(unSubscribe);
}

public class EnumEventSystem : SingletonManager<EnumEventSystem>
{
    private Dictionary<int, List<Action<object[]>>> _eventTable = new();

    public IUnSubscribe Subscribe<T>(T key, Action<object[]> onEvent) where T : IConvertible
    {
        int eventId = key.ToInt32(null);

        if (!_eventTable.TryGetValue(eventId, out var list))
        {
            list = new List<Action<object[]>>();
            _eventTable[eventId] = list;
        }
        
        list.Add(onEvent);

        return new CustomUnSubscribe(() =>
        {
            list.Remove(onEvent);

            if (list.Count == 0)
            {
                _eventTable.Remove(eventId);
            }
        });
    }

    public void UnSubscribe<T>(T key, Action<object[]> onEvent) where T : IConvertible
    {
        int eventId = key.ToInt32(null);

        if (_eventTable.TryGetValue(eventId, out var list))
        {
            list.Remove(onEvent);

            if (list.Count == 0)
            {
                _eventTable.Remove(eventId); // 清理
            }
        }
    }

    public void Fire<T>(T key, params object[] args) where T : IConvertible
    {
        int eventId = key.ToInt32(null);

        if (_eventTable.TryGetValue(eventId, out var list))
        {
            var snapshot = list.ToArray(); // 防止回调中修改

            foreach (var handler in snapshot)
            {
                handler?.Invoke(args);
            }
        }
    }
}