using Game.Singletons.Core;
using Game.Singletons.Core.Game.Singletons;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Singletons
{

   
        /// <summary>
        /// 全局单例管理器：根据配置创建/注册模块，并按优先级顺序执行多阶段初始化。
        /// 使用方式：
        /// 1. 创建一个配置 ScriptableObject（右键 -> Game/Singletons/Singleton Manager Config）。
        /// 2. 在首个启动场景放置一个包含本脚本的 GameObject，拖入配置引用。
        /// 3. 运行后自动初始化（可关闭 autoInitialize 转而手动调用）。
        /// </summary>
        [DefaultExecutionOrder(-4999)]
        public class SingletonManager : MonoBehaviour
        {
            public static SingletonManager Instance { get; private set; }

            [Header("Config")]
            public SingletonManagerConfig config;

            [Header("Options")]
            [Tooltip("是否在 Awake 自动初始化")]
            public bool autoInitialize = true;

            [Tooltip("日志详细输出")]
            public bool verboseLogging = true;

            [Tooltip("初始化完成后广播事件")]
            public event Action OnAllInitialized;

            /// <summary>单个实例注册事件（每注册一个调用一次）。</summary>
            public event Action<Component> OnSingletonRegistered;

            private readonly Dictionary<Type, Component> _typeMap = new();
            private readonly Dictionary<string, Component> _keyMap = new(StringComparer.Ordinal);

            private bool _initialized;

            public bool IsInitialized => _initialized;

            private void Awake()
            {
                if (Instance != null && Instance != this)
                {
                    if (verboseLogging)
                        Debug.LogWarning($"[SingletonManager] 场景中出现重复的 SingletonManager：{name}，自动销毁该对象。");
                    Destroy(gameObject);
                    return;
                }

                Instance = this;
                DontDestroyOnLoad(gameObject);
                SceneManager.sceneLoaded += OnSceneLoaded;

                if (autoInitialize)
                {
#if USE_UNITASK
                InitializeAsync().Forget();
#else
                    StartCoroutine(InitializeCoroutine());
#endif
                }
            }

            private void OnDestroy()
            {
                if (Instance == this)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    Instance = null;
                }
            }

            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (!_initialized) return;
                // 处理 Additive 加载后可能迟到出现的模块
                LateScanScene(scene);
            }

            private void LateScanScene(Scene scene)
            {
                if (config == null) return;
                foreach (var entry in config.Entries)
                {
                    if (entry?.prefabComponent == null) continue;
                    var t = entry.prefabComponent.GetType();
                    if (_typeMap.ContainsKey(t)) continue;

                    var found = FindFirstObjectByType(t) as Component;
                    if (found != null)
                    {
                        if (verboseLogging)
                            Debug.Log($"[SingletonManager] 在场景({scene.name})中发现迟到模块：{t.Name}，正在注册。");
                        RegisterInstance(entry, found);
                    }
                }
            }

        #region Public Access

            public T Get<T>() where T : class
            {
                if (_typeMap.TryGetValue(typeof(T), out var c))
                    return c as T;
                throw new InvalidOperationException($"[SingletonManager] 未找到类型 {typeof(T).Name} 的单例。");
            }

            public bool TryGet<T>(out T value) where T : class
            {
                if (_typeMap.TryGetValue(typeof(T), out var c))
                {
                    value = c as T;
                    return true;
                }
                value = null;
                return false;
            }

            public Component GetByKey(string key)
            {
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentNullException(nameof(key));
                if (_keyMap.TryGetValue(key, out var c))
                    return c;
                throw new InvalidOperationException($"[SingletonManager] 未找到 key = {key} 的单例。");
            }

            public bool TryGetByKey(string key, out Component component)
                => _keyMap.TryGetValue(key, out component);

        #endregion

#if USE_UNITASK
        public async Cysharp.Threading.Tasks.UniTask InitializeAsync()
        {
            if (_initialized) return;
            if (!ValidateConfig()) return;

            var ordered = OrderEntries();
            CreateOrBindInstances(ordered);

            RunPreInitialize(ordered);
            RunSyncInitialize(ordered);
            await RunAsyncInitializeUniTask(ordered);

            Finish();
        }
#else
            public IEnumerator InitializeCoroutine()
            {
                if (_initialized) yield break;
                if (!ValidateConfig()) yield break;

                var ordered = OrderEntries();
                CreateOrBindInstances(ordered);

                RunPreInitialize(ordered);
                RunSyncInitialize(ordered);
                yield return RunAsyncInitializeCoroutine(ordered);

                Finish();
            }
