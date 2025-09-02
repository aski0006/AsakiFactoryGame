# 轻量分段式存档系统使用说明 (Updated)

> 版本：含异步保存、脏标记(IDirtySaveSectionProvider)、可视化编辑器 (SaveEditorWindow)、可扩展接口（序列化 / 加密 / 哈希）  
> 适用：独立 2D 游戏快速迭代，从简开始，可平滑升级。

---

## 目录
1. 系统概览
2. 关键结构与职责
3. 快速上手步骤
4. SaveSystemConfig 配置字段详解
5. 保存生命周期与调用时机
6. 分段 Provider 编写指南 (含脏标记)
7. 自定义 Key / 类型：ICustomSaveSectionKey / IExposeSectionType
8. 脏标记 (IDirtySaveSectionProvider) 工作流
9. 同步 vs 异步保存 (ManualSave / ManualSaveAsync)
10. 可视化编辑器 SaveEditorWindow
11. 不可直接序列化数据的处理策略（替身 / ID 映射）
12. 加密 / 哈希 / 备份机制
13. 错误处理 & 调试技巧
14. 版本演进与迁移思路
15. 性能建议
16. 常见问题 FAQ
17. 示例代码片段集锦
18. 下一步扩展建议

---

## 1. 系统概览

| 目标 | 实现 |
|------|------|
| 模块化 | 每个系统通过 ISaveSectionProvider 提供自己的 Section |
| 最小依赖 | 默认使用 UnityJsonSerializer (JsonUtility) |
| 可进化 | 接口: ISaveSerializer / ISaveEncryptor / IHashProvider |
| 分段存档 | SaveRootData.sections = List<SectionBlob> |
| 高迭代 | Raw JSON & 表单可视化编辑器 |
| 性能可控 | 脏标记 + 异步保存 |
| 安全/完整性 | 可选 SHA256 校验与备份文件 |
| 扩展空间 | 多槽位 / 压缩 / 迁移 / 云同步 后续添加 |

---

## 2. 关键结构与职责

| 类型 | 描述 |
|------|------|
| SaveManager | 运行时总控：注册 Provider，加载 / 恢复 / 保存（同步+异步） |
| SaveSystemConfig | ScriptableObject 配置参数 |
| ISaveSectionProvider | 模块提供分段快照 (Capture/Restore) |
| IDirtySaveSectionProvider | Provider 可声明自己是否有变动 (Dirty) 以便增量保存 |
| ICustomSaveSectionKey | 覆盖 Section 默认存档键 |
| IExposeSectionType | 显式声明 Section 类型避免反射猜测 |
| SaveRootData | 顶层数据：version / lastSaveUnix / sections |
| SectionBlob | 单个分段：key / type / json |
| SaveService | 纯 I/O：JSON ↔ 字节 ↔ (Encrypt/Hash) ↔ 磁盘 |
| UnityJsonSerializer | 默认序列化实现（可替换） |
| NoOpEncryptor / Sha256HashProvider | 默认加密(NoOp) / 哈希(SHA256) |
| SaveEditorWindow | 编辑器工具：查看、增删、修改、写回、推送运行时 |

---

## 3. 快速上手步骤

1. 创建配置：  
   右键 → Create → Game/Save/Save System Config (SaveSystemConfig.asset)

2. 在启动场景的 SingletonManager 配置中加入 SaveManager 预制体，并引用上一步的配置。

3. 为每个逻辑模块实现一个 Provider：
   ```csharp
   public class PlayerProvider : MonoBehaviour, ISaveSectionProvider, IExposeSectionType, ICustomSaveSectionKey {
       [Serializable] public class PlayerSection : ISaveSection {
           public string id;
           public int level;
           public float hp;
       }
       public string Key => "Player";
       public Type SectionType => typeof(PlayerSection);
       public bool Ready => true;

       public ISaveSection Capture()
           => new PlayerSection { id = _id, level = _level, hp = _hp };

       public void Restore(ISaveSection sec) {
           var s = sec as PlayerSection;
           if (s == null) return; // 首次或缺失
           _id = s.id; _level = s.level; _hp = s.hp;
       }

       void OnEnable()=> SaveManager.Instance?.RegisterProvider(this);
       void OnDisable()=> SaveManager.Instance?.UnregisterProvider(this);

       string _id="p1"; int _level=1; float _hp=100;
   }
   ```

