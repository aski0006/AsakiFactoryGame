using Game.ScriptableObjectDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor.ConfigDatabase
{
    /// <summary>
    /// 集中管理数据库窗口：
    /// - 仅显示带 [CustomConfig] 的 ScriptableObject 类型
    /// - 列表+搜索+多选 (复选框)
    /// - 批量操作：创建 / 删除 / 复制 / 移动 / 重命名模式 / 导出 JSON / 添加或移除标签
    /// - 内嵌 Inspector (单选或批量逐个)
    /// - 状态持久化 (ConfigDatabaseState)
    /// </summary>
    public class ConfigDatabaseWindow : EditorWindow
    {
        private ConfigDatabaseState _state;
        private Vector2 _typeScroll;
        private Vector2 _assetScroll;
        private Vector2 _inspectorScroll;

        private List<ConfigTypeCache.TypeInfo> _types = new();
        private List<UnityEngine.Object> _currentAssets = new();
        private List<string> _currentGuids = new();

        private string _status;
        private double _lastOpTime;

        private GUIStyle _badgeStyle;
        private bool _needRefreshList;

        [MenuItem("Tools/Config DB/Config Database")]
        public static void Open()
        {
            GetWindow<ConfigDatabaseWindow>("Config Database").Show();
        }

        private void OnEnable()
        {
            _state = ConfigDatabaseState.LoadOrCreate();
            _types = ConfigTypeCache.GetTypes(true).ToList();
            if (_state.selectedTypeIndex >= _types.Count) _state.selectedTypeIndex = _types.Count - 1;
            BuildStyles();
            RefreshAssetList();
        }

        private void OnFocus()
        {
            if (_needRefreshList)
            {
                RefreshAssetList();
                _needRefreshList = false;
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTypePanel();
                DrawAssetListPanel();
                if (_state.showInspectorEmbedded)
                    DrawInspectorPanel();
            }

            DrawStatusBar();
        }

        #region Toolbar

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新类型", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    _types = ConfigTypeCache.GetTypes(true).ToList();
                    if (_state.selectedTypeIndex >= _types.Count)
                        _state.selectedTypeIndex = _types.Count - 1;
                    RefreshAssetList();
                }

                if (GUILayout.Button("刷新当前类型资源", EditorStyles.toolbarButton, GUILayout.Width(120)))
                {
                    RefreshAssetList();
                }

                GUILayout.Space(8);
                _state.searchText = GUILayout.TextField(_state.searchText, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(160));
                if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    _state.searchText = "";
                    RefreshAssetList();
                }

                _state.caseSensitiveSearch = GUILayout.Toggle(_state.caseSensitiveSearch, "Aa", EditorStyles.toolbarButton, GUILayout.Width(28));
                _state.showInspectorEmbedded = GUILayout.Toggle(_state.showInspectorEmbedded, "内嵌Inspector", EditorStyles.toolbarButton);
                _state.showPreviewIcons = GUILayout.Toggle(_state.showPreviewIcons, "预览", EditorStyles.toolbarButton);
                _state.showLabels = GUILayout.Toggle(_state.showLabels, "标签", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("批量操作", EditorStyles.toolbarDropDown, GUILayout.Width(90)))
                {
                    ShowBatchMenu();
                }
            }
        }

        #endregion

        #region Type Panel

        private void DrawTypePanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
            {
                EditorGUILayout.LabelField("配置类型", EditorStyles.boldLabel);
                _typeScroll = EditorGUILayout.BeginScrollView(_typeScroll, "box");

                for (int i = 0; i < _types.Count; i++)
                {
                    var t = _types[i];
                    bool selected = (i == _state.selectedTypeIndex);
                    using (new EditorGUILayout.HorizontalScope(selected ? "OL SelectedRow" : "OL Box"))
                    {
                        if (GUILayout.Toggle(selected, t.displayName, "Label"))
                        {
                            if (!selected)
                            {
                                _state.selectedTypeIndex = i;
                                RefreshAssetList();
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField("选项", EditorStyles.miniBoldLabel);
                    _state.newAssetFolder = EditorGUILayout.TextField("创建目录", _state.newAssetFolder);
                    _state.lastRenamePattern = EditorGUILayout.TextField("重命名模板", _state.lastRenamePattern);
                    _state.lastMoveFolder = EditorGUILayout.TextField("移动目标", _state.lastMoveFolder);
                    _state.lastExportFolder = EditorGUILayout.TextField("导出目录", _state.lastExportFolder);
                    _state.autoSelectCreated = EditorGUILayout.Toggle("新建后自动选中", _state.autoSelectCreated);
                    _state.confirmBeforeDelete = EditorGUILayout.Toggle("删除前确认", _state.confirmBeforeDelete);
                }
            }
        }

        #endregion

        #region Asset List Panel

        private void DrawAssetListPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.42f)))
            {
                EditorGUILayout.LabelField("资源列表", EditorStyles.boldLabel);
                DrawAssetListToolbar();
                _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll, "box");
                if (_state.selectedTypeIndex < 0 || _state.selectedTypeIndex >= _types.Count)
                {
                    EditorGUILayout.HelpBox("选择一个类型以展示资源。", MessageType.Info);
                }
                else
                {
                    var typeInfo = _types[_state.selectedTypeIndex];
                    var selectionSet = _state.GetSelection(typeInfo.fullName);
                    for (int i = 0; i < _currentAssets.Count; i++)
                    {
                        var obj = _currentAssets[i];
                        var guid = _currentGuids[i];
                        if (obj == null) continue;

                        bool selected = selectionSet.Contains(guid);
                        using (new EditorGUILayout.HorizontalScope(selected ? "SelectionRect" : GUIStyle.none))
                        {
                            bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(18));
                            if (newSelected != selected)
                            {
                                if (Event.current.shift && selectionSet.Count > 0)
                                {
                                    // Shift 范围选择
                                    int lastIndex = FindLastSelectedIndex(selectionSet);
                                    if (lastIndex >= 0)
                                    {
                                        int from = Mathf.Min(lastIndex, i);
                                        int to = Mathf.Max(lastIndex, i);
                                        for (int j = from; j <= to; j++)
                                            selectionSet.Add(_currentGuids[j]);
                                    }
                                    else selectionSet.Add(guid);
                                }
                                else if (Event.current.control || Event.current.command)
                                {
                                    if (newSelected) selectionSet.Add(guid);
                                    else selectionSet.Remove(guid);
                                }
                                else
                                {
                                    selectionSet.Clear();
                                    if (newSelected) selectionSet.Add(guid);
                                }
                                _state.SetSelection(typeInfo.fullName, selectionSet);
                            }

                            if (GUILayout.Button(obj.name, GUI.skin.label, GUILayout.ExpandWidth(true)))
                            {
                                // 单击行为：若无 ctrl 则单选
                                if (!(Event.current.control || Event.current.command))
                                {
                                    selectionSet.Clear();
                                }
                                selectionSet.Add(guid);
                                _state.SetSelection(typeInfo.fullName, selectionSet);
                                EditorGUIUtility.PingObject(obj);
                            }

                            if (_state.showPreviewIcons)
                            {
                                var tex = AssetPreview.GetMiniThumbnail(obj);
                                GUILayout.Label(tex, GUILayout.Width(20), GUILayout.Height(20));
                            }

                            if (_state.showLabels)
                            {
                                var path = AssetDatabase.GUIDToAssetPath(guid);
                                var labels = AssetDatabase.GetLabels(obj);
                                GUILayout.Label(string.Join(",", labels), EditorStyles.miniLabel, GUILayout.Width(120));
                            }

                            if (GUILayout.Button("E", GUILayout.Width(22)))
                            {
                                Selection.activeObject = obj;
                            }

                            if (GUILayout.Button("X", GUILayout.Width(22)))
                            {
                                if (!_state.confirmBeforeDelete ||
                                    EditorUtility.DisplayDialog("删除确认", $"删除 {obj.name} ?", "确定", "取消"))
                                {
                                    var path = AssetDatabase.GUIDToAssetPath(guid);
                                    AssetDatabase.DeleteAsset(path);
                                    _needRefreshList = true;
                                }
                            }
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAssetListToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新建", GUILayout.Width(50)))
                {
                    CreateNewAsset();
                }
                if (GUILayout.Button("全选", GUILayout.Width(45)))
                {
                    MultiSelectAll();
                }
                if (GUILayout.Button("清空", GUILayout.Width(45)))
                {
                    MultiSelectClear();
                }
                if (GUILayout.Button("反选", GUILayout.Width(45)))
                {
                    MultiSelectInvert();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"共 {_currentAssets.Count} 个");
            }
        }

        private int FindLastSelectedIndex(HashSet<string> set)
        {
            for (int i = _currentGuids.Count - 1; i >= 0; i--)
                if (set.Contains(_currentGuids[i]))
                    return i;
            return -1;
        }

        #endregion

        #region Inspector Panel

        private void DrawInspectorPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField("预览 / 编辑", EditorStyles.boldLabel);
                _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll, "box");
                if (_state.selectedTypeIndex < 0 || _state.selectedTypeIndex >= _types.Count)
                {
                    EditorGUILayout.HelpBox("无类型。", MessageType.Info);
                }
                else
                {
                    var typeInfo = _types[_state.selectedTypeIndex];
                    var selectionSet = _state.GetSelection(typeInfo.fullName);

                    if (selectionSet.Count == 0)
                    {
                        EditorGUILayout.HelpBox("未选中资源。", MessageType.Info);
                    }
                    else if (selectionSet.Count == 1)
                    {
                        var guid = selectionSet.First();
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        if (obj)
                        {
                            var so = new SerializedObject(obj);
                            so.Update();
                            var prop = so.GetIterator();
                            bool enter = true;
                            while (prop.NextVisible(enter))
                            {
                                enter = false;
                                EditorGUILayout.PropertyField(prop, true);
                            }
                            so.ApplyModifiedProperties();
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"已选择 {selectionSet.Count} 个。批量编辑（逐个显示）", MessageType.None);
                        foreach (var guid in selectionSet.Take(10))
                        {
                            var path = AssetDatabase.GUIDToAssetPath(guid);
                            var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                            if (!obj) continue;
                            EditorGUILayout.LabelField(obj.name, EditorStyles.miniBoldLabel);
                            var so = new SerializedObject(obj);
                            so.Update();
                            var it = so.GetIterator();
                            bool enterChildren = true;
                            int shown = 0;
                            while (it.NextVisible(enterChildren))
                            {
                                enterChildren = false;
                                if (it.name == "m_Script") continue;
                                EditorGUILayout.PropertyField(it, true);
                                shown++;
                                if (shown > 10)
                                {
                                    EditorGUILayout.HelpBox("已限制属性显示 (演示可扩展分页)。", MessageType.None);
                                    break;
                                }
                            }
                            so.ApplyModifiedProperties();
                            EditorGUILayout.Space();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        #endregion

        #region Batch Menu

        private void ShowBatchMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("复制选中"), false, () => BatchDuplicate());
            menu.AddItem(new GUIContent("删除选中"), false, () => BatchDelete());
            menu.AddItem(new GUIContent("移动到目录"), false, () => BatchMove());
            menu.AddItem(new GUIContent("批量重命名"), false, () => BatchRename());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("导出 JSON"), false, () => BatchExportJson());
            menu.AddItem(new GUIContent("添加标签"), false, () => BatchAddLabel());
            menu.AddItem(new GUIContent("移除标签"), false, () => BatchRemoveLabel());
            menu.ShowAsContext();
        }

        private void BatchDuplicate()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            if (selection.Count == 0) { LogStatus("没有选中。"); return; }
            Directory.CreateDirectory(_state.newAssetFolder);
            int count = 0;
            foreach (var guid in selection.ToList())
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!obj) continue;
                var newObj = ScriptableObject.Instantiate(obj);
                var newName = GetSafeName(obj.name + "_Copy");
                newObj.name = newName;
                var newPath = Path.Combine(_state.newAssetFolder, newName + ".asset").Replace("\\", "/");
                AssetDatabase.CreateAsset(newObj, newPath);
                count++;
            }
            AssetDatabase.SaveAssets();
            RefreshAssetList();
            LogStatus($"复制完成: {count}");
        }

        private void BatchDelete()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            if (selection.Count == 0) { LogStatus("没有选中。"); return; }
            if (_state.confirmBeforeDelete &&
                !EditorUtility.DisplayDialog("确认删除", $"删除 {selection.Count} 个资源?", "是", "否"))
                return;
            int removed = 0;
            foreach (var guid in selection.ToList())
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.DeleteAsset(path))
                    removed++;
            }
            selection.Clear();
            AssetDatabase.SaveAssets();
            RefreshAssetList();
            LogStatus($"删除完成: {removed}");
        }

        private void BatchMove()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            if (selection.Count == 0) { LogStatus("没有选中。"); return; }
            Directory.CreateDirectory(_state.lastMoveFolder);
            int moved = 0;
            foreach (var guid in selection)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var fileName = Path.GetFileName(path);
                var newPath = Path.Combine(_state.lastMoveFolder, fileName).Replace("\\", "/");
                var err = AssetDatabase.MoveAsset(path, newPath);
                if (string.IsNullOrEmpty(err)) moved++;
            }
            AssetDatabase.SaveAssets();
            RefreshAssetList();
            LogStatus($"移动完成: {moved}");
        }

        private void BatchRename()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            if (selection.Count == 0) { LogStatus("没有选中。"); return; }
            int renamed = 0;
            foreach (var guid in selection)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!obj) continue;
                var newName = _state.lastRenamePattern
                    .Replace("{name}", obj.name)
                    .Replace("{guid}", guid.Substring(0, 8))
                    .Replace("{time}", DateTime.Now.ToString("HHmmss"));
                newName = GetSafeName(newName);
                if (obj.name != newName)
                {
                    obj.name = newName;
                    EditorUtility.SetDirty(obj);
                    AssetDatabase.RenameAsset(path, newName);
                    renamed++;
                }
            }
            AssetDatabase.SaveAssets();
            RefreshAssetList();
            LogStatus($"重命名完成: {renamed}");
        }

        private void BatchExportJson()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            if (selection.Count == 0) { LogStatus("没有选中。"); return; }
            Directory.CreateDirectory(_state.lastExportFolder);
            int exported = 0;
            foreach (var guid in selection)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!obj) continue;
                try
                {
                    var json = JsonUtility.ToJson(obj, true);
                    File.WriteAllText(Path.Combine(_state.lastExportFolder, obj.name + ".json"), json);
                    exported++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"导出失败 {obj.name}: {e.Message}");
                }
            }
            LogStatus($"导出 JSON 完成: {exported}");
        }

        private void BatchAddLabel()
        {
            var label = EditorUtility.DisplayDialogComplex("添加标签", "输入标签 (用逗号分隔多标签)？", "确定", "取消", "清除输入");
            if (label == 1) return;
            string input = EditorUtility.DisplayDialog("输入", "请在 Console 窗口输入标签（示例功能）。", "知道了")
                ? "SampleTag" : "SampleTag";
            // 实际可改成 EditorGUILayout.TextField 弹窗实现，简化演示。
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var selection = _state.GetSelection(typeInfo.fullName);
            foreach (var guid in selection)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                var labels = AssetDatabase.GetLabels(obj).ToList();
                if (!labels.Contains(input)) labels.Add(input);
                AssetDatabase.SetLabels(obj, labels.ToArray());
            }
            LogStatus("添加标签完成");
        }

        private void BatchRemoveLabel()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var label = "SampleTag"; // Demo
            var selection = _state.GetSelection(typeInfo.fullName);
            foreach (var guid in selection)
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));
                var labels = AssetDatabase.GetLabels(obj).ToList();
                if (labels.Remove(label))
                    AssetDatabase.SetLabels(obj, labels.ToArray());
            }
            LogStatus("移除标签完成");
        }

        #endregion

        #region Helpers

        private void CreateNewAsset()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            Directory.CreateDirectory(_state.newAssetFolder);
            ScriptableObject inst = ScriptableObject.CreateInstance(typeInfo.type);
            string safeName = GetSafeName(typeInfo.displayName);
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(_state.newAssetFolder, safeName + ".asset"));
            AssetDatabase.CreateAsset(inst, path);
            AssetDatabase.SaveAssets();
            _needRefreshList = true;
            if (_state.autoSelectCreated)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                var sel = _state.GetSelection(typeInfo.fullName);
                sel.Clear();
                sel.Add(guid);
                _state.SetSelection(typeInfo.fullName, sel);
            }
            LogStatus("创建完成: " + Path.GetFileName(path));
        }

        private void RefreshAssetList()
        {
            _currentAssets.Clear();
            _currentGuids.Clear();
            if (_state.selectedTypeIndex < 0 || _state.selectedTypeIndex >= _types.Count) return;
            var typeInfo = _types[_state.selectedTypeIndex];
            var guids = AssetDatabase.FindAssets("t:" + typeInfo.type.Name);
            string search = _state.searchText;
            var comp = _state.caseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!obj) continue;
                if (!string.IsNullOrEmpty(search))
                {
                    if (obj.name.IndexOf(search, comp) < 0 &&
                        path.IndexOf(search, comp) < 0)
                        continue;
                }
                _currentAssets.Add(obj);
                _currentGuids.Add(g);
            }
        }

        private string GetSafeName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "NewConfig";
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
                baseName = baseName.Replace(c, '_');
            return baseName;
        }

        private bool ValidateTypeSelected(out ConfigTypeCache.TypeInfo typeInfo)
        {
            typeInfo = null;
            if (_state.selectedTypeIndex < 0 || _state.selectedTypeIndex >= _types.Count)
            {
                LogStatus("未选择类型");
                return false;
            }
            typeInfo = _types[_state.selectedTypeIndex];
            return true;
        }

        private void MultiSelectAll()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var set = _state.GetSelection(typeInfo.fullName);
            set.Clear();
            foreach (var g in _currentGuids) set.Add(g);
            _state.SetSelection(typeInfo.fullName, set);
        }

        private void MultiSelectClear()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var set = _state.GetSelection(typeInfo.fullName);
            set.Clear();
            _state.SetSelection(typeInfo.fullName, set);
        }

        private void MultiSelectInvert()
        {
            if (!ValidateTypeSelected(out var typeInfo)) return;
            var set = _state.GetSelection(typeInfo.fullName);
            var newSet = new HashSet<string>();
            foreach (var g in _currentGuids)
                if (!set.Contains(g))
                    newSet.Add(g);
            _state.SetSelection(typeInfo.fullName, newSet);
        }

        private void LogStatus(string msg)
        {
            _status = msg;
            _lastOpTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.Height(22)))
            {
                GUILayout.Label(_status ?? "就绪", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("打开存档目录", EditorStyles.miniButton, GUILayout.Width(96)))
                {
                    EditorUtility.RevealInFinder(Application.dataPath);
                }
            }
        }

        private void BuildStyles()
        {
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        #endregion
    }
}