#endif

            private bool ValidateConfig()
            {
                if (config == null)
                {
                    Debug.LogError("[SingletonManager] 未指定配置 ScriptableObject。");
                    return false;
                }
                return true;
            }

            private List<SingletonManagerConfig.Entry> OrderEntries()
            {
                return config.Entries
                    .Where(e => e != null && e.prefabComponent != null)
                    .OrderByDescending(e => e.priority)
                    .ToList();
            }

            private void CreateOrBindInstances(List<SingletonManagerConfig.Entry> ordered)
            {
                foreach (var entry in ordered)
                {
                    var t = entry.prefabComponent.GetType();
                    if (_typeMap.ContainsKey(t))
                    {
                        if (verboseLogging)
                            Debug.Log($"[SingletonManager] 跳过 {t.Name} (已注册)。");
                        continue;
                    }

                    // 1. 场景中是否已有
                    var existing = FindFirstObjectByType(t) as Component;
                    Component instance = existing;

                    // 2. 若没有且需要实例化
                    if (instance == null && entry.instantiateIfMissing)
                    {
                        instance = InstantiatePrefabComponent(entry);
                    }

                    if (instance == null)
                    {
                        if (entry.optional)
                        {
                            if (verboseLogging)
                                Debug.Log($"[SingletonManager] 可选模块 {t.Name} 未找到 / 未实例化，跳过。");
                            continue;
                        }
                        Debug.LogError($"[SingletonManager] 无法获取或创建模块 {t.Name}。");
                        continue;
                    }

                    RegisterInstance(entry, instance);
                }
            }

            private Component InstantiatePrefabComponent(SingletonManagerConfig.Entry entry)
            {
                if (entry.prefabComponent == null) return null;
                var prefabGO = entry.prefabComponent.gameObject;
                if (!prefabGO)
                {
                    Debug.LogError($"[SingletonManager] 预制体缺失：{entry.prefabComponent.name}");
                    return null;
                }

                var go = Instantiate(prefabGO);
                go.name = $"[Singleton] {prefabGO.name}";
                if (entry.dontDestroyOnLoad)
                    DontDestroyOnLoad(go);

                var comp = go.GetComponent(entry.prefabComponent.GetType());
                if (!comp)
                {
                    Debug.LogError($"[SingletonManager] 在实例化预制体上未找到组件 {entry.prefabComponent.GetType().Name}");
                }
                return comp;
            }

            private void RegisterInstance(SingletonManagerConfig.Entry entry, Component instance)
            {
                var t = instance.GetType();
                if (_typeMap.ContainsKey(t))
                {
                    if (instance != _typeMap[t])
                    {
                        Debug.LogWarning($"[SingletonManager] 类型 {t.Name} 冲突：销毁重复实例 {instance.gameObject.name}");
                        Destroy(instance.gameObject);
                    }
                    return;
                }

                _typeMap[t] = instance;

                if (!string.IsNullOrEmpty(entry.key))
                {
                    if (_keyMap.ContainsKey(entry.key))
                        Debug.LogWarning($"[SingletonManager] key 重复：{entry.key}（覆盖旧引用）");
                    _keyMap[entry.key] = instance;
                }

                if (instance is SingletonInterfaces.IOnRegistered onReg)
                    onReg.OnRegistered(this);

                OnSingletonRegistered?.Invoke(instance);

                if (verboseLogging)
                    Debug.Log($"[SingletonManager] 注册：{t.Name} (key={entry.key}, priority={entry.priority})");
            }

            private void RunPreInitialize(List<SingletonManagerConfig.Entry> ordered)
            {
                foreach (var entry in ordered)
                {
                    var t = entry.prefabComponent.GetType();
                    if (!_typeMap.TryGetValue(t, out var comp)) continue;
                    if (comp is SingletonInterfaces.IPreInitialize pre)
                    {
                        if (verboseLogging) Debug.Log($"[SingletonManager] PreInitialize -> {t.Name}");
                        try { pre.PreInitialize(); }
                        catch (Exception ex) { Debug.LogError($"[SingletonManager] PreInitialize {t.Name} 异常: {ex}"); }
                    }
                }
            }

            private void RunSyncInitialize(List<SingletonManagerConfig.Entry> ordered)
            {
                foreach (var entry in ordered)
                {
                    var t = entry.prefabComponent.GetType();
                    if (!_typeMap.TryGetValue(t, out var comp)) continue;
                    if (comp is SingletonInterfaces.ISyncInitialize sync)
                    {
                        if (verboseLogging) Debug.Log($"[SingletonManager] Initialize -> {t.Name}");
                        try { sync.Initialize(); }
                        catch (Exception ex) { Debug.LogError($"[SingletonManager] Initialize {t.Name} 异常: {ex}"); }
                    }
                }
            }

#if USE_UNITASK
        private async Cysharp.Threading.Tasks.UniTask RunAsyncInitializeUniTask(List<SingletonManagerConfig.Entry> ordered)
        {
            foreach (var entry in ordered)
            {
                var t = entry.prefabComponent.GetType();
                if (!_typeMap.TryGetValue(t, out var comp)) continue;
                if (comp is IAsyncInitialize asyncInit)
                {
                    if (verboseLogging) Debug.Log($"[SingletonManager] AsyncInitialize -> {t.Name}");
                    try { await asyncInit.InitializeAsync(); }
                    catch (Exception ex) { Debug.LogError($"[SingletonManager] AsyncInitialize {t.Name} 异常: {ex}"); }
                }
            }
        }
#else
            private IEnumerator RunAsyncInitializeCoroutine(List<SingletonManagerConfig.Entry> ordered)
            {
                foreach (var entry in ordered)
                {
                    var t = entry.prefabComponent.GetType();
                    if (!_typeMap.TryGetValue(t, out var comp)) continue;
                    if (comp is SingletonInterfaces.IAsyncInitialize asyncInit)
                    {
                        if (verboseLogging) Debug.Log($"[SingletonManager] AsyncInitialize -> {t.Name}");
                        IEnumerator routine = null;
                        try
                        {
                            routine = asyncInit.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[SingletonManager] 获取 AsyncInitialize 协程时异常 {t.Name}: {ex}");
                        }
                        if (routine != null)
                            yield return StartCoroutine(routine);
                    }
                }
            }
#endif

            private void Finish()
            {
                _initialized = true;
                if (verboseLogging)
                    Debug.Log("[SingletonManager] 所有模块初始化完成。");
                OnAllInitialized?.Invoke();
            }
        }
    
}
