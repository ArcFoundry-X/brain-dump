## 对象池

unity中一些对象，需要频繁使用，但Instantiate和Destory是非常消耗性能的，这种情况下，我们就希望能将这些对象缓存起来，生成之后并不Destory，而是放到一个数据结构比如Stack中，留着下次再使用。

这样的话需要对池子中的对象有一个规范，比如什么东西能放入对象池，分配和回收的时候对象该执行什么动作，使用接口就非常合适。

### IPoolItem

具体对象，一个对象只要继承自这个接口，就能成为对象池中的对象。每个对象应该拥有Allocate和Recycle方法，处理申请和回收相关动作

```c#
public interface IPoolItem
{
    void OnAllocate();
    
    void OnRecycle();
}
```



### ObjectPool<T>

对象池，管理具体的对象。

通过Func和Action给他传入具体的回调。



### ObjectPoolManager

对象池管理类，在使用对象池的时候，不直接接触具体的池对象，而是通过它注册对象池后再使用

