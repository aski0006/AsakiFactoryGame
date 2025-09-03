# 单例管理器（SingletonManager）使用说明

## 概述
单例管理器（`SingletonManager`）是一个用于管理游戏中全局单例模块的系统，支持按配置自动创建、注册单例，并按优先级执行多阶段初始化流程。适用于管理音频管理器、分析管理器等需要全局唯一实例的模块。


## 核心功能
- 自动检测或创建单例实例
- 支持按优先级排序初始化
- 多阶段初始化（预初始化、同步初始化、异步初始化）
- 支持通过类型或Key获取单例实例
- 场景加载时自动检测迟来的单例模块


## 快速开始

### 1. 创建配置文件
1. 在Project窗口右键 -> `Game/Singletons/Singleton Manager Config`
2. 命名为`SingletonManagerConfig`（默认名称）


### 2. 配置单例模块
在创建的配置文件中添加单例条目（Entry），每个条目包含以下设置：
- `key`：可选，用于通过Key获取实例的唯一标识
- `prefabComponent`：拖拽预制体上的MonoBehaviour组件（必须是Project窗口中的预制体）
- `priority`：优先级（数值越大越先初始化）
- `instantiateIfMissing`：场景中找不到时是否自动实例化
- `dontDestroyOnLoad`：实例化后是否标记为不随场景销毁
- `optional`：是否为可选模块（缺失时不报错）


### 3. 放置管理器到场景
1. 在首个启动场景中创建空物体，命名为`SingletonManager`
2. 挂载`SingletonManager`脚本
3. 将步骤1创建的配置文件拖入`config`字段
4. （可选）调整`autoInitialize`（是否在Awake时自动初始化）等选项


### 4. 创建单例模块脚本
单例模块需继承`MonoBehaviour`，并根据需求实现以下接口（来自`SingletonInterfaces`）：

| 接口 | 说明 | 调用时机 |
|------|------|----------|
| `ISingletonModule` | 标记接口（非必须，用于分类） | 无 |
| `IPreInitialize` | 预初始化（数据预读取） | 初始化阶段1 |
| `ISyncInitialize` | 同步初始化（轻量逻辑） | 初始化阶段2 |
| `IAsyncInitialize` | 异步初始化（资源加载等） | 初始化阶段3 |
| `IOnRegistered` | 注册时回调（获取其他实例） | 实例被注册后 |


#### 示例：音频管理器
```csharp
using UnityEngine;
using Game.Singletons.Example;

public class AudioManagerExample : MonoBehaviour, 
    SingletonInterfaces.ISyncInitialize, 
    SingletonInterfaces.ISingletonModule
{
    public void Initialize()
    {
        Debug.Log("[AudioManager] 初始化：加载音频设置");
    }
}
```

#### 示例：分析管理器（带异步初始化）
```csharp
using System.Collections;
using UnityEngine;

public class AnalyticsManagerExample : MonoBehaviour,
    SingletonInterfaces.IPreInitialize,
    SingletonInterfaces.IAsyncInitialize
{
    public void PreInitialize()
    {
        Debug.Log("[AnalyticsManager] 准备缓存目录");
    }

    // 协程版异步初始化（不使用UniTask时）
    public IEnumerator InitializeAsync()
    {
        Debug.Log("[AnalyticsManager] 异步初始化开始...");
        yield return new WaitForSeconds(0.8f);
        Debug.Log("[AnalyticsManager] 异步初始化完成");
    }
}
```


## 获取单例实例

### 通过类型获取
```csharp
// 直接获取（不存在时抛异常）
var audioManager = GM.Get<AudioManagerExample>();

// 尝试获取（不存在时返回false）
if (GM.TryGet<AnalyticsManagerExample>(out var analytics))
{
    // 使用analytics实例
}
```

### 通过Key获取
```csharp
// 直接获取（不存在时抛异常）
var component = SingletonManager.Instance.GetByKey("Audio");

// 尝试获取
if (SingletonManager.Instance.TryGetByKey("Analytics", out var comp))
{
    // 使用comp实例
}
```


## 初始化流程说明
1. **排序阶段**：按`priority`降序排序所有条目
2. **实例化/绑定阶段**：
    - 先在场景中查找已存在的实例
    - 未找到且允许自动实例化时，从预制体创建
3. **初始化阶段**：
    - **PreInitialize**：执行所有实现`IPreInitialize`的模块
    - **SyncInitialize**：执行所有实现`ISyncInitialize`的模块
    - **AsyncInitialize**：执行所有实现`IAsyncInitialize`的模块（异步）
4. **完成**：触发`OnAllInitialized`事件


## 注意事项
1. 预制体必须放在Project窗口中，不能是场景中的对象
2. 避免同一类型的组件在配置中重复出现
3. 异步初始化阶段会按顺序执行，前一个异步完成后才会执行下一个
4. 场景加载时会自动检测并注册迟来的单例模块（需在配置中存在）
5. 若启用`dontDestroyOnLoad`，实例会在场景切换时保留


## 扩展接口说明
- `OnAllInitialized`：所有模块初始化完成后触发的事件
- `OnSingletonRegistered`：每个单例被注册时触发的事件
- `LateScanScene`：场景加载后检测未注册的单例模块（内部使用）


通过以上步骤，您可以快速实现游戏中全局单例模块的统一管理和初始化流程，确保模块间的依赖关系和初始化顺序正确。