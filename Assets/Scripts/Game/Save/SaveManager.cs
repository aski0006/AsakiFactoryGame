using Game.Core.Debug.FastString;
using Game.Save.Core;
using Game.Singletons;
using Game.Singletons.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Save
{
    /// <summary>
    /// 方案B实现：
    /// - 若在尝试 Restore 时还没有任何 Provider，则不立刻标记 _restored，而是设置 _pendingRestore = true
    /// - 第一个（或之后） Provider 注册 / 场景扫描完成后，若存在 _pendingRestore，则执行真正的 Restore
    /// - 避免出现误导性的 “Restore 完成 0/0” 并仍能在稍后正确恢复
    /// </summary>
    public class SaveManager : MonoBehaviour, SingletonInterfaces.ISyncInitialize
    {
        [Header("Config (ScriptableObject)")] public SaveSystemConfig config;

        [Header("State (Debug)")]
        [SerializeField] private bool _loaded;
        [SerializeField] private bool _restored;
        [SerializeField] private int _loadedVersion;
        [SerializeField] private long _loadedTimestamp;
        [SerializeField] private int _sectionCount;

        // Providers
        private readonly List<ISaveSectionProvider> _providers = new();

        // 延迟恢复标记（方案B新增）
        private bool _pendingRestore;

        // 自动保存
        private Coroutine _autoSaveRoutine;

        // 存档聚合
        private SaveRootData _composite;
        private ISaveEncryptor _encryptor;
        private IHashProvider _hash;
        private ISaveSerializer _serializer;
        private SaveService _service;

        public static SaveManager Instance { get; private set; }

        #region Unity 生命周期

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!config)
            {
                Debug.LogError("[SaveManager] 缺少 SaveSystemConfig 资产。");
                enabled = false;
                return;
            }

            SetupDependencies();

            if (config.loadOnAwake)
            {
                InternalLoad();
                if (config.writeImmediatelyAfterFirstLoad && !_loaded)
                {
                    InternalSave();
                }
            }

            if (config.autoDiscoverProvidersOnSceneLoad)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            // 如果不等待单例就恢复（restoreAfterSingletonsReady=false），先尝试延迟恢复
            if (_loaded && config.loadOnAwake && !config.restoreAfterSingletonsReady)
            {
                AttemptRestore(); // 可能会进入延迟
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void Initialize()
        {
            // 单例系统完成后再尝试
            if (config.restoreAfterSingletonsReady && _loaded && !_restored)
            {
                AttemptRestore();
            }
            if (config.autoIntervalEnabled)
                _autoSaveRoutine = StartCoroutine(AutoSaveLoop());
        }

        #endregion

        #region 方案B: 延迟 Restore 逻辑

        /// <summary>
        /// 外部发起一次“尝试恢复”操作。若无 Provider 则进入延迟。
        /// </summary>
        private void AttemptRestore()
        {
            if (_restored) return; // 已恢复过
            RestoreToProviders_Internal(allowDefer: true);
        }

        /// <summary>
        /// 在 Provider 注册或场景扫描后，若需要执行延迟恢复则调用。
        /// </summary>
        private void TryRunDeferredRestore()
        {
            if (_pendingRestore && _loaded && !_restored)
            {
                RestoreToProviders_Internal(allowDefer: false);
            }
        }

        #endregion

        #region Provider 发现 / 注册

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DiscoverProvidersInScene(scene);

            // 如果之前是延迟恢复，现在尝试真正恢复
            TryRunDeferredRestore();

            // 场景加载后（如果未等待单例或者单例已完成）也可能再尝试一次
            if (_loaded && !_restored && !config.restoreAfterSingletonsReady)
                TryRunDeferredRestore();
            else if (_loaded && !_restored && config.restoreAfterSingletonsReady &&
                     SingletonManager.Instance?.IsInitialized == true)
                TryRunDeferredRestore();
        }

        private void DiscoverProvidersInScene(Scene scene)
        {
            var all = FindObjectsOfType<MonoBehaviour>(config.discoverInactiveGameObjects);
            int added = 0;
            foreach (var mb in all)
            {
                if (mb is ISaveSectionProvider p)
                {
                    if (TryRegisterProvider(p))
                        added++;
                }
            }
            if (config.verboseLog)
                Debug.Log($"[SaveManager] Scene '{scene.name}' provider scan: +{added} (total {_providers.Count})");
        }

        public void RegisterProvider(ISaveSectionProvider provider)
        {
            TryRegisterProvider(provider);
        }

        public void UnregisterProvider(ISaveSectionProvider provider)
        {
            if (provider == null) return;
            _providers.Remove(provider);
        }

        private bool TryRegisterProvider(ISaveSectionProvider p)
        {
            if (p == null) return false;
            string fullName = p.GetType().FullName;

            if (!string.IsNullOrEmpty(fullName))
            {
                if (config.explicitIncludeTypeNames.Count > 0 &&
                    !config.explicitIncludeTypeNames.Contains(fullName))
                    return false;

                if (config.excludeTypeNames.Contains(fullName))
                    return false;
            }

            if (_providers.Contains(p)) return false;
            _providers.Add(p);

            // 若之前处于“延迟恢复”状态，现在有 Provider 加进来就执行真正恢复
            if (_pendingRestore && _loaded && !_restored)
            {
                if (config.verboseLog)
                    Debug.Log($"[SaveManager] Provider {p.GetType().Name} 注册，执行延迟恢复。");
                TryRunDeferredRestore();
                return true;
            }

            // 如果已经恢复过（_restored = true），新 Provider 需要单独 RestoreSingleProvider
            if (_loaded && _restored)
            {
                RestoreSingleProvider(p);
            }

            return true;
        }

        #endregion

        #region 同步保存 / 加载 / 恢复

        public void ManualSave() => InternalSave();

        public void ManualLoad(bool restoreAfter = true)
        {
            InternalLoad();
            if (restoreAfter && _loaded)
            {
                // 重新加载后重置相关状态
                _restored = false;
                _pendingRestore = false;
                AttemptRestore();
            }
        }

        private void InternalLoad()
        {
            _composite = _service.Load();
            if (_composite == null)
            {
                _loaded = false;
                _loadedVersion = SaveRootData.CurrentVersion;
                _loadedTimestamp = 0;
                if (config.verboseLog)
                    Debug.Log("[SaveManager] 无现存档，等待创建。");
                return;
            }

            _loaded = true;
            _loadedVersion = _composite.version;
            _loadedTimestamp = _composite.lastSaveUnix;
            _sectionCount = _composite.sections?.Count ?? 0;

            if (config.verboseLog)
                Debug.Log($"[SaveManager] 已加载存档 version={_loadedVersion} sections={_sectionCount}");
        }

        private void InternalSave()
        {
            if (_composite == null)
                _composite = new SaveRootData();

            var newList = new List<SectionBlob>();
            foreach (var p in _providers)
            {
                if (p == null || !p.Ready) continue;
                ISaveSection section;
                try { section = p.Capture(); }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] Capture 异常 {p.GetType().Name}: {e}");
                    continue;
                }
                if (section == null) continue;

                Type type = section.GetType();
                string sectionJson = _serializer.Serialize(section);
                string key = GetProviderKey(p, type);
                string typeName = config.storeTypeAssemblyQualified ? type.AssemblyQualifiedName : type.FullName;

                newList.Add(new SectionBlob
                {
                    key = key,
                    type = typeName,
                    json = sectionJson
                });
            }

            _composite.sections = newList;
            _service.Save(_composite);
            _loaded = true;
            // 保存代表当前内存是最新快照，但不改变恢复的语义；保持 _restored 状态不强制 true

            if (config.logOnEveryAutoSave && config.verboseLog)
                Debug.Log($"[SaveManager] Save 完成，Sections={newList.Count}");
        }

        /// <summary>
        /// 内部恢复（allowDefer 控制是否允许进入延迟模式）。
        /// </summary>
        private void RestoreToProviders_Internal(bool allowDefer)
        {
            if (_composite == null)
            {
                if (config.verboseLog)
                    Debug.Log("[SaveManager] 无 composite 数据，跳过 Restore。");
                return;
            }

            if (_providers.Count == 0)
            {
                if (allowDefer && _composite.sections != null && _composite.sections.Count > 0)
                {
                    _pendingRestore = true;
                    if (config.verboseLog)
                        Debug.Log("[SaveManager] 进入延迟 Restore 状态（尚无 Provider）。");
                }
                else
                {
                    if (config.verboseLog)
                        Debug.Log("[SaveManager] Restore 0/0 (无 Provider 且不允许延迟)");
                }
                return;
            }

            int success = 0;
            foreach (var p in _providers)
            {
                if (RestoreSingleProvider(p))
                    success++;
            }

            _restored = true;
            _pendingRestore = false;

            if (config.verboseLog)
                Debug.Log($"[SaveManager] Restore 完成，成功 {success}/{_providers.Count}");
        }

        /// <summary>
        /// 对外使用的 Restore 调用（保持原语义）。
        /// </summary>
        private void RestoreToProviders() => RestoreToProviders_Internal(allowDefer: false);

        private bool RestoreSingleProvider(ISaveSectionProvider p)
        {
            if (p == null) return false;
            if (_composite?.sections == null) return false;

            Type targetType = GetProviderSectionType(p);
            string key = GetProviderKey(p, targetType);
            SectionBlob blob = _composite.sections.FirstOrDefault(s => s.key == key);
            if (blob == null)
            {
                try { p.Restore(null); }
                catch (Exception e)
                {
                    FastString.Acquire().Tag("SaveManager").T("RestoreSingleProvider DefaultFail ").T(e.Message).Log();
                }
                return false;
            }

            Type resolvedType = ResolveSectionType(blob.type);
            if (resolvedType == null || targetType == null)
            {
                Debug.LogWarning($"[SaveManager] 无法解析 section 类型: {blob.type}");
                return false;
            }
            if (resolvedType != targetType)
            {
                Debug.LogWarning($"[SaveManager] Section 类型不一致: blob={resolvedType.FullName} target={targetType.FullName}");
            }

            ISaveSection instance = null;
            try
            {
                instance = (ISaveSection)_serializer.Deserialize(resolvedType, blob.json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] 反序列化 Section 失败 key={key} err={e}");
            }

            try
            {
                p.Restore(instance);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Restore 调用异常 provider={p.GetType().Name} err={e}");
                return false;
            }
            if (config.verboseLog)
                Debug.Log($"[SaveManager] 单 Provider Restore 成功 key={key}");
            return true;
        }

        #endregion

        #region 异步 保存 / 加载 / 恢复

        private Task _ongoingSaveTask;

        public Task ManualSaveAsync(bool forceAll = false, CancellationToken ct = default) =>
            InternalSaveAsync(forceAll, ct);

        private async Task InternalSaveAsync(bool forceAll, CancellationToken ct)
        {
            if (_ongoingSaveTask != null && !_ongoingSaveTask.IsCompleted)
            {
                if (config.verboseLog)
                    Debug.LogWarning("[SaveManager] 上一次异步保存尚未结束，跳过本次。");
                return;
            }

            PrepareCompositeSections(forceAll);
            Task task = _service.SaveAsync(_composite, ct);
            _ongoingSaveTask = task;
            await task;
            _loaded = true;

            if (config.logOnEveryAutoSave && config.verboseLog)
                Debug.Log($"[SaveManager] Save 完成 (Async)，Sections={_composite.sections.Count}");
        }

        private void PrepareCompositeSections(bool forceAll)
        {
            if (_composite == null)
                _composite = new SaveRootData();

            var newList = new List<SectionBlob>();
            foreach (ISaveSectionProvider p in _providers)
            {
                if (p == null || !p.Ready) continue;

                if (!forceAll && p is IDirtySaveSectionProvider dirtyProv && !dirtyProv.Dirty)
                    continue;

                ISaveSection section;
                try { section = p.Capture(); }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] Capture 异常 {p.GetType().Name}: {e}");
                    continue;
                }
                if (section == null) continue;

                Type type = section.GetType();
                string sectionJson = _serializer.Serialize(section);
                string key = GetProviderKey(p, type);
                string typeName = config.storeTypeAssemblyQualified ? type.AssemblyQualifiedName : type.FullName;

                newList.Add(new SectionBlob
                {
                    key = key,
                    type = typeName,
                    json = sectionJson
                });

                if (p is IDirtySaveSectionProvider dirtyAfter)
                    dirtyAfter.ClearDirty();
            }
            _composite.sections = newList;
        }

        #endregion

        #region 自动保存钩子

        private IEnumerator AutoSaveLoop()
        {
            WaitForSeconds wait = new(Mathf.Max(5f, config.autoIntervalSeconds));
            while (true)
            {
                yield return wait;
                if (!_loaded) continue;
                InternalSave();
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (config.saveOnPause && pause)
                InternalSave();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (config.saveOnFocusLost && !focus)
                InternalSave();
        }

        private void OnApplicationQuit()
        {
            if (config.saveOnQuit)
                InternalSave();
        }

        #endregion

        #region 工具 / 辅助

        private void SetupDependencies()
        {
            _serializer = new UnityJsonSerializer(config.prettyJson);
            _encryptor = config.enableEncryption ? (ISaveEncryptor)NoOpEncryptor.Instance : NoOpEncryptor.Instance;
            _hash = config.enableHash
                ? config.useSha256 ? Sha256HashProvider.Instance : NoOpHashProvider.Instance
                : NoOpHashProvider.Instance;

            _service = new SaveService(_serializer, _encryptor, _hash, config);

            if (config.verboseLog)
                Debug.Log("[SaveManager] 依赖初始化完成。");
        }

        private string GetProviderKey(ISaveSectionProvider provider, Type sectionType)
        {
            if (provider is ICustomSaveSectionKey custom && !string.IsNullOrWhiteSpace(custom.Key))
                return custom.Key;
            return sectionType?.FullName ?? provider.GetType().FullName;
        }

        private Type GetProviderSectionType(ISaveSectionProvider provider)
        {
            if (provider is IExposeSectionType expose && expose.SectionType != null)
                return expose.SectionType;

            string name = provider.GetType().FullName;
            if (name != null)
            {
                string guess = name.Replace("Provider", "Section");
                Type t = Type.GetType(guess);
                if (t != null && typeof(ISaveSection).IsAssignableFrom(t))
                    return t;
            }
            return null;
        }

        private Type ResolveSectionType(string storedTypeName)
        {
            if (string.IsNullOrEmpty(storedTypeName)) return null;
            Type t = Type.GetType(storedTypeName);
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = asm.GetType(storedTypeName);
                if (candidate != null) return candidate;
            }
            return null;
        }

        #endregion

        #region 开发期接口

        public void DevForceDiscover()
        {
            DiscoverProvidersInScene(SceneManager.GetActiveScene());
            TryRunDeferredRestore();
        }

        public void DevForceRestore()
        {
            if (!_loaded)
            {
                Debug.LogWarning("[SaveManager] 未加载任何存档，无法 Restore。");
                return;
            }
            _pendingRestore = false;
            _restored = false;
            RestoreToProviders_Internal(allowDefer: false);
        }

        public void DevForceSave() => InternalSave();

        public void DevDeleteSave()
        {
            string main = Path.Combine(Application.persistentDataPath, config.fileName);
            string bak = main + ".bak";
            if (File.Exists(main)) File.Delete(main);
            if (File.Exists(bak)) File.Delete(bak);
            if (config.verboseLog)
                Debug.Log("[SaveManager] 已删除存档文件。");
            _loaded = false;
            _restored = false;
            _pendingRestore = false;
            _composite = null;
        }

        public void DevLogProviders()
        {
            Debug.Log($"[SaveManager] Providers={_providers.Count} loaded={_loaded} restored={_restored} pending={_pendingRestore}");
            foreach (var p in _providers)
                Debug.Log($"  - {p.GetType().Name} Ready={p.Ready}");
        }

        #endregion
    }
}