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
    /// SaveManager
    /// - 支持延迟 Restore（方案B）
    /// - 增加快照有效性 / 空写防护 / 未变化跳过 / 首次恢复前禁止自动保存 等安全策略
    /// - 同步与异步保存统一使用收集逻辑，并仅在真正写盘后清除 Dirty
    ///
    /// PATCH 2025-09-04:
    /// 修复问题：增量保存时，未 Dirty 的 Provider 没被 Capture，
    ///           导致其 Section 不被新快照包含，从而“丢失”之前的存档数据（背包消失）。
    /// 方案：CollectSections 时对未脏（且已有旧 Section）的 Provider 复制旧 Section 进入新快照。
    ///       仅当 Provider 是 Dirty 且 Capture 结果为 null 时才真正移除。
    ///       这样保证“增量保存”是覆盖+保留，而不是“重建仅脏部分”。
    /// </summary>
    public class SaveManager : MonoBehaviour, SingletonInterfaces.ISyncInitialize
    {
        [Header("Config (ScriptableObject)")]
        public SaveSystemConfig config;

        [Header("State (Debug)")]
        [SerializeField] private bool _loaded;           // 是否已经有 _composite（来自磁盘或刚创建）
        [SerializeField] private bool _restored;         // 是否已对当前已注册 Provider 执行 Restore
        [SerializeField] private int _loadedVersion;
        [SerializeField] private long _loadedTimestamp;
        [SerializeField] private int _sectionCount;

        // Providers
        private readonly List<ISaveSectionProvider> _providers = new();

        // 延迟恢复标记（无 Provider 时加载了存档）
        private bool _pendingRestore;

        // 自动保存
        private Coroutine _autoSaveRoutine;

        // 存档聚合根
        private SaveRootData _composite;
        private ISaveEncryptor _encryptor;
        private IHashProvider _hash;
        private ISaveSerializer _serializer;
        private SaveService _service;

        // 快照安全与去重
        private string _lastSnapshotHash;
        private bool _hasValidSnapshot;                         // 已经至少成功写过一次“有效快照”
        private bool _firstRestoreCompleted => _restored && !_pendingRestore;

        // 异步保存任务
        private Task _ongoingSaveTask;

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
                    InternalSave(isManual: true);
                }
            }

            if (config.autoDiscoverProvidersOnSceneLoad)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            // 若不等待单例系统就恢复
            if (_loaded && config.loadOnAwake && !config.restoreAfterSingletonsReady)
            {
                AttemptRestore(); // 可能进入延迟
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void Initialize()
        {
            // 单例系统完成后再尝试恢复
            if (config.restoreAfterSingletonsReady && _loaded && !_restored)
            {
                AttemptRestore();
            }
            if (config.autoIntervalEnabled)
                _autoSaveRoutine = StartCoroutine(AutoSaveLoop());
        }

        #endregion

        #region 延迟 Restore 逻辑 (方案B)

        /// <summary>尝试恢复：若无 Provider 且有数据 -> 进入延迟模式。</summary>
        private void AttemptRestore()
        {
            if (_restored) return;
            RestoreToProviders_Internal(allowDefer: true);
        }

        /// <summary>在有 Provider 注册或场景加载后，如果处于延迟状态则执行真正的恢复。</summary>
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

            // 延迟 -> 正式恢复尝试
            TryRunDeferredRestore();

            // 兼容配置：再次尝试
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

        public void RegisterProvider(ISaveSectionProvider provider) => TryRegisterProvider(provider);

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

            // 若之前延迟恢复，现在尝试
            if (_pendingRestore && _loaded && !_restored)
            {
                if (config.verboseLog)
                    Debug.Log($"[SaveManager] Provider {p.GetType().Name} 注册，执行延迟恢复。");
                TryRunDeferredRestore();
                return true;
            }

            // 已经恢复完成后新注册的 Provider 需要单独恢复
            if (_loaded && _restored)
            {
                RestoreSingleProvider(p);
            }

            return true;
        }

        #endregion

        #region 快照哈希 / 工具

        private string ComputeSnapshotHash(List<SectionBlob> sections)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < sections.Count; i++)
                {
                    var s = sections[i];
                    h = h * 31 + (s.key?.GetHashCode() ?? 0);
                    h = h * 31 + (s.type?.GetHashCode() ?? 0);
                    h = h * 31 + (s.json?.GetHashCode() ?? 0);
                }
                return h.ToString("X");
            }
        }

        #endregion

        #region 同步保存 / 加载 / 恢复

        public void ManualSave() => InternalSave(isManual: true);

        public void ManualLoad(bool restoreAfter = true)
        {
            InternalLoad();
            if (restoreAfter && _loaded)
            {
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
                _sectionCount = 0;
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

        private void InternalSave(bool isManual = false, bool forceAll = false)
        {
            if (!CanProceedSave(isManual))
            {
                if (config.verboseLog)
                    Debug.Log("[SaveManager] 保存被跳过（条件不满足）。");
                return;
            }

            if (_composite == null)
                _composite = new SaveRootData();

            // 收集 Section
            var dirtyProvidersToClear = new List<IDirtySaveSectionProvider>();
            var newList = CollectSections(forceAll, isManual, dirtyProvidersToClear);

            if (!ValidateSnapshotList(newList, isManual))
                return;

            // 变化检测
            if (config.skipIfUnchanged)
            {
                string hash = ComputeSnapshotHash(newList);
                if (_lastSnapshotHash == hash)
                {
                    if (config.verboseLog)
                        Debug.Log("[SaveManager] Snapshot 未变化，跳过写盘。");
                    return;
                }
                _lastSnapshotHash = hash;
            }

            _composite.sections = newList;
            _service.Save(_composite);
            _loaded = true;

            if (newList.Count >= Math.Max(1, config.minSectionCountForValidSnapshot))
                _hasValidSnapshot = true;

            // 成功写盘后才清除 Dirty
            for (int i = 0; i < dirtyProvidersToClear.Count; i++)
                dirtyProvidersToClear[i].ClearDirty();

            if ((isManual || config.logOnEveryAutoSave) && config.verboseLog)
                Debug.Log($"[SaveManager] Save 完成 (Sync) Sections={newList.Count} manual={isManual}");
        }

        /// <summary>内部恢复（allowDefer 控制是否允许进入延迟模式）。</summary>
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
                    FastString
                        .Acquire()
                        .Tag("SaveManager")
                        .T("RestoreSingleProvider DefaultFail ")
                        .T(e.Message)
                        .Log();
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

        #region 异步保存 / 加载

        public Task ManualSaveAsync(bool forceAll = false, CancellationToken ct = default)
            => InternalSaveAsync(forceAll, isManual: true, ct);

        private async Task InternalSaveAsync(bool forceAll, bool isManual, CancellationToken ct)
        {
            if (!CanProceedSave(isManual))
            {
                if (config.verboseLog)
                    Debug.Log("[SaveManager] (Async) 保存被跳过（条件不满足）。");
                return;
            }

            if (_ongoingSaveTask != null && !_ongoingSaveTask.IsCompleted)
            {
                if (config.verboseLog)
                    Debug.LogWarning("[SaveManager] (Async) 上一次保存未结束，跳过。");
                return;
            }

            if (_composite == null)
                _composite = new SaveRootData();

            var dirtyProviders = new List<IDirtySaveSectionProvider>();
            var newList = CollectSections(forceAll, isManual, dirtyProviders);

            if (!ValidateSnapshotList(newList, isManual))
                return;

            if (config.skipIfUnchanged)
            {
                string hash = ComputeSnapshotHash(newList);
                if (_lastSnapshotHash == hash)
                {
                    if (config.verboseLog)
                        Debug.Log("[SaveManager] (Async) Snapshot 未变化，跳过写盘。");
                    return;
                }
                _lastSnapshotHash = hash;
            }

            _composite.sections = newList;
            Task task = _service.SaveAsync(_composite, ct);
            _ongoingSaveTask = task;
            await task;
            _loaded = true;

            if (newList.Count >= Math.Max(1, config.minSectionCountForValidSnapshot))
                _hasValidSnapshot = true;

            // 成功后清除 dirty
            for (int i = 0; i < dirtyProviders.Count; i++)
                dirtyProviders[i].ClearDirty();

            if ((isManual || config.logOnEveryAutoSave) && config.verboseLog)
                Debug.Log($"[SaveManager] Save 完成 (Async) Sections={newList.Count} manual={isManual}");
        }

        #endregion

        #region 保存策略 / 校验逻辑

        /// <summary>是否允许继续保存。</summary>
        private bool CanProceedSave(bool isManual)
        {
            if (!_loaded && !isManual && config.forbidAutoSaveBeforeFirstRestore)
            {
                if (!_firstRestoreCompleted)
                {
                    FastString.Acquire().Tag("SaveManager")
                        .T("首次自动保存被拒绝（未加载存档且未完成恢复）")
                        .Log();
                    return false;
                }
            }

            if (!isManual && config.forbidAutoSaveBeforeFirstRestore && !_firstRestoreCompleted)
            {
                FastString.Acquire().Tag("SaveManager")
                    .T("禁止首次恢复前的自动保存")
                    .Log();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 收集 Providers 的 Section，不清除 dirty（写盘成功后再清除）。
        ///
        /// PATCH: 现在会把“未脏且未捕获”的 Provider 的旧 Section 复制进新列表，避免丢失。
        /// </summary>
        private List<SectionBlob> CollectSections(bool forceAll, bool isManual, List<IDirtySaveSectionProvider> dirtyProvidersUsed)
        {
            var newList = new List<SectionBlob>(_providers.Count + 2);

            // 旧快照映射：key -> old blob
            Dictionary<string, SectionBlob> oldMap = null;
            if (_composite != null && _composite.sections != null && _composite.sections.Count > 0)
                oldMap = _composite.sections.ToDictionary(s => s.key, s => s);

            foreach (var p in _providers)
            {
                if (p == null || !p.Ready)
                    continue;

                var dirtyProv = p as IDirtySaveSectionProvider;

                // 条件：未强制保存 + 是脏接口 + 不是脏 -> 复制旧数据（如果存在）
                if (!forceAll && dirtyProv != null && !dirtyProv.Dirty)
                {
                    if (oldMap != null)
                    {
                        // 尝试获取 Section Key（需要 SectionType）
                        var sectionType = GetProviderSectionType(p);
                        string key = GetProviderKey(p, sectionType);

                        if (key != null && oldMap.TryGetValue(key, out var oldBlob))
                        {
                            newList.Add(oldBlob);
                            if (config.verboseLog)
                                Debug.Log($"[SaveManager] Preserve(unchanged) key={key}");
                        }
                        else
                        {
                            // 无旧 Section：代表以前从未写入过；跳过即可
                            if (config.verboseLog)
                                Debug.Log($"[SaveManager] Skip provider(no previous section) {p.GetType().Name}");
                        }
                    }
                    else
                    {
                        // 没有旧快照，直接跳过（没有数据可保留）
                        if (config.verboseLog)
                            Debug.Log($"[SaveManager] Skip provider(no old map) {p.GetType().Name}");
                    }
                    continue;
                }

                // 需要捕获（脏 / 强制 / 非 dirty 接口实现）
                ISaveSection sectionInstance;
                try
                {
                    sectionInstance = p.Capture();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] Capture 异常 {p.GetType().Name}: {e}");
                    continue;
                }

                if (sectionInstance == null)
                {
                    // 注意：这里仅当“本次 provider 明确返回 null”才视为删除/跳过，
                    // 若不是脏的场景已经被上面逻辑处理。
                    if (config.verboseLog)
                        Debug.Log($"[SaveManager] Provider {p.GetType().Name} Capture=null (removed or empty).");
                    continue;
                }

                Type type = sectionInstance.GetType();
                string sectionJson;
                try
                {
                    sectionJson = _serializer.Serialize(sectionInstance);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveManager] 序列化 Section 失败 {p.GetType().Name}: {e}");
                    continue;
                }

                string keyFinal = GetProviderKey(p, type);
                string typeName = config.storeTypeAssemblyQualified ? type.AssemblyQualifiedName : type.FullName;

                newList.Add(new SectionBlob
                {
                    key = keyFinal,
                    type = typeName,
                    json = sectionJson
                });

                if (dirtyProv != null && !dirtyProvidersUsed.Contains(dirtyProv))
                    dirtyProvidersUsed.Add(dirtyProv);
            }

            return newList;
        }

        /// <summary>校验快照列表是否满足保存条件。</summary>
        private bool ValidateSnapshotList(List<SectionBlob> list, bool isManual)
        {
            int count = list.Count;

            if (count == 0)
            {
                if (!config.allowEmptySnapshot && !isManual)
                {
                    if (config.verboseLog)
                        Debug.Log("[SaveManager] 空快照（自动）被拒绝。");
                    return false;
                }
                // 手动保存或允许空快照：允许，但不标记有效
                return true;
            }

            if (config.minSectionCountForValidSnapshot > 0 &&
                count < config.minSectionCountForValidSnapshot)
            {
                if (!isManual)
                {
                    if (config.verboseLog)
                        Debug.Log($"[SaveManager] 快照 Section 数 {count} < 最小要求 {config.minSectionCountForValidSnapshot}，跳过。");
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region 自动保存钩子

        private IEnumerator AutoSaveLoop()
        {
            WaitForSeconds wait = new(Mathf.Max(5f, config.autoIntervalSeconds));
            while (true)
            {
                yield return wait;
                InternalSave(isManual: false);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (config.saveOnPause && pause)
                InternalSave(isManual: false);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (config.saveOnFocusLost && !focus)
                InternalSave(isManual: false);
        }

        private void OnApplicationQuit()
        {
            if (config.saveOnQuit)
                InternalSave(isManual: false);
        }

        #endregion

        #region 依赖 / 反射工具

        private void SetupDependencies()
        {
            _serializer = new UnityJsonSerializer(config.prettyJson);
            _encryptor = config.enableEncryption
                ? (ISaveEncryptor)NoOpEncryptor.Instance // 预留：替换为真正加密
                : NoOpEncryptor.Instance;
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

        #region 开发期接口 / 调试

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

        public void DevForceSave() => InternalSave(isManual: true);

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
            _lastSnapshotHash = null;
            _hasValidSnapshot = false;
        }

        public void DevLogProviders()
        {
            Debug.Log($"[SaveManager] Providers={_providers.Count} loaded={_loaded} restored={_restored} pending={_pendingRestore} validSnap={_hasValidSnapshot}");
            foreach (var p in _providers)
                Debug.Log($"  - {p.GetType().Name} Ready={p.Ready}");
        }

        #endregion
    }
}