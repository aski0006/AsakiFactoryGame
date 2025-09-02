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
        [Serializable]
        private class Settings
        {
            public int maxDepth = 20;
            public bool includeFiles = true;
            public bool includeHidden = false;
            public bool showFileSize = false;
            public bool useUnicodeTree = true;          // 使用 ├── / └──
            public bool sortFoldersFirst = true;
            public bool alphabetical = true;
            public bool incremental = false;            // 大项目时防止主线程长时间卡顿
            public bool collapseSingleChild = false;    // 如 A/OnlyDir/Leaf.cs -> A/OnlyDir/Leaf.cs 合并（目录链折叠）
            public long minFileSizeBytes = 0;            // 过滤过小文件（0=不过滤）
            public string fileExtensionsFilter = "";     // 例如: ".cs,.shader,.png" 为空表示全部
            public string ignoreNamePatterns = "obj,bin,temp,.DS_Store,Thumbs.db";
            public string ignorePathContains = "Packages~,__backup__,~$";
            public bool ignoreMeta = true;
            public bool trimTrailingSpaces = true;
        }

        private Settings _settings = new Settings();
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

        [MenuItem("Tools/Project/Print Project Structure")]
        public static void Open()
        {
            GetWindow<ProjectStructurePrinterWindow>("Project Structure");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("项目结构打印器", EditorStyles.boldLabel);
            DrawSettings();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成", GUILayout.Height(28)))
                    StartGenerate();

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
            EditorGUILayout.LabelField("遍历设置", EditorStyles.boldLabel);
            _settings.maxDepth = EditorGUILayout.IntField("最大深度", _settings.maxDepth);
            _settings.includeFiles = EditorGUILayout.Toggle("包含文件", _settings.includeFiles);
            _settings.includeHidden = EditorGUILayout.Toggle("包含隐藏(.开头)", _settings.includeHidden);
            _settings.showFileSize = EditorGUILayout.Toggle("显示文件大小", _settings.showFileSize);
            _settings.minFileSizeBytes = EditorGUILayout.LongField("最小文件大小(B)", _settings.minFileSizeBytes);
            _settings.useUnicodeTree = EditorGUILayout.Toggle("使用 Unicode 树形符号", _settings.useUnicodeTree);
            _settings.sortFoldersFirst = EditorGUILayout.Toggle("文件夹优先排序", _settings.sortFoldersFirst);
            _settings.alphabetical = EditorGUILayout.Toggle("按名称字母排序", _settings.alphabetical);
            _settings.incremental = EditorGUILayout.Toggle(new GUIContent("增量模式", "用于超大项目避免一次性卡顿"), _settings.incremental);
            _settings.collapseSingleChild = EditorGUILayout.Toggle("折叠单子目录链", _settings.collapseSingleChild);
            _settings.ignoreMeta = EditorGUILayout.Toggle("忽略 .meta 文件", _settings.ignoreMeta);
            _settings.trimTrailingSpaces = EditorGUILayout.Toggle("去除行尾空格", _settings.trimTrailingSpaces);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("过滤", EditorStyles.boldLabel);
            _settings.fileExtensionsFilter = EditorGUILayout.TextField(new GUIContent("扩展名过滤", "用逗号分隔，如: .cs,.png；留空=全部"), _settings.fileExtensionsFilter);
            _settings.ignoreNamePatterns = EditorGUILayout.TextField(new GUIContent("忽略名称匹配(包含)", "逗号分隔，命中子串即忽略"), _settings.ignoreNamePatterns);
            _settings.ignorePathContains = EditorGUILayout.TextField(new GUIContent("忽略路径包含", "逗号分隔，路径含任一即忽略"), _settings.ignorePathContains);

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

            _extensions = ParseExtensions(_settings.fileExtensionsFilter);
            _cacheKey = BuildCacheKey();
            if (Cache.TryGetValue(_cacheKey, out var cached))
            {
                _output = cached;
                _status = "命中缓存。";
                return;
            }

            if (_settings.incremental)
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
            _targetCountEstimate = 0; // 可不估算；或粗略统计目录数
        }

        private void OnEditorUpdate()
        {
            if (!_isRunningIncremental) return;

            // 每帧处理一定量，避免长卡顿
            int slice = 300; // 可调
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
            var rootInfo = new DirectoryInfo(root);
            AppendLine("Assets/");
            // 收集第一层再递归
            ProcessChildren(rootInfo, 0, ""); // root depth=0
        }

        private void ProcessChildren(DirectoryInfo dir, int depth, string parentPrefix)
        {
            if (depth >= _settings.maxDepth) return;

            var entries = SafeEnumerate(dir.FullName);
            var (folders, files) = SplitAndFilter(entries);

            SortLists(folders, files);

            // 目录折叠单链
            if (_settings.collapseSingleChild)
            {
                CollapseSingleChildChains(folders);
            }

            for (int i = 0; i < folders.Count; i++)
            {
                var folder = new DirectoryInfo(folders[i]);
                bool isLast = (i == folders.Count - 1) && (!_settings.includeFiles || files.Count == 0);
                var (branch, nextPrefix) = MakeBranches(parentPrefix, isLast);
                AppendLine($"{branch}{folder.Name}/");

                _processedDirs++;

                ProcessChildren(folder, depth + 1, nextPrefix);
            }

            if (_settings.includeFiles)
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

            if (task.depth >= _settings.maxDepth) return;

            var entries = SafeEnumerate(task.path);
            var (folders, files) = SplitAndFilter(entries);
            SortLists(folders, files);

            if (_settings.collapseSingleChild)
                CollapseSingleChildChains(folders);

            // 入队子目录
            for (int i = 0; i < folders.Count; i++)
            {
                bool lastDir = (i == folders.Count - 1) && (!_settings.includeFiles || files.Count == 0);
                string nextPrefix = task.prefix + (task.depth == 0 ? "" : (task.isLast ? "    " : "│   "));
                _dirQueue.Enqueue(new DirTask
                {
                    path = folders[i],
                    depth = task.depth + 1,
                    prefix = nextPrefix,
                    isLast = lastDir
                });
            }

            if (_settings.includeFiles)
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

                // 忽略 meta
                if (_settings.ignoreMeta && name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                // hidden
                if (!_settings.includeHidden && name.StartsWith("."))
                    continue;

                if (IsIgnoredByName(name)) continue;
                if (IsIgnoredByPath(p)) continue;

                if (Directory.Exists(p))
                {
                    folders.Add(p);
                }
                else
                {
                    if (!_settings.includeFiles) continue;
                    if (!PassExtensionFilter(name)) continue;
                    if (_settings.minFileSizeBytes > 0)
                    {
                        try
                        {
                            var len = new FileInfo(p).Length;
                            if (len < _settings.minFileSizeBytes) continue;
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
            if (string.IsNullOrWhiteSpace(_settings.ignoreNamePatterns)) return false;
            var tokens = _settings.ignoreNamePatterns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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
            if (string.IsNullOrWhiteSpace(_settings.ignorePathContains)) return false;
            var tokens = _settings.ignorePathContains.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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
            if (_settings.alphabetical)
            {
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            if (!_settings.sortFoldersFirst)
            {
                // 如果用户不要求文件夹优先，则不需要额外处理；(默认 tree 输出仍分开)
            }
        }

        private void CollapseSingleChildChains(List<string> folders)
        {
            // 我们只在输出时折叠，所以这里只改名字（path 不变）
            // 简化：路径链折叠只改变最终显示可读性，在这里不处理真实结构。
            // 若想真正折叠需在构建树结构时做，这里为了简单不实现完全折叠逻辑。
            // 留接口以便后续扩展。
        }

        private IEnumerable<string> SafeEnumerate(string path)
        {
            IEnumerable<string> enums;
            try
            {
                enums = Directory.EnumerateFileSystemEntries(path);
            }
            catch
            {
                yield break;
            }

            foreach (var e in enums)
                yield return e;
        }

        private (string branch, string nextPrefix) MakeBranches(string parentPrefix, bool isLast)
        {
            var branch = parentPrefix + GetBranch(isLast);
            var nextPrefix = parentPrefix + (isLast ? "    " : (_settings.useUnicodeTree ? "│   " : "|   "));
            return (branch, nextPrefix);
        }

        private string GetBranch(bool isLast)
        {
            if (_settings.useUnicodeTree)
                return isLast ? "└── " : "├── ";
            else
                return isLast ? "+-- " : "|-- ";
        }

        private string FormatFileName(FileInfo fi)
        {
            if (!_settings.showFileSize) return fi.Name;
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
            if (_settings.trimTrailingSpaces)
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
            return $"{_settings.maxDepth}|{_settings.includeFiles}|{_settings.includeHidden}|{_settings.showFileSize}|{_settings.useUnicodeTree}|{_settings.sortFoldersFirst}|{_settings.alphabetical}|{_settings.fileExtensionsFilter}|{_settings.ignoreNamePatterns}|{_settings.ignorePathContains}|{_settings.minFileSizeBytes}|{_settings.ignoreMeta}|{_settings.collapseSingleChild}";
        }

        // ---------- 对外静态快速调用 API ----------
        /// <summary>
        /// 直接生成（阻塞），返回字符串（默认使用一套简洁参数）。
        /// </summary>
        public static string GenerateStructureBlocking(
            bool includeFiles = true,
            int maxDepth = 50,
            string extensionsCsv = "",
            bool showFileSize = false)
        {
            var window = CreateInstance<ProjectStructurePrinterWindow>();
            window._settings.includeFiles = includeFiles;
            window._settings.maxDepth = maxDepth;
            window._settings.fileExtensionsFilter = extensionsCsv;
            window._settings.showFileSize = showFileSize;
            window._builder = new StringBuilder(1024 * 512);
            window.GenerateBlocking();
            return window._builder.ToString();
        }
    }
}