4. 运行：
   - 首次无存档 → 如果 `writeImmediatelyAfterFirstLoad=false` 只在第一次保存时生成。
   - 用 `SaveManager.Instance.ManualSave()` / 自动触发保存。
   - 查看 `Application.persistentDataPath` 下的 `save.dat`。

5. 打开编辑器窗口：Tools → Save System → Save Editor  
   修改 Section → “写回磁盘” → （Play 模式下）“推送到运行时 & Save”。

---

## 4. SaveSystemConfig 配置字段详解

| 分类 | 字段 | 描述 |
|------|------|------|
| 路径与文件 | fileName | 存档文件名 |
| | useBackup | 是否在保存前复制旧文件为 .bak |
| 序列化 | prettyJson | 写回 JSON 时是否格式化（文件更易读但更大） |
| | storeTypeAssemblyQualified | 是否写入 AssemblyQualifiedName（跨重命名/程序集更稳） |
| 加密/哈希 | enableEncryption | 是否启用加密（当前为 NoOp，占位） |
| | enableHash | 是否启用 HASH 行（防篡改/损坏） |
| | useSha256 | true=SHA256, false=NOOP |
| 自动保存 | saveOnQuit / saveOnPause / saveOnFocusLost | 生命周期触发保存 |
| | autoIntervalEnabled / autoIntervalSeconds | 定时自动保存 |
| 初始化 | loadOnAwake | Awake 是否读取存档 |
| | restoreAfterSingletonsReady | 等 Singleton 全部初始化后再 Restore |
| | autoDiscoverProvidersOnSceneLoad | 场景加载时自动扫描 Provider |
| | discoverInactiveGameObjects | 扫描时包含未激活对象 |
| Provider 过滤 | explicitIncludeTypeNames | 非空时=白名单模式 |
| | excludeTypeNames | 黑名单 |
| 调试 | verboseLog | 详细日志 |
| | logOnEveryAutoSave | 自动保存时输出日志 |
| 其他 | writeImmediatelyAfterFirstLoad | 首次无存档时立即写入一个空基础文件 |

---

## 5. 保存生命周期与调用时机

1. Awake: 可选加载存档 (loadOnAwake)
2. SceneLoaded: 自动发现 Provider
3. SingletonManager 初始化阶段：调用 SaveManager.Initialize → RestoreToProviders
4. 运行时：
   - ManualSave()
   - ManualSaveAsync()
   - 自动：间隔 / 暂停 / 失焦 / 退出
5. RestoreSingleProvider：当一个新的 Provider 在 Restore 之后注册时立即补回数据

---

## 6. 分段 Provider 编写指南

必需实现：
```csharp
public interface ISaveSectionProvider {
    bool Ready { get; }
    ISaveSection Capture();
    void Restore(ISaveSection section);
}
```

最佳实践：
- Ready 在模块需要依赖（如资源表 / 数据库）完成后再返回 true。
- Capture 返回 null 表示“本次不写入”（比如无数据或未初始化）。
- Restore(null) 表示没有历史数据（首次或被删除）→ 设默认值。
- OnEnable / OnDisable 自动注册/注销。

---

## 7. Key / Section 类型控制

| 接口 | 用途 |
|------|------|
| ICustomSaveSectionKey | 自定义稳定 Key，避免类型重命名导致丢失 |
| IExposeSectionType | 明确 Section 类型，节省反射猜测步骤 |

示例：
```csharp
public class SettingsProvider : MonoBehaviour,
    ISaveSectionProvider, ICustomSaveSectionKey, IExposeSectionType {
    [Serializable] public class SettingsSection : ISaveSection { public float bgm=0.8f; }
    public string Key => "Settings";
    public Type SectionType => typeof(SettingsSection);
    public bool Ready => true;
    public ISaveSection Capture() => new SettingsSection { bgm = _bgm };
    public void Restore(ISaveSection s){ if(s is SettingsSection ss) _bgm = ss.bgm; }
    float _bgm = 0.8f;
}
```

---

## 8. 脏标记 (IDirtySaveSectionProvider) 工作流

接口：
```csharp
public interface IDirtySaveSectionProvider : ISaveSectionProvider {
    bool Dirty { get; }
    void ClearDirty();
}
```

