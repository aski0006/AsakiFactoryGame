/*
 * SaveEditorWindow
 * 一个可视化的存档(分段)编辑器，用于查看 / 新建 / 修改 / 删除存档中的 Section。
 *
 * 功能:
 *  - 读取磁盘当前存档 (SaveSystemConfig.fileName)
 *  - 从运行时 SaveManager (若存在且已加载) 获取内存版本
 *  - 列出所有 SectionBlob: key / type / json size / 修改标记
 *  - 展开后可在两种模式间切换：
 *      (1) Raw JSON 直接编辑
 *      (2) 简易表单(反射)编辑: 支持可序列化类的 public 字段 (int/float/bool/string/enum/Vector2/3/4/Color/自定义 struct(递归))
 *          以及 List<T> (T 为上述支持的基元或结构)
 *  - 创建新 Section（选择类型 -> 输入 Key -> 默认实例 -> 加入列表）
 *  - 删除 / 复制 Section
 *  - 还原单个 Section 的本地修改
 *  - 导出单 Section JSON 到剪贴板 / 从剪贴板粘贴覆盖
 *  - 保存修改到磁盘 (重写文件) 或 “应用并让 SaveManager 保存” (在 Play 模式下)
 *  - 可只重新加载磁盘版本 (不破坏未保存编辑器内版本) 或完全丢弃并重载
 *  - 显示 meta: version / lastSaveTime / 文件大小 / 路径
 *
 * 注意:
 *  - 修改分段后点击顶部“写回磁盘”才真正落盘。
 *  - 在 Play 模式下若同时运行游戏逻辑，SaveManager 的自动保存可能覆盖你的编辑，建议先暂停自动保存或在修改后立即点击“推送到运行时 & Save”。
 *  - Raw JSON 模式不做严格校验，写错字段名会导致运行时恢复失败。
 *
 * 可扩展点:
 *  - 添加搜索过滤
 *  - 添加多存档槽位支持
 *  - 引入差异对比
 *  - 支持多选批量操作
 */

