using Game.Save.Core;
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

public class SaveManager : MonoBehaviour,
                           SingletonInterfaces.ISyncInitialize // 可让 SingletonManager 在同步阶段调用 Initialize()
{

    [Header("Config (ScriptableObject)")]
    public SaveSystemConfig config;

    [Header("State (Debug)")]
    [SerializeField] private bool _loaded;
    [SerializeField] private bool _restored;
    [SerializeField] private int _loadedVersion;
    [SerializeField] private long _loadedTimestamp;
    [SerializeField] private int _sectionCount;

    // 运行时：已注册 Provider
    private readonly List<ISaveSectionProvider> _providers = new List<ISaveSectionProvider>();

    // 自动保存
    private Coroutine _autoSaveRoutine;
    // 临时缓存：加载的 composite
    private SaveRootData _composite;
    private ISaveEncryptor _encryptor;
    private IHashProvider _hash;
    private ISaveSerializer _serializer;

    private SaveService _service;
    public static SaveManager Instance { get; private set; }

        #region 自动保存

    private IEnumerator AutoSaveLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(5f, config.autoIntervalSeconds));
        while (true)
        {
            yield return wait;
            if (!_loaded) continue; // 还没加载（或尚未创建）就跳过
            InternalSave();
        }
    }

        #endregion

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
                // 首次无存档 -> 写入默认
                InternalSave();
            }
        }

        if (config.autoDiscoverProvidersOnSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void Initialize()
    {
        // 来自 SingletonManager 的同步初始化回调(如果在配置里)
        if (config.restoreAfterSingletonsReady && _loaded && !_restored)
        {
            RestoreToProviders();
        }
        if (config.autoIntervalEnabled)
        {
            _autoSaveRoutine = StartCoroutine(AutoSaveLoop());
        }
    }

        #endregion

        #region Provider 发现 / 注册

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DiscoverProvidersInScene(scene);
        // 场景加载后若之前已加载存档且还没 restore 过，也可尝试 restore
        if (_loaded && !_restored && (!config.restoreAfterSingletonsReady || SingletonManager.Instance?.IsInitialized == true))
        {
            RestoreToProviders();
        }
    }

    private void DiscoverProvidersInScene(Scene scene)
    {
        var all = FindObjectsOfType<MonoBehaviour>(config.discoverInactiveGameObjects);
        int added = 0;
        foreach (MonoBehaviour mb in all)
        {
            if (mb is ISaveSectionProvider p)
            {
                TryRegisterProvider(p);
                added++;
            }
        }
        if (config.verboseLog)
            Debug.Log($"[SaveManager] Scene '{scene.name}' provider scan: +{added} (total {_providers.Count})");
    }

    /// <summary>
    ///     供外部（或 provider 自己 OnEnable）调用的注册。
    /// </summary>
    public void RegisterProvider(ISaveSectionProvider provider)
    {
        TryRegisterProvider(provider);
    }

    public void UnregisterProvider(ISaveSectionProvider provider)
    {
        if (provider == null) return;
        _providers.Remove(provider);
    }

    private void TryRegisterProvider(ISaveSectionProvider p)
    {
        if (p == null) return;
        Type t = p.GetType();
        string fullName = t.FullName;
        if (!string.IsNullOrEmpty(fullName))
        {
            if (config.explicitIncludeTypeNames.Count > 0 &&
                !config.explicitIncludeTypeNames.Contains(fullName))
                return;

            if (config.excludeTypeNames.Contains(fullName))
                return;
        }

        if (_providers.Contains(p)) return;
        _providers.Add(p);

        // 如果已经有存档并且已经做过 Restore，新的 provider 需要单独 Restore
        if (_loaded && _restored)
        {
            RestoreSingleProvider(p);
        }
    }

        #endregion

        #region 同步保存 / 加载 / 恢复

    public void ManualSave()
    {
        InternalSave();
    }

    public void ManualLoad(bool restoreAfter = true)
    {
        InternalLoad();
        if (restoreAfter && _loaded)
            RestoreToProviders();
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

        // 抓取所有 provider section
        var newList = new List<SectionBlob>();
        foreach (ISaveSectionProvider p in _providers)
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
                json = sectionJson,
            });
        }

        _composite.sections = newList;
        _service.Save(_composite);
        _loaded = true; // 已有存档
        _restored = true; // 当前内存即为最新状态

        if (config.logOnEveryAutoSave && config.verboseLog)
            Debug.Log($"[SaveManager] Save 完成，Sections={newList.Count}");
    }

    private void RestoreToProviders()
    {
        if (_composite == null)
        {
            if (config.verboseLog)
                Debug.Log("[SaveManager] 无 composite 数据，跳过 Restore。");
            return;
        }

        int success = 0;
        foreach (ISaveSectionProvider p in _providers)
        {
            if (RestoreSingleProvider(p))
                success++;
        }

        _restored = true;
        if (config.verboseLog)
            Debug.Log($"[SaveManager] Restore 完成，成功 {success}/{_providers.Count}");
    }

    private bool RestoreSingleProvider(ISaveSectionProvider p)
    {
        if (p == null) return false;
        if (_composite?.sections == null) return false;

        // 取得 key & section blob
        Type targetType = GetProviderSectionType(p);
        string key = GetProviderKey(p, targetType);
        SectionBlob blob = _composite.sections.FirstOrDefault(s => s.key == key);
        if (blob == null)
        {
            // 没有数据 -> 默认/忽略
            try { p.Restore(null); }
            catch { }
            return false;
        }

        // 类型匹配检查
        Type resolvedType = ResolveSectionType(blob.type);
        if (resolvedType == null || targetType == null)
        {
            Debug.LogWarning($"[SaveManager] 无法解析 section 类型: {blob.type}");
            return false;
        }
        if (resolvedType != targetType)
        {
            // 如果类型不一样，可以尝试兼容（例如版本迁移后换了类名）：
            // 本示例直接警告并跳过。
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
        return true;
    }

        #endregion

        #region 异步 保存 / 加载 / 恢复

    private Task _ongoingSaveTask;
    
    public Task ManualSaveAsync(bool forceAll = false, CancellationToken ct = default(CancellationToken))
    {
        return InternalSaveAsync(forceAll, ct);
    }
    
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
        _restored = true;

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

            // 脏过滤
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
                json = sectionJson,
            });

            if (p is IDirtySaveSectionProvider dirtyAfter)
                dirtyAfter.ClearDirty();
        }
        _composite.sections = newList;
    }

        #endregion

        #region 事件钩子

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

        #region 辅助 / 工具

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

        // 默认策略：SectionType.FullName
        return sectionType?.FullName ?? provider.GetType().FullName;
    }

    private Type GetProviderSectionType(ISaveSectionProvider provider)
    {

        if (provider is IExposeSectionType expose && expose.SectionType != null)
            return expose.SectionType;

        // 退化：尝试通过命名约定（XxxProvider -> XxxSection）
        string name = provider.GetType().FullName;
        if (name != null)
        {
            string guess = name.Replace("Provider", "Section");
            Type t = Type.GetType(guess);
            if (t != null && typeof(ISaveSection).IsAssignableFrom(t))
                return t;
        }
        // 最终无法推断 -> 需要在首次 Save 前调用一次 Capture() 获取真实类型
        // 为避免副作用，这里返回 null，首次 Save 时会实际 Capture 并写入。
        return null;
    }

    private Type ResolveSectionType(string storedTypeName)
    {
        if (string.IsNullOrEmpty(storedTypeName)) return null;
        Type t = Type.GetType(storedTypeName);
        if (t != null) return t;

        // 兼容：如果存的是 FullName 而当前需要 AssemblyQualifiedName，或反之
        // 这里简单再次遍历所有已加载程序集搜索一次（轻量项目可接受）
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type candidate = asm.GetType(storedTypeName);
            if (candidate != null) return candidate;
        }
        return null;
    }

        #endregion

        #region 调试 / 开发期接口

    public void DevForceDiscover()
    {
        DiscoverProvidersInScene(SceneManager.GetActiveScene());
    }

    public void DevForceRestore()
    {
        if (!_loaded)
        {
            Debug.LogWarning("[SaveManager] 未加载任何存档，无法 Restore。");
            return;
        }
        RestoreToProviders();
    }

    public void DevForceSave()
    {
        InternalSave();
    }

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
        _composite = null;
    }

        #endregion
}
