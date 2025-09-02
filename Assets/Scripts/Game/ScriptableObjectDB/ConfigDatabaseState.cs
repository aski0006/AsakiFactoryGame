using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.ScriptableObjectDB
{
    /// <summary>
    /// 持久化窗口状态的配置资产 (用户上次操作的记忆)。
    /// </summary>
    [CustomConfig]
    public class ConfigDatabaseState : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Editor/ConfigDatabase/ConfigDatabaseState.asset";

        [Header("UI 状态")]
        public int selectedTypeIndex = -1;
        public string searchText = "";
        public bool caseSensitiveSearch = false;
        public bool showInspectorEmbedded = true;
        public bool showPreviewIcons = false;
        public bool autoSelectCreated = true;
        public bool foldTypes = true;
        public bool showLabels = false;

        [Header("批量操作设置")]
        public string newAssetFolder = "Assets/ConfigAssets";
        public string lastRenamePattern = "{name}_Copy";
        public string lastMoveFolder = "Assets/ConfigAssets/Archived";
        public string lastExportFolder = "Assets/ConfigExports";
        public bool confirmBeforeDelete = true;

        // 类型折叠状态
        [SerializeField] private List<string> foldoutTypeNames = new();
        [SerializeField] private List<bool> foldoutTypeValues = new();

        // 每种类型选中的 GUID 集合（序列化为管道分隔字符串）
        [SerializeField] private List<string> typeSelectionTypeNames = new();
        [SerializeField] private List<string> typeSelectionGuids = new();

        private Dictionary<string, bool> _foldoutCache;
        private Dictionary<string, HashSet<string>> _selectionCache;

        public static ConfigDatabaseState LoadOrCreate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ConfigDatabaseState>(DefaultAssetPath);
            if (asset == null)
            {
                var dir = System.IO.Path.GetDirectoryName(DefaultAssetPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir!);
                asset = CreateInstance<ConfigDatabaseState>();
                AssetDatabase.CreateAsset(asset, DefaultAssetPath);
                AssetDatabase.SaveAssets();
            }
            return asset;
        }

        // -------- Foldout --------
        public bool GetFoldout(string typeName, bool defaultValue = true)
        {
            EnsureFoldoutCache();
            if (!_foldoutCache.TryGetValue(typeName, out var v))
            {
                v = defaultValue;
                _foldoutCache[typeName] = v;
                SaveFoldoutCache();
            }
            return v;
        }

        public void SetFoldout(string typeName, bool value)
        {
            EnsureFoldoutCache();
            _foldoutCache[typeName] = value;
            SaveFoldoutCache();
            MarkDirty();
        }

        private void EnsureFoldoutCache()
        {
            if (_foldoutCache != null) return;
            _foldoutCache = new Dictionary<string, bool>();
            for (int i = 0; i < Math.Min(foldoutTypeNames.Count, foldoutTypeValues.Count); i++)
                _foldoutCache[foldoutTypeNames[i]] = foldoutTypeValues[i];
        }

        private void SaveFoldoutCache()
        {
            foldoutTypeNames.Clear();
            foldoutTypeValues.Clear();
            foreach (var kv in _foldoutCache)
            {
                foldoutTypeNames.Add(kv.Key);
                foldoutTypeValues.Add(kv.Value);
            }
        }

        // -------- Selection --------
        public HashSet<string> GetSelection(string typeName)
        {
            EnsureSelectionCache();
            if (!_selectionCache.TryGetValue(typeName, out var set))
            {
                set = new HashSet<string>();
                _selectionCache[typeName] = set;
                SaveSelectionCache();
            }
            return set;
        }

        public void SetSelection(string typeName, HashSet<string> selection)
        {
            EnsureSelectionCache();
            _selectionCache[typeName] = selection;
            SaveSelectionCache();
            MarkDirty();
        }

        private void EnsureSelectionCache()
        {
            if (_selectionCache != null) return;
            _selectionCache = new Dictionary<string, HashSet<string>>();
            for (int i = 0; i < typeSelectionTypeNames.Count; i++)
            {
                var typeName = typeSelectionTypeNames[i];
                var line = i < typeSelectionGuids.Count ? typeSelectionGuids[i] : "";
                var hs = new HashSet<string>();
                if (!string.IsNullOrEmpty(line))
                {
                    foreach (var g in line.Split('|'))
                        if (!string.IsNullOrWhiteSpace(g))
                            hs.Add(g);
                }
                _selectionCache[typeName] = hs;
            }
        }

        private void SaveSelectionCache()
        {
            typeSelectionTypeNames.Clear();
            typeSelectionGuids.Clear();
            foreach (var kv in _selectionCache)
            {
                typeSelectionTypeNames.Add(kv.Key);
                typeSelectionGuids.Add(string.Join("|", kv.Value));
            }
        }

        public void MarkDirty() => EditorUtility.SetDirty(this);
    }
}