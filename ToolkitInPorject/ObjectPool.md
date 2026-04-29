## 对象池



### ObjectPoolManager

对象池管理类，外部类代码只能和他打交道，不能接触到具体的池对象



### ObjectPool

 对象池，最核心的就是分配对象Allocate 和回收对象Recycle



### IPoolable

可以归属为对象池的对象