using Game.Save.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public class SaveEditorWindow : EditorWindow
    {
        #region Internal Data Structures

        private class SectionView
        {
            public SectionBlob blobOriginal;         // 原始磁盘载入版本 (用于比较)
            public SectionBlob blobWorking;          // 正在编辑版本
            public object     runtimeObject;         // 解析的对象 (表单模式)
            public bool       expanded;
            public bool       editRaw = true;
            public bool       runtimeObjectDirty;    // 表单 -> json 尚未同步
            public string     rawJsonCached;         // 当前文本编辑缓存
            public bool       corrupted;             // 解析失败
            public bool       deleted;
            public bool       added;
            public bool       duplicated;
        }

        #endregion

        #region Fields

        private SaveSystemConfig _config;
        private SaveRootData     _diskData;          // 磁盘读取 (原始)
        private List<SectionView> _sections = new();

        private Vector2 _scroll;
        private bool _showMeta = true;
        private bool _showAddNew;
        private bool _showTypeList;

        private string _newSectionKey = "";
        private Type _newSectionType;
        private string _status;
        private double _lastLoadTime;

        private List<Type> _allSectionTypes = new();
        private string _sectionTypeSearch = "";

        // UI Styles
        private GUIStyle _badgeStyle;
        private GUIStyle _jsonTextArea;
        private GUIStyle _foldoutBold;

        private bool _playModeWarned;

        private const string PREF_LAST_CONFIG_GUID = "SAVE_EDITOR_LAST_CFG_GUID";

        #endregion

        #region Menu

        [MenuItem("Tools/Save System/Save Editor")]
        public static void Open()
        {
            var w = GetWindow<SaveEditorWindow>("Save Editor");
            w.Show();
        }

        #endregion

        #region Unity

        private void OnEnable()
        {
            BuildStyles();
            AutoLocateConfig();
            RefreshTypes();
            TryLoadFromDisk();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            _playModeWarned = false;
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (_config == null)
            {
                EditorGUILayout.HelpBox("请先指定 SaveSystemConfig。", MessageType.Warning);
                if (GUILayout.Button("尝试自动查找 Config"))
                {
                    AutoLocateConfig();
                }
                return;
            }

            DrawMeta();
            DrawSectionsList();
            DrawAddNewSection();
            DrawTypesPanel();
            DrawStatusBar();
        }

        #endregion

        #region Toolbar

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _config = (SaveSystemConfig)EditorGUILayout.ObjectField(_config, typeof(SaveSystemConfig), false, GUILayout.Width(250));

                if (GUILayout.Button("重新载入磁盘", EditorStyles.toolbarButton))
                    TryLoadFromDisk();

                if (GUILayout.Button("丢弃修改并重载", EditorStyles.toolbarButton))
                {
                    if (EditorUtility.DisplayDialog("确认", "将丢弃所有未保存修改，继续？", "是", "否"))
                        TryLoadFromDisk(hardReload: true);
                }

                EditorGUI.BeginDisabledGroup(!HasDirty());
                if (GUILayout.Button("写回磁盘", EditorStyles.toolbarButton))
                {
                    SaveToDisk();
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("推送到运行时 && Save", EditorStyles.toolbarButton))
                {
                    PushToRuntimeAndSave();
                }

                if (GUILayout.Button("刷新类型缓存", EditorStyles.toolbarButton))
                    RefreshTypes();

                GUILayout.FlexibleSpace();

                _showMeta = GUILayout.Toggle(_showMeta, "Meta", EditorStyles.toolbarButton);
                _showAddNew = GUILayout.Toggle(_showAddNew, "新增", EditorStyles.toolbarButton);
                _showTypeList = GUILayout.Toggle(_showTypeList, "类型", EditorStyles.toolbarButton);
            }
        }

        #endregion

        #region Meta

        private void DrawMeta()
        {
            if (!_showMeta) return;
            if (_diskData == null)
            {
                EditorGUILayout.HelpBox("当前无磁盘存档。点击 写回磁盘 可创建。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUILayout.Label("存档 Meta 信息", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("版本", _diskData.version.ToString());
                EditorGUILayout.LabelField("最后保存(Unix)", _diskData.lastSaveUnix.ToString());
                DateTime t = DateTimeOffset.FromUnixTimeSeconds(_diskData.lastSaveUnix).LocalDateTime;
                EditorGUILayout.LabelField("本地时间", _diskData.lastSaveUnix == 0 ? "-" : t.ToString("yyyy-MM-dd HH:mm:ss"));
                string path = Path.Combine(Application.persistentDataPath, _config.fileName);
                EditorGUILayout.LabelField("路径", path);
                if (File.Exists(path))
                {
                    long len = new FileInfo(path).Length;
                    EditorGUILayout.LabelField("文件大小", FormatSize(len));
                    if (GUILayout.Button("打开存档目录"))
                        EditorUtility.RevealInFinder(Application.persistentDataPath);
                }

                if (Application.isPlaying && !_playModeWarned)
                {
                    EditorGUILayout.HelpBox("Play 模式下请注意：游戏逻辑可能自动保存覆盖你的修改。", MessageType.Warning);
                }
            }
        }

        #endregion

        #region Sections List

        private void DrawSectionsList()
        {
            GUILayout.Space(4);
            GUILayout.Label($"Sections ({_sections.Count})", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _sections.Count; i++)
            {
                var sv = _sections[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    DrawSectionHeader(sv, i);
                    if (sv.expanded)
                        DrawSectionBody(sv);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSectionHeader(SectionView sv, int index)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                sv.expanded = EditorGUILayout.Foldout(sv.expanded, $"{sv.blobWorking.key}", true, _foldoutBold);

                GUILayout.Label(ShortTypeName(sv.blobWorking.type), GUILayout.Width(140));

                if (sv.added) Badge("New", Color.green);
                if (sv.duplicated) Badge("Dup", new Color(0.3f, 0.7f, 1f));
                if (sv.deleted) Badge("Del", Color.red);
                else if (sv.corrupted) Badge("Err", Color.red);
                else if (IsModified(sv)) Badge("Mod", new Color(1f, 0.6f, 0));

                GUILayout.Label(ByteLen(sv.blobWorking.json), GUILayout.Width(70));

                if (GUILayout.Button("复制 JSON", GUILayout.Width(70)))
                {
                    EditorGUIUtility.systemCopyBuffer = sv.blobWorking.json;
                    ShowNotification(new GUIContent("已复制"));
                }
                if (GUILayout.Button("粘贴覆盖", GUILayout.Width(70)))
                {
                    string text = EditorGUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(text))
                    {
                        Undo.RecordObject(this, "Paste Section JSON");
                        sv.blobWorking.json = text;
                        sv.rawJsonCached = text;
                        sv.runtimeObject = null;
                        sv.runtimeObjectDirty = false;
                        sv.corrupted = !TryParseRuntimeObject(sv);
                    }
                }

                if (!sv.deleted)
                {
                    if (GUILayout.Button("删除", GUILayout.Width(50)))
                    {
                        if (EditorUtility.DisplayDialog("确认", $"删除 Section '{sv.blobWorking.key}' ?", "是", "否"))
                        {
                            sv.deleted = true;
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("撤销删除", GUILayout.Width(70)))
                        sv.deleted = false;
                }

                if (GUILayout.Button("还原", GUILayout.Width(50)))
                {
                    if (sv.blobOriginal != null)
                    {
                        sv.blobWorking.json = sv.blobOriginal.json;
                        sv.blobWorking.type = sv.blobOriginal.type;
                        sv.blobWorking.key = sv.blobOriginal.key;
                        sv.rawJsonCached = sv.blobWorking.json;
                        sv.runtimeObject = null;
                        sv.runtimeObjectDirty = false;
                        sv.deleted = false;
                        sv.added = false;
                        sv.duplicated = false;
                        sv.corrupted = !TryParseRuntimeObject(sv);
                    }
                    else
                    {
                        // 新增的 => 直接标记删除
                        sv.deleted = true;
                    }
                }

                if (GUILayout.Button("复制", GUILayout.Width(50)))
                {
                    DuplicateSection(index);
                }
            }
        }

        private void DrawSectionBody(SectionView sv)
        {
            EditorGUI.indentLevel++;
            // 模式切换
            using (new EditorGUILayout.HorizontalScope())
            {
                bool newEditRaw = GUILayout.Toggle(sv.editRaw, "Raw JSON", "Button");
                bool newForm = GUILayout.Toggle(!sv.editRaw, "表单模式", "Button");
                if (newEditRaw != sv.editRaw)
                {
                    sv.editRaw = newEditRaw;
                }
                else if (newForm == true && sv.editRaw)
                {
                    sv.editRaw = false;
                    // 切换到表单模式时需要解析对象
                    if (sv.runtimeObject == null)
                        sv.corrupted = !TryParseRuntimeObject(sv);
                }

                GUILayout.FlexibleSpace();
                if (!sv.editRaw && sv.runtimeObject != null && GUILayout.Button("同步到 JSON", GUILayout.Width(100)))
                {
                    SerializeRuntimeObjectToBlob(sv);
                }
            }

            if (sv.editRaw)
            {
                DrawRawJsonEditor(sv);
            }
            else
            {
                if (sv.runtimeObject == null)
                {
                    EditorGUILayout.HelpBox("无法解析为对象。请切换到 Raw JSON 修复。", MessageType.Error);
                }
                else
                {
                    sv.runtimeObjectDirty |= DrawObjectFormRecursive(sv.runtimeObject, sv.runtimeObject.GetType(), sv.blobWorking.key);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawRawJsonEditor(SectionView sv)
        {
            if (sv.rawJsonCached == null) sv.rawJsonCached = sv.blobWorking.json;
            EditorGUI.BeginChangeCheck();
            sv.rawJsonCached = EditorGUILayout.TextArea(sv.rawJsonCached, _jsonTextArea, GUILayout.MinHeight(80));
            if (EditorGUI.EndChangeCheck())
            {
                sv.blobWorking.json = sv.rawJsonCached;
                sv.corrupted = !TryParseRuntimeObject(sv, silent: true);
            }
        }

        #endregion

        #region Add New Section

        private void DrawAddNewSection()
        {
            if (!_showAddNew) return;
            GUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUILayout.Label("新增 Section", EditorStyles.boldLabel);
                _newSectionKey = EditorGUILayout.TextField("Key", _newSectionKey);

                // 选择类型
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("类型", _newSectionType != null ? _newSectionType.FullName : "<选择>");
                if (GUILayout.Button("选择类型", GUILayout.Width(90)))
                {
                    _showTypeList = true;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_newSectionKey) || _newSectionType == null);
                if (GUILayout.Button("创建并插入"))
                {
                    CreateNewSection();
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("重置输入"))
                {
                    _newSectionKey = "";
                    _newSectionType = null;
                }
            }
        }

        #endregion

        #region Types Panel

        private void DrawTypesPanel()
        {
            if (!_showTypeList) return;
            GUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUILayout.Label("可用 Section 类型", EditorStyles.boldLabel);
                _sectionTypeSearch = EditorGUILayout.TextField("搜索", _sectionTypeSearch);
                var filtered = string.IsNullOrWhiteSpace(_sectionTypeSearch)
                    ? _allSectionTypes
                    : _allSectionTypes.Where(t => t.FullName.IndexOf(_sectionTypeSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                int count = 0;
                foreach (var t in filtered)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(t.Name, GUILayout.Width(180)))
                        {
                            _newSectionType = t;
                            _showTypeList = false;
                        }
                        GUILayout.Label(t.FullName, EditorStyles.miniLabel);
                    }
                    count++;
                }
                if (count == 0) GUILayout.Label("无匹配类型。");
            }
        }

        #endregion

        #region Object Form Drawing (Reflection)

        private bool DrawObjectFormRecursive(object obj, Type type, string labelPrefix)
        {
            bool dirty = false;
            if (obj == null) return false;

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fields)
            {
                var fType = f.FieldType;
                object value = f.GetValue(obj);
                string fieldLabel = ObjectNames.NicifyVariableName(f.Name);

                EditorGUI.BeginChangeCheck();
                object newValue = value;

                if (fType == typeof(int))
                    newValue = EditorGUILayout.IntField(fieldLabel, (int)(value ?? 0));
                else if (fType == typeof(float))
                    newValue = EditorGUILayout.FloatField(fieldLabel, (float)(value ?? 0f));
                else if (fType == typeof(double))
                    newValue = EditorGUILayout.DoubleField(fieldLabel, (double)(value ?? 0d));
                else if (fType == typeof(bool))
                    newValue = EditorGUILayout.Toggle(fieldLabel, (bool)(value ?? false));
                else if (fType == typeof(string))
                    newValue = EditorGUILayout.TextField(fieldLabel, (string)(value ?? ""));
                else if (fType.IsEnum)
                    newValue = EditorGUILayout.EnumPopup(fieldLabel, (Enum)(value ?? Activator.CreateInstance(fType)));
                else if (fType == typeof(Vector2))
                    newValue = EditorGUILayout.Vector2Field(fieldLabel, (Vector2)(value ?? Vector2.zero));
                else if (fType == typeof(Vector3))
                    newValue = EditorGUILayout.Vector3Field(fieldLabel, (Vector3)(value ?? Vector3.zero));
                else if (fType == typeof(Vector4))
                    newValue = EditorGUILayout.Vector4Field(fieldLabel, (Vector4)(value ?? Vector4.zero));
                else if (fType == typeof(Color))
                    newValue = EditorGUILayout.ColorField(fieldLabel, (Color)(value ?? Color.white));
                else if (IsGenericList(fType))
                {
                    newValue = DrawListEditor(fieldLabel, fType, value);
                }
                else if (IsSimpleStruct(fType))
                {
                    // 递归绘制 struct
                    if (value == null) value = Activator.CreateInstance(fType);
                    EditorGUILayout.LabelField(fieldLabel, EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    var boxed = value;
                    if (DrawObjectFormRecursive(boxed, fType, labelPrefix + "/" + f.Name))
                    {
                        newValue = boxed;
                    }
                    EditorGUI.indentLevel--;
                }
                else
                {
                    // 复杂引用类型递归
                    if (value == null) value = Activator.CreateInstance(fType);
                    EditorGUILayout.LabelField(fieldLabel, EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    if (DrawObjectFormRecursive(value, fType, labelPrefix + "/" + f.Name))
                        newValue = value;
                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    f.SetValue(obj, newValue);
                    dirty = true;
                }
            }

            return dirty;
        }

        private object DrawListEditor(string label, Type listType, object listInstance)
        {
            var elemType = listType.GetGenericArguments()[0];
            if (listInstance == null)
                listInstance = Activator.CreateInstance(listType);

            var list = (System.Collections.IList)listInstance;
            using (new EditorGUILayout.VerticalScope("helpbox"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{label} (List<{elemType.Name}>)  Count={list.Count}", EditorStyles.boldLabel);
                    if (GUILayout.Button("+", GUILayout.Width(24)))
                    {
                        list.Add(CreateDefaultValue(elemType));
                    }
                }

                int removeIndex = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    object val = list[i];
                    EditorGUI.BeginChangeCheck();
                    object newVal = DrawElementField($"[{i}]", elemType, val);
                    if (EditorGUI.EndChangeCheck())
                    {
                        list[i] = newVal;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("X", GUILayout.Width(20)))
                            removeIndex = i;
                    }
                    GUILayout.Space(2);
                }

                if (removeIndex >= 0 && removeIndex < list.Count)
                    list.RemoveAt(removeIndex);
            }
            return list;
        }

        private object DrawElementField(string label, Type elemType, object val)
        {
            if (elemType == typeof(int)) return EditorGUILayout.IntField(label, (int)(val ?? 0));
            if (elemType == typeof(float)) return EditorGUILayout.FloatField(label, (float)(val ?? 0f));
            if (elemType == typeof(double)) return EditorGUILayout.DoubleField(label, (double)(val ?? 0d));
            if (elemType == typeof(bool)) return EditorGUILayout.Toggle(label, (bool)(val ?? false));
            if (elemType == typeof(string)) return EditorGUILayout.TextField(label, (string)(val ?? ""));
            if (elemType.IsEnum) return EditorGUILayout.EnumPopup(label, (Enum)(val ?? Activator.CreateInstance(elemType)));
            if (elemType == typeof(Vector2)) return EditorGUILayout.Vector2Field(label, (Vector2)(val ?? Vector2.zero));
            if (elemType == typeof(Vector3)) return EditorGUILayout.Vector3Field(label, (Vector3)(val ?? Vector3.zero));
            if (elemType == typeof(Vector4)) return (Vector4)EditorGUILayout.Vector4Field(label, (Vector4)(val ?? Vector4.zero));
            if (elemType == typeof(Color)) return EditorGUILayout.ColorField(label, (Color)(val ?? Color.white));

            // 复杂元素：递归
            if (val == null) val = Activator.CreateInstance(elemType);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            DrawObjectFormRecursive(val, elemType, label);
            EditorGUI.indentLevel--;
            return val;
        }

        private static bool IsGenericList(Type t)
            => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

        private static bool IsSimpleStruct(Type t)
            => t.IsValueType && !t.IsPrimitive && !t.IsEnum && t != typeof(decimal) &&
               t.Namespace != null && !t.Namespace.StartsWith("System");

        private static object CreateDefaultValue(Type t)
        {
            if (t.IsValueType) return Activator.CreateInstance(t);
            if (t == typeof(string)) return "";
            return Activator.CreateInstance(t);
        }

        #endregion

        #region Logic: Load / Save / Dirty

        private void TryLoadFromDisk(bool hardReload = false)
        {
            if (_config == null)
            {
                _status = "未找到配置";
                return;
            }

            string path = Path.Combine(Application.persistentDataPath, _config.fileName);
            SaveRootData data = null;
            if (File.Exists(path))
            {
                try
                {
                    // 解析现有文件
                    var lines = File.ReadAllLines(path);
                    if (lines.Length >= 2 && lines[0].StartsWith("HASH:") && lines[1].StartsWith("DATA:"))
                    {
                        var base64 = lines[1].Substring(5);
                        var bytes = Convert.FromBase64String(base64);
                        var json = Encoding.UTF8.GetString(bytes); // 未加密 / hash 之前: 这里因你的实现，若有加密需解密
                        data = JsonUtility.FromJson<SaveRootData>(json);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[SaveEditor] 读取存档失败: " + e);
                }
            }
            if (data == null)
            {
                data = new SaveRootData();
            }

            _diskData = data;
            _sections.Clear();
            if (_diskData.sections != null)
            {
                foreach (var b in _diskData.sections)
                {
                    var sv = new SectionView
                    {
                        blobOriginal = CloneBlob(b),
                        blobWorking = CloneBlob(b),
                        rawJsonCached = b.json,
                        expanded = false
                    };
                    sv.corrupted = !TryParseRuntimeObject(sv, silent: true);
                    _sections.Add(sv);
                }
            }

            _lastLoadTime = EditorApplication.timeSinceStartup;
            _status = $"已加载磁盘存档: {_sections.Count} sections";
            Repaint();
        }

        private void SaveToDisk()
        {
            // 将 runtimeObjectDirty 同步
            foreach (var sv in _sections)
            {
                if (sv.deleted) continue;
                if (!sv.editRaw && sv.runtimeObjectDirty)
                {
                    SerializeRuntimeObjectToBlob(sv);
                }
            }

            var final = new SaveRootData
            {
                version = SaveRootData.CurrentVersion,
                lastSaveUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                sections = new List<SectionBlob>()
            };

            foreach (var sv in _sections)
            {
                if (sv.deleted) continue;
                final.sections.Add(CloneBlob(sv.blobWorking));
            }

            // 写文件 (复用 SaveService 的格式: HASH + DATA)
            string json = JsonUtility.ToJson(final, _config.prettyJson);
            byte[] plain = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = plain; // 若需要: 在此接入 _config.enableEncryption
            string hash = _config.enableHash
                ? Sha256HashProvider.Instance.ComputeHash(encrypted)
                : "NO_HASH";
            string base64 = Convert.ToBase64String(encrypted);

            string path = Path.Combine(Application.persistentDataPath, _config.fileName);
            string temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var sw = new StreamWriter(temp, false, Encoding.UTF8))
            {
                sw.WriteLine("HASH:" + hash);
                sw.WriteLine("DATA:" + base64);
            }
            if (File.Exists(path))
            {
                if (_config.useBackup)
                {
                    try { File.Copy(path, path + ".bak", true); } catch { }
                }
                File.Delete(path);
            }
            File.Move(temp, path);

            // 更新原始数据(重建)
            TryLoadFromDisk();
            _status = "已写入磁盘";
        }

        private void PushToRuntimeAndSave()
        {
            if (!Application.isPlaying)
            {
                if (EditorUtility.DisplayDialog("提示", "当前不是 Play 模式，只会写回磁盘。继续？", "写回", "取消"))
                {
                    SaveToDisk();
                }
                return;
            }

            var sm = UnityEngine.Object.FindFirstObjectByType<SaveManager>();
            if (sm == null)
            {
                if (EditorUtility.DisplayDialog("提示", "运行中未找到 SaveManager。仅写回磁盘？", "写回", "取消"))
                    SaveToDisk();
                return;
            }

            if (EditorUtility.DisplayDialog("确认", "写回磁盘并让运行时重新加载 + 保存？", "执行", "取消"))
            {
                SaveToDisk();
                sm.ManualLoad(true);   // 重新加载并 Restore
                sm.ManualSave();       // 再次立刻保存 (触发内部 Service)
                _status = "已推送到运行时并保存";
            }
        }

        private bool HasDirty()
        {
            foreach (var sv in _sections)
            {
                if (sv.deleted || sv.added || sv.duplicated || IsModified(sv) || sv.runtimeObjectDirty)
                    return true;
            }
            return false;
        }

        private bool IsModified(SectionView sv)
        {
            if (sv.blobOriginal == null) return sv.added || sv.duplicated;
            if (sv.deleted) return true;
            if (sv.blobOriginal.key != sv.blobWorking.key) return true;
            if (sv.blobOriginal.type != sv.blobWorking.type) return true;
            if (!string.Equals(sv.blobOriginal.json, sv.blobWorking.json, StringComparison.Ordinal))
                return true;
            return false;
        }

        #endregion

        #region Create / Duplicate

        private void CreateNewSection()
        {
            if (_newSectionType == null || string.IsNullOrWhiteSpace(_newSectionKey)) return;
            // 检查 key 冲突
            if (_sections.Any(s => s.blobWorking.key == _newSectionKey && !s.deleted))
            {
                if (!EditorUtility.DisplayDialog("Key 冲突", "该 Key 已存在，仍然创建？", "是", "否"))
                    return;
            }

            object inst;
            try
            {
                inst = Activator.CreateInstance(_newSectionType);
            }
            catch
            {
                inst = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(_newSectionType);
            }

            string json = JsonUtility.ToJson(inst, _config.prettyJson);

            var blob = new SectionBlob
            {
                key = _newSectionKey,
                type = _config.storeTypeAssemblyQualified ? _newSectionType.AssemblyQualifiedName : _newSectionType.FullName,
                json = json
            };

            var sv = new SectionView
            {
                blobOriginal = null,
                blobWorking = blob,
                added = true,
                rawJsonCached = json,
                runtimeObject = inst,
                expanded = true,
                editRaw = false
            };
            _sections.Add(sv);
            _newSectionKey = "";
            _newSectionType = null;
            _status = "已新增 Section (未保存)";
        }

        private void DuplicateSection(int index)
        {
            if (index < 0 || index >= _sections.Count) return;
            var src = _sections[index];
            var copy = CloneBlob(src.blobWorking);
            copy.key = UniqueKey(copy.key);
            var sv = new SectionView
            {
                blobOriginal = null,
                blobWorking = copy,
                duplicated = true,
                rawJsonCached = copy.json,
                expanded = true
            };
            sv.corrupted = !TryParseRuntimeObject(sv, silent: true);
            _sections.Add(sv);
            _status = "已复制 (未保存)";
        }

        private string UniqueKey(string baseKey)
        {
            string k = baseKey;
            int i = 1;
            while (_sections.Any(s => s.blobWorking.key == k && !s.deleted))
            {
                k = baseKey + "_" + i;
                i++;
            }
            return k;
        }

        #endregion

        #region Parse / Serialize RuntimeObject

        private bool TryParseRuntimeObject(SectionView sv, bool silent = false)
        {
            sv.runtimeObject = null;
            if (string.IsNullOrEmpty(sv.blobWorking.type) || string.IsNullOrEmpty(sv.blobWorking.json))
                return false;

            var type = ResolveSectionType(sv.blobWorking.type);
            if (type == null)
            {
                if (!silent) Debug.LogWarning($"[SaveEditor] 无法解析类型: {sv.blobWorking.type}");
                return false;
            }
            try
            {
                sv.runtimeObject = JsonUtility.FromJson(sv.blobWorking.json, type);
                sv.runtimeObjectDirty = false;
                return true;
            }
            catch (Exception e)
            {
                if (!silent) Debug.LogWarning($"[SaveEditor] JSON -> 对象失败: {sv.blobWorking.key} {e.Message}");
                return false;
            }
        }

        private void SerializeRuntimeObjectToBlob(SectionView sv)
        {
            if (sv.runtimeObject == null) return;
            try
            {
                string json = JsonUtility.ToJson(sv.runtimeObject, _config.prettyJson);
                sv.blobWorking.json = json;
                sv.rawJsonCached = json;
                sv.runtimeObjectDirty = false;
                sv.corrupted = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveEditor] 对象 -> JSON 失败: {e}");
            }
        }

        #endregion

        #region Helpers

        private void BuildStyles()
        {
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 0, 0),
                fontSize = 10
            };
            _jsonTextArea = new GUIStyle(EditorStyles.textArea)
            {
                font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font,
                fontSize = 11
            };
            _foldoutBold = new GUIStyle(EditorStyles.foldout);
            _foldoutBold.fontStyle = FontStyle.Bold;
        }

        private void Badge(string text, Color c)
        {
            var col = GUI.color;
            GUI.color = c;
            GUILayout.Label(text, _badgeStyle, GUILayout.Width(34));
            GUI.color = col;
        }

        private string ByteLen(string json)
        {
            if (json == null) return "0B";
            return FormatSize(Encoding.UTF8.GetByteCount(json));
        }

        private string ShortTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "<null>";
            int idx = typeName.LastIndexOf('.');
            if (idx >= 0 && idx < typeName.Length - 1)
                return typeName.Substring(idx + 1);
            return typeName;
        }

        private static SectionBlob CloneBlob(SectionBlob b)
        {
            return new SectionBlob
            {
                key = b.key,
                type = b.type,
                json = b.json
            };
        }

        private void RefreshTypes()
        {
            _allSectionTypes.Clear();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(ISaveSection).IsAssignableFrom(t)) continue;
                    // 无公共无参构造也可以，后续用反序列化填充
                    _allSectionTypes.Add(t);
                }
            }
            _allSectionTypes = _allSectionTypes.Distinct().OrderBy(t => t.Name).ToList();
        }

        private Type ResolveSectionType(string storedTypeName)
        {
            if (string.IsNullOrEmpty(storedTypeName)) return null;
            var t = Type.GetType(storedTypeName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(storedTypeName);
                if (t != null) return t;
            }
            return null;
        }

        private void AutoLocateConfig()
        {
            if (_config != null) return;
            string[] guids = AssetDatabase.FindAssets("t:SaveSystemConfig");
            if (guids.Length > 0)
            {
                string guid = EditorPrefs.GetString(PREF_LAST_CONFIG_GUID, guids[0]);
                if (!guids.Contains(guid))
                    guid = guids[0];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                _config = AssetDatabase.LoadAssetAtPath<SaveSystemConfig>(path);
                EditorPrefs.SetString(PREF_LAST_CONFIG_GUID, guid);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }

        private void DrawStatusBar()
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                GUILayout.Label(_status ?? "就绪", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (HasDirty())
                {
                    GUILayout.Label("有未保存修改", EditorStyles.miniBoldLabel);
                }
            }
        }

        #endregion
    }
}