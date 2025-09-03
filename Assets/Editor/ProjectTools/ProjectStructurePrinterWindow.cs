using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.ProjectTools
{
    public class ProjectStructurePrinterWindow : EditorWindow
    {
        // 使用 ScriptableObject 持久化
        private ProjectStructurePrinterSettings _settingsAsset;
        private ProjectStructurePrinterSettings.SettingsData S => _settingsAsset.Data;

        private Vector2 _scroll;
        private string _output = "";
        private string _status = "";
        private double _lastGenTime;

        // 增量遍历状态
        private bool _isRunningIncremental;
        private Queue<DirTask> _dirQueue;
        private List<string> _extensions;
        private StringBuilder _builder;
        private int _processedDirs;
        private int _processedFiles;
        private int _lines;
        private int _targetCountEstimate;
        private string _cacheKey;
        private static readonly Dictionary<string, string> Cache = new();

        private struct DirTask
        {
            public string path;
            public int depth;
            public string prefix;
            public bool isLast;
        }

        [MenuItem("Tools/Project Tools/Print Project Structure")]
        public static void Open()
        {
            GetWindow<ProjectStructurePrinterWindow>("Project Structure");
        }

        [MenuItem("Tools/Project Tools/Config/Create Project Structure Settings Asset")]
        public static void CreateSettingsAsset()
        {
            var asset = ProjectStructurePrinterSettings.LoadOrCreate();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void OnEnable()
        {
            _settingsAsset = ProjectStructurePrinterSettings.LoadOrCreate();
        }

        private void OnGUI()
        {
            if (_settingsAsset == null)
            {
                EditorGUILayout.HelpBox("未找到配置资产，点击下方按钮创建。", MessageType.Warning);
                if (GUILayout.Button("创建配置资产"))
                {
                    _settingsAsset = ProjectStructurePrinterSettings.LoadOrCreate();
                }
                return;
            }

            EditorGUILayout.LabelField("项目结构打印器", EditorStyles.boldLabel);

            DrawSettings();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !_isRunningIncremental;
                if (GUILayout.Button("生成", GUILayout.Height(28)))
                    StartGenerate();
                GUI.enabled = true;

                if (GUILayout.Button("复制到剪贴板", GUILayout.Height(28)))
                {
                    if (!string.IsNullOrEmpty(_output))
                    {
                        EditorGUIUtility.systemCopyBuffer = _output;
                        ShowNotification(new GUIContent("已复制"));
                    }
                }

                if (_isRunningIncremental)
                {
                    if (GUILayout.Button("停止", GUILayout.Height(28)))
                        StopIncremental("用户停止。");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"状态: {_status}");
            if (!string.IsNullOrEmpty(_output))
            {
                EditorGUILayout.LabelField(
                    $"统计: {_lines} 行 | 目录 {_processedDirs} | 文件 {_processedFiles} | 用时 {(Time.realtimeSinceStartup - _lastGenTime):F2}s");
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("遍历设置 (持久化于 ScriptableObject)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            S.maxDepth = EditorGUILayout.IntField("最大深度", S.maxDepth);
            S.includeFiles = EditorGUILayout.Toggle("包含文件", S.includeFiles);
            S.includeHidden = EditorGUILayout.Toggle("包含隐藏(.开头)", S.includeHidden);
            S.showFileSize = EditorGUILayout.Toggle("显示文件大小", S.showFileSize);
            S.minFileSizeBytes = EditorGUILayout.LongField("最小文件大小(B)", S.minFileSizeBytes);
            S.useUnicodeTree = EditorGUILayout.Toggle("使用 Unicode 树形符号", S.useUnicodeTree);
            S.sortFoldersFirst = EditorGUILayout.Toggle("文件夹优先排序", S.sortFoldersFirst);
            S.alphabetical = EditorGUILayout.Toggle("按名称字母排序", S.alphabetical);
            S.incremental = EditorGUILayout.Toggle(new GUIContent("增量模式", "用于超大项目避免一次性卡顿"), S.incremental);
            S.collapseSingleChild = EditorGUILayout.Toggle("折叠单子目录链", S.collapseSingleChild);
            S.ignoreMeta = EditorGUILayout.Toggle("忽略 .meta 文件", S.ignoreMeta);
            S.trimTrailingSpaces = EditorGUILayout.Toggle("去除行尾空格", S.trimTrailingSpaces);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("过滤", EditorStyles.boldLabel);
            S.fileExtensionsFilter = EditorGUILayout.TextField(new GUIContent("扩展名过滤", "用逗号分隔，如: .cs,.png；留空=全部"), S.fileExtensionsFilter);
            S.ignoreNamePatterns = EditorGUILayout.TextField(new GUIContent("忽略名称匹配(包含)", "逗号分隔，命中子串即忽略"), S.ignoreNamePatterns);
            S.ignorePathContains = EditorGUILayout.TextField(new GUIContent("忽略路径包含", "逗号分隔，路径含任一即忽略"), S.ignorePathContains);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settingsAsset);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存配置 (Mark Dirty)"))
                {
                    EditorUtility.SetDirty(_settingsAsset);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("还原默认"))
                {
                    if (EditorUtility.DisplayDialog("确认", "恢复默认将丢失当前设置，确定？", "是", "否"))
                    {
                        _settingsAsset.ResetToDefault();
                        GUI.FocusControl(null);
                    }
                }
                if (GUILayout.Button("打开资产"))
                {
                    Selection.activeObject = _settingsAsset;
                    EditorGUIUtility.PingObject(_settingsAsset);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void StartGenerate()
        {
            if (!Directory.Exists(Application.dataPath))
            {
                _status = "Assets 路径不存在?";
                return;
            }
            _output = "";
            _lines = 0;
            _processedDirs = 0;
            _processedFiles = 0;
            _lastGenTime = Time.realtimeSinceStartup;

            _extensions = ParseExtensions(S.fileExtensionsFilter);
            _cacheKey = BuildCacheKey();
            if (Cache.TryGetValue(_cacheKey, out var cached))
            {
                _output = cached;
                _status = "命中缓存。";
                return;
            }

            if (S.incremental)
            {
                PrepareIncremental();
                _isRunningIncremental = true;
                _status = "增量生成中...";
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;
            }
            else
            {
                _status = "生成中...";
                _builder = new StringBuilder(1024 * 512);
                try
                {
                    GenerateBlocking();
                    FinalizeOutput();
                    _status = "完成。";
                }
                catch (Exception ex)
                {
                    _status = "错误: " + ex.Message;
                    Debug.LogException(ex);
                }
            }
        }

        private void PrepareIncremental()
        {
            _builder = new StringBuilder(1024 * 512);
            _dirQueue = new Queue<DirTask>();
            _dirQueue.Enqueue(new DirTask
            {
                path = Application.dataPath,
                depth = 0,
                prefix = "",
                isLast = true
            });
            _targetCountEstimate = 0;
        }

        private void OnEditorUpdate()
        {
            if (!_isRunningIncremental) return;

            int slice = 300;
            while (slice-- > 0 && _dirQueue.Count > 0)
            {
                var task = _dirQueue.Dequeue();
                ProcessDirectory(task);
            }

            if (_dirQueue.Count == 0)
            {
                StopIncremental("完成。");
                FinalizeOutput();
            }

            Repaint();
        }

        private void StopIncremental(string reason)
        {
            _isRunningIncremental = false;
            EditorApplication.update -= OnEditorUpdate;
            _status = reason;
        }

        private void GenerateBlocking()
        {
            var root = Application.dataPath;
            AppendLine("Assets/");
            ProcessChildren(new DirectoryInfo(root), 0, "");
        }

        private void ProcessChildren(DirectoryInfo dir, int depth, string parentPrefix)
        {
            if (depth >= S.maxDepth) return;

            var entries = SafeEnumerate(dir.FullName);
            var (folders, files) = SplitAndFilter(entries);

            SortLists(folders, files);

            if (S.collapseSingleChild)
            {
                CollapseSingleChildChains(folders);
            }

            for (int i = 0; i < folders.Count; i++)
            {
                var folder = new DirectoryInfo(folders[i]);
                bool isLast = (i == folders.Count - 1) && (!S.includeFiles || files.Count == 0);
                var (branch, nextPrefix) = MakeBranches(parentPrefix, isLast);
                AppendLine($"{branch}{folder.Name}/");
                _processedDirs++;
                ProcessChildren(folder, depth + 1, nextPrefix);
            }

            if (S.includeFiles)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var fi = new FileInfo(files[i]);
                    bool isLast = i == files.Count - 1;
                    var (branch, _) = MakeBranches(parentPrefix, isLast);
                    AppendLine($"{branch}{FormatFileName(fi)}");
                    _processedFiles++;
                }
            }
        }

        private void ProcessDirectory(DirTask task)
        {
            var dirInfo = new DirectoryInfo(task.path);
            if (task.depth == 0)
            {
                AppendLine("Assets/");
                _processedDirs++;
            }
            else
            {
                AppendLine($"{task.prefix}{(task.isLast ? GetBranch(true) : GetBranch(false))}{dirInfo.Name}/");
                _processedDirs++;
            }

            if (task.depth >= S.maxDepth) return;

            var entries = SafeEnumerate(task.path);
            var (folders, files) = SplitAndFilter(entries);
            SortLists(folders, files);

            if (S.collapseSingleChild)
                CollapseSingleChildChains(folders);

            for (int i = 0; i < folders.Count; i++)
            {
                bool lastDir = (i == folders.Count - 1) && (!S.includeFiles || files.Count == 0);
                string nextPrefix = task.prefix + (task.depth == 0 ? "" : (task.isLast ? "    " : "│   "));
                _dirQueue.Enqueue(new DirTask
                {
                    path = folders[i],
                    depth = task.depth + 1,
                    prefix = nextPrefix,
                    isLast = lastDir
                });
            }

            if (S.includeFiles)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var fi = new FileInfo(files[i]);
                    bool isLast = i == files.Count - 1;
                    var branch = task.prefix + (task.depth == 0 ? "" : (task.isLast ? "    " : "│   ")) + GetBranch(isLast);
                    AppendLine($"{branch}{FormatFileName(fi)}");
                    _processedFiles++;
                }
            }
        }

        private (List<string> folders, List<string> files) SplitAndFilter(IEnumerable<string> entries)
        {
            var folders = new List<string>(64);
            var files = new List<string>(128);

            foreach (var p in entries)
            {
                var name = Path.GetFileName(p);
                if (string.IsNullOrEmpty(name)) continue;

                if (S.ignoreMeta && name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!S.includeHidden && name.StartsWith("."))
                    continue;

                if (IsIgnoredByName(name)) continue;
                if (IsIgnoredByPath(p)) continue;

                if (Directory.Exists(p))
                {
                    folders.Add(p);
                }
                else
                {
                    if (!S.includeFiles) continue;
                    if (!PassExtensionFilter(name)) continue;
                    if (S.minFileSizeBytes > 0)
                    {
                        try
                        {
                            var len = new FileInfo(p).Length;
                            if (len < S.minFileSizeBytes) continue;
                        }
                        catch { }
                    }
                    files.Add(p);
                }
            }
            return (folders, files);
        }

        private bool IsIgnoredByName(string name)
        {
            if (string.IsNullOrWhiteSpace(S.ignoreNamePatterns)) return false;
            var tokens = S.ignoreNamePatterns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                var trim = t.Trim();
                if (trim.Length == 0) continue;
                if (name.IndexOf(trim, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private bool IsIgnoredByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(S.ignorePathContains)) return false;
            var tokens = S.ignorePathContains.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                var trim = t.Trim();
                if (trim.Length == 0) continue;
                if (path.IndexOf(trim, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private bool PassExtensionFilter(string name)
        {
            if (_extensions == null || _extensions.Count == 0) return true;
            var ext = Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext)) return false;
            return _extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private void SortLists(List<string> folders, List<string> files)
        {
            if (S.alphabetical)
            {
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void CollapseSingleChildChains(List<string> folders)
        {
            // TODO: 以后可实现真正折叠逻辑
        }

        private IEnumerable<string> SafeEnumerate(string path)
        {
            IEnumerable<string> enums;
            try { enums = Directory.EnumerateFileSystemEntries(path); }
            catch { yield break; }
            foreach (var e in enums) yield return e;
        }

        private (string branch, string nextPrefix) MakeBranches(string parentPrefix, bool isLast)
        {
            var branch = parentPrefix + GetBranch(isLast);
            var nextPrefix = parentPrefix + (isLast ? "    " : (S.useUnicodeTree ? "│   " : "|   "));
            return (branch, nextPrefix);
        }

        private string GetBranch(bool isLast)
        {
            if (S.useUnicodeTree)
                return isLast ? "└── " : "├── ";
            else
                return isLast ? "+-- " : "|-- ";
        }

        private string FormatFileName(FileInfo fi)
        {
            if (!S.showFileSize) return fi.Name;
            long size = fi.Length;
            return $"{fi.Name} ({FormatSize(size)})";
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1}KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F1}MB";
            double gb = mb / 1024.0;
            return $"{gb:F2}GB";
        }

        private void AppendLine(string line)
        {
            if (S.trimTrailingSpaces)
                line = line.TrimEnd();
            _builder.AppendLine(line);
            _lines++;
        }

        private void FinalizeOutput()
        {
            _output = _builder.ToString();
            Cache[_cacheKey] = _output;
            Repaint();
        }

        private List<string> ParseExtensions(string extFilter)
        {
            if (string.IsNullOrWhiteSpace(extFilter)) return new List<string>();
            return extFilter
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e =>
                {
                    var t = e.Trim();
                    if (!t.StartsWith(".")) t = "." + t;
                    return t;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildCacheKey()
        {
            return $"{S.maxDepth}|{S.includeFiles}|{S.includeHidden}|{S.showFileSize}|{S.useUnicodeTree}|{S.sortFoldersFirst}|{S.alphabetical}|{S.fileExtensionsFilter}|{S.ignoreNamePatterns}|{S.ignorePathContains}|{S.minFileSizeBytes}|{S.ignoreMeta}|{S.collapseSingleChild}";
        }

        // ---------- 对外静态快速调用 API（保持兼容，不使用资产） ----------
        public static string GenerateStructureBlocking(
            bool includeFiles = true,
            int maxDepth = 50,
            string extensionsCsv = "",
            bool showFileSize = false)
        {
            var tempSettings = new ProjectStructurePrinterSettings.SettingsData
            {
                includeFiles = includeFiles,
                maxDepth = maxDepth,
                fileExtensionsFilter = extensionsCsv,
                showFileSize = showFileSize
            };

            var window = CreateInstance<ProjectStructurePrinterWindow>();
            // 构造临时状态
            window._settingsAsset = ScriptableObject.CreateInstance<ProjectStructurePrinterSettings>();
            window._settingsAsset.ResetToDefault();
            var dataField = window._settingsAsset.Data;
            dataField.includeFiles = tempSettings.includeFiles;
            dataField.maxDepth = tempSettings.maxDepth;
            dataField.fileExtensionsFilter = tempSettings.fileExtensionsFilter;
            dataField.showFileSize = tempSettings.showFileSize;

            window._builder = new StringBuilder(1024 * 512);
            window._extensions = window.ParseExtensions(dataField.fileExtensionsFilter);
            window.GenerateBlocking();
            return window._builder.ToString();
        }
    }
}