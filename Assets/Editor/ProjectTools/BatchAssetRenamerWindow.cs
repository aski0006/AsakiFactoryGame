/*
 * 批量资源重命名工具 (支持 Dry Run)
 * 功能点:
 *  1. 前缀添加 (Prefix)
 *  2. 去除空格 (Remove Spaces)
 *  3. 正则替换 (可添加多条规则) Regex (Pattern -> Replacement, 支持 IgnoreCase)
 *  4. Dry Run 预览：不真正执行，仅显示重命名计划
 *  5. 冲突 / 未变化 / 无效 情况提示
 *  6. 支持基于当前 Selection 或 指定根目录扫描
 *  7. 可选包含文件夹
 *
 * 使用:
 *  菜单：Tools/Project Tools/Batch Asset Renamer
 *  1) 设置参数 -> 点击 [Preview (Dry Run)] 查看预览
 *  2) 确认无冲突后点击 [Apply Rename]
 *
 * 注意:
 *  - 正则按添加顺序执行
 *  - 仅修改文件名（不含扩展名）
 *  - Prefix 若已存在且勾选 SkipIfExists 则不会重复添加
 *  - Rename 操作不可 Undo，请谨慎使用
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Editor.ProjectTools
{
    internal class BatchAssetRenamerWindow : EditorWindow
    {
        [Serializable]
        private class RegexRule
        {
            public bool enabled = true;
            public string pattern = "";
            public string replacement = "";
            public bool ignoreCase = false;
        }

        private class RenameResult
        {
            public string assetPath;
            public string dir;
            public string oldName;
            public string newName;
            public string extension;
            public string newFullPath;
            public bool willRename;
            public string status; // OK / Conflict / Unchanged / Invalid
            public Color uiColor;
        }

        // 用户参数
        private bool useSelectionOnly = true;
        private string rootFolder = "Assets";
        private bool includeFolders = false;

        private string prefix = "";
        private bool skipIfHasPrefix = true;
        private bool removeSpaces = true;
        private bool trimName = true;

        private List<RegexRule> regexRules = new()
        {
            new RegexRule{ pattern = "\\s+", replacement = "_", enabled = false, ignoreCase = false }
        };

        // 状态
        private Vector2 _scroll;
        private List<RenameResult> _results = new();
        private bool _hasPreview = false;
        private int _renameCount;
        private int _conflictCount;
        private int _unchangedCount;
        private int _invalidCount;

        private GUIStyle _headerStyle;
        private GUIStyle _miniStatusStyle;

        [MenuItem("Tools/Project Tools/Batch Asset Renamer")]
        private static void Open()
        {
            var win = GetWindow<BatchAssetRenamerWindow>("Batch Asset Renamer");
            win.minSize = new Vector2(780, 480);
            win.Show();
        }

        private void OnGUI()
        {
            InitStyles();
            DrawConfig();
            EditorGUILayout.Space(6);
            DrawRegexRules();
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(6);
            DrawSummary();
            EditorGUILayout.Space(4);
            DrawResultList();
        }

        private void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };
            }
            if (_miniStatusStyle == null)
            {
                _miniStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontStyle = FontStyle.Italic
                };
            }
        }

        private void DrawConfig()
        {
            EditorGUILayout.LabelField("扫描范围", _headerStyle);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                useSelectionOnly = EditorGUILayout.ToggleLeft("仅处理当前选中 (Selection) 资源", useSelectionOnly);
                using (new EditorGUI.DisabledScope(useSelectionOnly))
                {
                    rootFolder = EditorGUILayout.TextField(new GUIContent("根目录", "必须位于 Assets/ 下"), rootFolder);
                }
                includeFolders = EditorGUILayout.ToggleLeft("包含文件夹 (Folder)", includeFolders);
            }

            EditorGUILayout.LabelField("命名规则", _headerStyle);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                prefix = EditorGUILayout.TextField(new GUIContent("前缀 Prefix", "为空则不添加"), prefix);
                using (new EditorGUILayout.HorizontalScope())
                {
                    skipIfHasPrefix = EditorGUILayout.ToggleLeft("若已有前缀则跳过添加", skipIfHasPrefix, GUILayout.Width(160));
                    removeSpaces = EditorGUILayout.ToggleLeft("去除空格 (直接删除)", removeSpaces, GUILayout.Width(140));
                    trimName = EditorGUILayout.ToggleLeft("Trim 头尾空白", trimName, GUILayout.Width(120));
                }
            }
        }

        private void DrawRegexRules()
        {
            EditorGUILayout.LabelField("正则规则 (按顺序应用)", _headerStyle);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                for (int i = 0; i < regexRules.Count; i++)
                {
                    var rule = regexRules[i];
                    using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(20));
                            rule.pattern = EditorGUILayout.TextField("Pattern", rule.pattern);
                            if (GUILayout.Button("X", GUILayout.Width(24)))
                            {
                                regexRules.RemoveAt(i);
                                GUI.FocusControl(null);
                                break;
                            }
                        }
                        rule.replacement = EditorGUILayout.TextField("Replacement", rule.replacement);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            rule.ignoreCase = EditorGUILayout.ToggleLeft("Ignore Case", rule.ignoreCase, GUILayout.Width(100));
                            EditorGUILayout.LabelField("示例: ( ) -> _  或  [A-Z] -> a", _miniStatusStyle);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ 添加规则", GUILayout.Width(120)))
                    {
                        regexRules.Add(new RegexRule());
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview (Dry Run)", GUILayout.Height(28)))
                {
                    RunPreview();
                }

                using (new EditorGUI.DisabledScope(!_hasPreview || _renameCount == 0))
                {
                    if (GUILayout.Button("Apply Rename", GUILayout.Height(28)))
                    {
                        if (EditorUtility.DisplayDialog("确认重命名",
                                $"将执行 {_renameCount} 个重命名操作。\n该操作不可撤销，是否继续？",
                                "确认执行", "取消"))
                        {
                            Apply();
                        }
                    }
                }

                if (GUILayout.Button("清空预览", GUILayout.Height(28)))
                {
                    _results.Clear();
                    _hasPreview = false;
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSummary()
        {
            if (!_hasPreview)
            {
                EditorGUILayout.HelpBox("点击 Preview (Dry Run) 生成重命名计划。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("预览结果", _headerStyle);
            EditorGUILayout.HelpBox(
                $"Total: {_results.Count} | Rename: {_renameCount} | Unchanged: {_unchangedCount} | Conflict: {_conflictCount} | Invalid: {_invalidCount}",
                _conflictCount > 0 ? MessageType.Warning : MessageType.Info);
        }

        private void DrawResultList()
        {
            if (!_hasPreview) return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _results)
            {
                using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                {
                    var c = GUI.color;
                    GUI.color = r.uiColor;
                    GUILayout.Label(r.status, GUILayout.Width(70));
                    GUI.color = c;

                    EditorGUILayout.LabelField(new GUIContent(r.oldName, r.assetPath), GUILayout.Width(240));
                    GUILayout.Label("=>", GUILayout.Width(24));
                    EditorGUILayout.LabelField(r.newName, GUILayout.Width(240));

                    if (GUILayout.Button("Ping", GUILayout.Width(40)))
                    {
                        var obj = AssetDatabase.LoadMainAssetAtPath(r.assetPath);
                        EditorGUIUtility.PingObject(obj);
                        Selection.activeObject = obj;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void RunPreview()
        {
            _results.Clear();
            _hasPreview = true;
            _renameCount = _conflictCount = _unchangedCount = _invalidCount = 0;

            var paths = CollectAssetPaths();
            var existingNameMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                var dir = Path.GetDirectoryName(p)?.Replace("\\", "/") ?? "Assets";
                var oldName = Path.GetFileNameWithoutExtension(p);
                var extension = Path.GetExtension(p);

                string processed = oldName;

                if (trimName) processed = processed.Trim();
                if (removeSpaces) processed = processed.Replace(" ", "");

                // 正则执行
                foreach (var rule in regexRules)
                {
                    if (!rule.enabled || string.IsNullOrEmpty(rule.pattern)) continue;
                    try
                    {
                        var options = rule.ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                        processed = Regex.Replace(processed, rule.pattern, rule.replacement ?? "", options);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Regex 规则错误: {rule.pattern}  => {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(prefix))
                {
                    if (skipIfHasPrefix)
                    {
                        if (!processed.StartsWith(prefix, StringComparison.Ordinal))
                            processed = prefix + processed;
                    }
                    else
                    {
                        processed = prefix + processed;
                    }
                }

                // 验证
                string status;
                bool willRename = false;
                string newName = processed;
                string newFullPath = $"{dir}/{newName}{extension}";

                if (newName.Length == 0)
                {
                    status = "Invalid";
                    _invalidCount++;
                }
                else if (newName == oldName)
                {
                    status = "Unchanged";
                    _unchangedCount++;
                }
                else if (FileOrFolderExists(newFullPath) || existingNameMap.Contains(newFullPath))
                {
                    status = "Conflict";
                    _conflictCount++;
                }
                else
                {
                    status = "OK";
                    willRename = true;
                    _renameCount++;
                    existingNameMap.Add(newFullPath);
                }

                var uiColor = Color.white;
                switch (status)
                {
                    case "OK": uiColor = new Color(.75f, 1f, .75f); break;
                    case "Conflict": uiColor = new Color(1f, .6f, .6f); break;
                    case "Unchanged": uiColor = new Color(.85f, .85f, .85f); break;
                    case "Invalid": uiColor = new Color(1f, .4f, .4f); break;
                }

                _results.Add(new RenameResult
                {
                    assetPath = p,
                    dir = dir,
                    oldName = oldName,
                    newName = newName,
                    extension = extension,
                    newFullPath = newFullPath,
                    willRename = willRename,
                    status = status,
                    uiColor = uiColor
                });
            }
        }

        private void Apply()
        {
            int success = 0;
            int fail = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var r in _results)
                {
                    if (!r.willRename) continue;
                    // 仅改变名字，不改变目录
                    var res = AssetDatabase.RenameAsset(r.assetPath, r.newName);
                    if (string.IsNullOrEmpty(res))
                    {
                        success++;
                        r.status = "Renamed";
                        r.uiColor = new Color(0.55f, 0.9f, 0.55f);
                    }
                    else
                    {
                        fail++;
                        r.status = "Fail";
                        r.uiColor = new Color(1f, 0.4f, 0.4f);
                        Debug.LogError($"重命名失败: {r.assetPath} => {r.newName}  Error: {res}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[BatchAssetRenamer] 完成: 成功 {success}, 失败 {fail}");
            RecalculateStatusAfterApply();
        }

        private void RecalculateStatusAfterApply()
        {
            _renameCount = _conflictCount = _unchangedCount = _invalidCount = 0;
            foreach (var r in _results)
            {
                switch (r.status)
                {
                    case "OK":
                        _renameCount++;
                        break;
                    case "Conflict":
                        _conflictCount++;
                        break;
                    case "Unchanged":
                        _unchangedCount++;
                        break;
                    case "Invalid":
                        _invalidCount++;
                        break;
                }
            }
        }

        private List<string> CollectAssetPaths()
        {
            var list = new List<string>();
            if (useSelectionOnly)
            {
                var objs = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
                foreach (var o in objs)
                {
                    var p = AssetDatabase.GetAssetPath(o);
                    if (IsValidTarget(p)) list.Add(p);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(rootFolder) || !rootFolder.StartsWith("Assets", StringComparison.Ordinal))
                {
                    Debug.LogWarning("根目录不合法，已回退为 Assets");
                    rootFolder = "Assets";
                }

                var guids = AssetDatabase.FindAssets("", new[] { rootFolder });
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (IsValidTarget(p)) list.Add(p);
                }
            }

            // 去重
            return list.Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private bool IsValidTarget(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            bool isFolder = AssetDatabase.IsValidFolder(path);
            if (isFolder && !includeFolders) return false;

            // 排除场景中的内嵌子资源；只处理主资源 (Path 里不含 " /")
            if (path.Contains(" /")) return false;

            return true;
        }

        private bool FileOrFolderExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                return true;
            if (AssetDatabase.IsValidFolder(assetPath))
                return true;
            return false;
        }
    }
}