SaveManager 异步保存 `ManualSaveAsync(forceAll:false)` 时只抓取 Dirty = true 的 Provider。  
实现示例：
```csharp
public class InventoryProvider : MonoBehaviour, IDirtySaveSectionProvider {
    [Serializable] public class InvSection : ISaveSection { public List<string> items = new(); }
    bool _dirty;
    List<string> _items = new();

    public bool Ready => true;
    public bool Dirty => _dirty;

    public ISaveSection Capture() {
        if(!Dirty) return null; // 也可以返回数据让覆盖
        return new InvSection { items = new List<string>(_items) };
    }
    public void Restore(ISaveSection s) {
        _items.Clear();
        if (s is InvSection inv) _items.AddRange(inv.items);
        _dirty = false;
    }
    public void ClearDirty() => _dirty = false;

    public void AddItem(string id){ _items.Add(id); _dirty = true; }

    void OnEnable()=> SaveManager.Instance?.RegisterProvider(this);
    void OnDisable()=> SaveManager.Instance?.UnregisterProvider(this);
}
```

调用：
```csharp
await SaveManager.Instance.ManualSaveAsync(forceAll:false);
// 只保存脏数据；保存后 ClearDirty() 被调用
```

---

## 9. 同步 vs 异步保存

| 方法 | 描述 | 适用 |
|------|------|------|
| ManualSave() | 主线程整合并立即写文件 | 小型存档，简单 |
| ManualSaveAsync(forceAll, ct) | 线程池处理序列化 + 写入，支持脏过滤 | 大型/频繁保存情景 |

注意：
- 异步保存有重入保护（正在保存时再次调用会跳过）。
- 异步保存不阻塞主线程；需要 UI 提示可自定义事件。

---

## 10. 可视化编辑器 SaveEditorWindow

菜单：`Tools / Save System / Save Editor`  
功能：
- 读取磁盘存档（HASH: / DATA: Base64）
- 列出所有 Section (key/type/size/状态标签)
- Raw JSON & 表单模式编辑
- 新增 / 复制 / 删除 / 撤销
- 写回磁盘 / 推送运行时（Play 模式自动调用 SaveManager.ManualLoad + ManualSave）
- 类型扫描 / 搜索
- Dirty 标记自动提示

建议：
- Play 下修改后使用“推送到运行时 & Save”避免游戏逻辑覆盖。

---

## 11. 不可直接序列化数据处理策略

| 场景 | 存档写入 | 恢复方式 |
|------|----------|----------|
| ScriptableObject | 唯一 ID / GUID / 资源名 | 查资源表 (Dictionary) |
| MonoBehaviour 引用 | 自定义 instanceId / 逻辑 ID | 从场景重新查找或实例化 |
| Dictionary | List<KeyValuePairDTO> 或 两个平行 List | 反序列化后重建 |
| 多态集合 | typeId + 数据 | switch(typeId) 构建具体类 |
| 大地图/网格 | 种子 seed / 差异 diff | 重新生成 |
| 临时缓存 | 不保存 | 运行时重建 |
| 循环引用 | 拆分成 ID 关系 | 第二阶段 ResolveRef |

---

## 12. 加密 / 哈希 / 备份

文件格式：
```
HASH:<hashString>
DATA:<base64(encrypted bytes)>
```

流程：
1. Serialize -> json (UTF8 bytes)
2. Encrypt (当前 NoOp)
3. Hash(encrypted)
4. Base64 存 DATA 行
5. 写临时文件 -> 备份旧文件(.bak) -> 替换

修改：
- 启用加密：实现 AES 加密类并替换 SaveService 内加密逻辑。
- 关闭哈希：config.enableHash = false。

---

## 13. 错误处理 & 调试技巧

| 情况 | 日志 | 解决 |
|------|------|------|
| Hash mismatch | “[SaveService] Hash mismatch” | 文件损坏 / 外部修改 → 尝试 .bak |
| 反序列化失败 | “反序列化 Section 失败” | JSON 字段丢失/改名 → 手动迁移或恢复 |
| 类型不匹配 | “Section 类型不一致” | 类重命名 → 临时保留旧类 / 编写迁移脚本 |
| Provider 未 Restore | Ready=false 在延迟阶段 → 等依赖加载后调用 DevForceRestore() |

---

## 14. 版本演进与迁移思路

当前 SaveRootData.version = 1  
升级步骤：
1. 增加字段 → 默认值即可
2. 删除字段 → 旧 json 中多余字段被忽略
3. 重命名字段 / 类型 → 在加载后手动扫描 SectionBlob.json (字符串替换 / 解析旧结构再重组)
4. 扩展：实现 MigrationPipeline，在 SaveManager.InternalLoad 后先对 _composite.sections 做迁移再 Restore

---

## 15. 性能建议

| 需求 | 建议 |
|------|------|
| 频繁小改动保存 | 使用 IDirtySaveSectionProvider + ManualSaveAsync |
| 大量 Section | Key 简短化，避免过长类型名（选择 FullName 替代 AssemblyQualifiedName） |
| 序列化开销 | 避免在 Capture() 内做重量级计算；缓存数据结构 |
| 降低 GC | 避免在 Capture() 临时分配大 List（复用对象） |
| 更快序列化 | 后期切换到更高性能库 (MessagePack / MemoryPack) 保留接口不变 |

---

## 16. 常见问题 FAQ

**Q: 为何新 Provider 在运行时没有恢复数据？**  
A: 可能在 Restore 之后才注册；框架调用 RestoreSingleProvider；若类型或 Key 改动导致找不到匹配 Section。

**Q: 文件显示 HASH 不匹配但 .bak 也坏了？**  
A: 玩家手动修改 / 未完整写入；可回退到默认存档或忽略哈希继续读（修改代码调试）。

**Q: 表单模式下某 Section 显示错误？**  
A: JSON 不合法或字段无法解析（改回 Raw JSON 修复）。

**Q: 能否只保存某几个 Provider？**  
A: 使用脏标记并调用 ManualSaveAsync(forceAll:false)。

**Q: 加密后 SaveEditorWindow 显示乱码？**  
A: 需要在编辑器工具里引入同样的解密逻辑（当前示例未启用加密）。

---

## 17. 示例代码片段集锦

### 17.1 脏标记 Provider (局部保存)

```csharp
public class QuestProvider : MonoBehaviour, IDirtySaveSectionProvider, IExposeSectionType, ICustomSaveSectionKey {
    [Serializable] public class QuestSection : ISaveSection {
        public List<string> completed = new();
    }
    public string Key => "Quests";
    public Type SectionType => typeof(QuestSection);
    public bool Ready => true;
    bool _dirty;
    List<string> _done = new();

    public ISaveSection Capture() {
        if(!_dirty) return null; // 或返回也行
        return new QuestSection { completed = new List<string>(_done) };
    }
    public void Restore(ISaveSection section) {
        _done.Clear();
        if(section is QuestSection qs) _done.AddRange(qs.completed);
        _dirty = false;
    }
    public bool Dirty => _dirty;
    public void ClearDirty() => _dirty = false;
    public void CompleteQuest(string id){ if(!_done.Contains(id)){ _done.Add(id); _dirty = true; } }
    void OnEnable()=> SaveManager.Instance?.RegisterProvider(this);
    void OnDisable()=> SaveManager.Instance?.UnregisterProvider(this);
}
```

### 17.2 异步保存调用

```csharp
// 保存所有脏 Section，界面提示
async void SaveIfDirty() {
    await SaveManager.Instance.ManualSaveAsync(forceAll:false);
}
```

### 17.3 在编辑器窗口写回后强制运行时刷新

```csharp
// SaveEditorWindow 中调用
SaveManager.Instance.ManualLoad(true); // 重新加载 + Restore
SaveManager.Instance.ManualSave();     // 立即写入 (保证一致)
```

### 17.4 自定义序列化（未来切换）

```csharp
public class NewtonsoftSerializer : ISaveSerializer {
    public string Serialize<T>(T obj) => Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    public T Deserialize<T>(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
    public object Deserialize(Type t, string json) => Newtonsoft.Json.JsonConvert.DeserializeObject(json, t);
}
// 在 SetupDependencies() 中替换 new UnityJsonSerializer(...)
```

---

## 18. 下一步扩展建议

| 方向 | 说明 |
|------|------|
| 多存档槽位 | fileName 改为 slot_{index}.dat；SaveEditorWindow 添加下拉槽位 |
| 压缩支持 | Encrypt 前增加 ICompressionProvider (LZ4 / GZip) |
| 迁移系统 | 定义 IMigrationStep + MigrationRunner 支持复杂结构变更 |
| 事件广播 | SaveManager 增加 OnBeforeSave / OnAfterRestore |
| 断点调试友好 | 抽离文件读写接口以便单元测试 |
| 热更兼容 | Section 数量与类型动态注册/剔除 |

---

## 总结

你拥有一个：
- 分段化
- 低耦合
- 支持脏增量与异步
- 内置可视化编辑的
- 可渐进扩展加密 / 迁移 / 压缩
  的存档系统。

保持 Section 纯数据化 + Provider 负责运行时映射，可最小化未来架构变化的迁移成本。

需要更多示例（AES 加密 / 多槽位 / 迁移脚手架）继续提出即可。祝开发顺利！