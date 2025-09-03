#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.Logging
{
    /// <summary>
    /// 自定义日志导出窗口：
    /// 1. 实时捕获新日志
    /// 2. 可拉取现有 Console 历史（反射）
    /// 3. 过滤 / 搜索 / 导出 (Plain / Markdown)
    /// 4. 可选包含堆栈
    /// </summary>
    public class ConsoleLogExporterWindow : EditorWindow
    {
        private static readonly string Version = "1.0.0";

        private LogCaptureBuffer _buffer;
        private Vector2 _scroll;

        // Filters
        private bool _showInfo = true;
        private bool _showWarning = true;
        private bool _showError = true;
        private string _includeKeyword = "";
        private string _excludeKeyword = "";
        private bool _caseSensitive = false;
        private int _limitLast = 0; // 0 = 不限制
        private bool _includeStackTrace = false;
        private bool _asMarkdown = true;
        private bool _autoScroll = true;
        private bool _useReflectionFetch = false;
        private bool _showHeaderMeta = true;

        private DateTime _startTime;
        private bool _initialized;
        private string _searchCacheLowerInc;
        private string _searchCacheLowerExc;

        [MenuItem("Tools/Debug/Console Log Exporter %#E")]
        public static void Open()
        {
            var w = GetWindow<ConsoleLogExporterWindow>("Log Exporter");
            w.minSize = new Vector2(620, 420);
            w.Show();
        }

        private void OnEnable()
        {
            if (_buffer == null) _buffer = new LogCaptureBuffer();
            _buffer.Collapse = true;
            _startTime = DateTime.Now;
            Application.logMessageReceivedThreaded += HandleLog;
            _initialized = true;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            _buffer.Enqueue(new CapturedLog
            {
                Condition = condition,
                StackTrace = stackTrace,
                Type = type,
                Time = DateTime.Now
            });
            Repaint();
        }

        private void Update()
        {
            if (!_initialized) return;
            _buffer.FlushIncoming();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawLogList();
            EditorGUILayout.Space(4);
            DrawExportArea();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Console Log Exporter v{Version}", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _showInfo = GUILayout.Toggle(_showInfo, "Info", "Button");
            _showWarning = GUILayout.Toggle(_showWarning, "Warning", "Button");
            _showError = GUILayout.Toggle(_showError, "Error/Exception", "Button");
            _buffer.Collapse = GUILayout.Toggle(_buffer.Collapse, "Collapse", "Button");
            _autoScroll = GUILayout.Toggle(_autoScroll, "AutoScroll", "Button");
            if (GUILayout.Button("Clear Buffer", GUILayout.Width(110)))
                _buffer.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _includeKeyword = EditorGUILayout.TextField("含关键词(AND)", _includeKeyword);
            _excludeKeyword = EditorGUILayout.TextField("排除关键词", _excludeKeyword);
            _caseSensitive = EditorGUILayout.ToggleLeft("大小写敏感", _caseSensitive, GUILayout.Width(90));
            _limitLast = EditorGUILayout.IntField("限制条数(0=不限)", _limitLast);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _includeStackTrace = EditorGUILayout.ToggleLeft("包含堆栈", _includeStackTrace, GUILayout.Width(90));
            _asMarkdown = EditorGUILayout.ToggleLeft("Markdown", _asMarkdown, GUILayout.Width(90));
            _showHeaderMeta = EditorGUILayout.ToggleLeft("MetaHeader", _showHeaderMeta, GUILayout.Width(90));
            _useReflectionFetch = EditorGUILayout.ToggleLeft("抓取历史(反射)", _useReflectionFetch, GUILayout.Width(120));
            if (GUILayout.Button("拉取现有 Console", GUILayout.Width(140)))
            {
                ReflectionFetch();
            }
            if (GUILayout.Button("复制到剪贴板", GUILayout.Width(120)))
            {
                var text = BuildExportString();
                EditorGUIUtility.systemCopyBuffer = text;
                Debug.Log("[LogExporter] 已复制日志到剪贴板。");
            }
            if (GUILayout.Button("保存为文件", GUILayout.Width(100)))
            {
                var path = EditorUtility.SaveFilePanel("导出日志", "", "console_log.txt", "txt");
                if (!string.IsNullOrEmpty(path))
                {
                    System.IO.File.WriteAllText(path, BuildExportString(), Encoding.UTF8);
                    Debug.Log($"[LogExporter] 已保存: {path}");
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void ReflectionFetch()
        {
            if (!_useReflectionFetch)
            {
                Debug.LogWarning("[LogExporter] 请先勾选 '抓取历史(反射)' 再执行。");
                return;
            }

            if (!ConsoleReflectionUtil.Initialize())
            {
                Debug.LogError("[LogExporter] 反射初始化失败，可能 Unity 版本不兼容。");
                return;
            }

            int before = _buffer.Logs.Count;
            int cnt = 0;
            ConsoleReflectionUtil.ForEachEntry((cond, stack, typeInt) =>
            {
                _buffer.Enqueue(new CapturedLog
                {
                    Condition = cond,
                    StackTrace = stack,
                    Type = (LogType)typeInt,
                    Time = DateTime.Now.AddSeconds(-1) // 历史统一给一个接近时间
                });
                cnt++;
                return true;
            });
            Debug.Log($"[LogExporter] 已抓取历史日志 {cnt} 条（之前缓冲 {before} 条）。");
        }

        private void DrawLogList()
        {
            var logs = _buffer.Logs;
            if (logs.Count == 0)
            {
                EditorGUILayout.HelpBox("当前无捕获日志。", MessageType.Info);
                return;
            }

            PrepareSearchCaches();

            int shown = 0;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180), GUILayout.ExpandHeight(true));
            for (int i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                if (!PassFilter(log)) continue;
                shown++;
                if (_limitLast > 0 && shown <= (GetFilteredCount() - _limitLast))
                    continue; // Skip older

                DrawSingleLogEntry(log, i);
            }
            if (_autoScroll && Event.current.type == EventType.Repaint)
                _scroll.y = float.MaxValue;

            EditorGUILayout.EndScrollView();
        }

        private int _filteredCountCache = -1;
        private int GetFilteredCount()
        {
            if (_filteredCountCache >= 0) return _filteredCountCache;
            int c = 0;
            foreach (var l in _buffer.Logs) if (PassFilter(l)) c++;
            _filteredCountCache = c;
            return c;
        }

        private void PrepareSearchCaches()
        {
            _filteredCountCache = -1;
            _searchCacheLowerInc = _caseSensitive ? _includeKeyword : _includeKeyword.ToLowerInvariant();
            _searchCacheLowerExc = _caseSensitive ? _excludeKeyword : _excludeKeyword.ToLowerInvariant();
        }

        private bool PassFilter(CapturedLog log)
        {
            if (log == null) return false;
            if (log.Type == LogType.Log && !_showInfo) return false;
            if (log.Type == LogType.Warning && !_showWarning) return false;
            if ((log.Type == LogType.Error || log.Type == LogType.Exception || log.Type == LogType.Assert) && !_showError) return false;

            if (!string.IsNullOrEmpty(_includeKeyword))
            {
                var target = _caseSensitive ? log.Condition : log.Condition.ToLowerInvariant();
                if (!target.Contains(_searchCacheLowerInc))
                    return false;
            }

            if (!string.IsNullOrEmpty(_excludeKeyword))
            {
                var target = _caseSensitive ? log.Condition : log.Condition.ToLowerInvariant();
                if (target.Contains(_searchCacheLowerExc))
                    return false;
            }

            return true;
        }

        private void DrawSingleLogEntry(CapturedLog log, int index)
        {
            var color = GUI.color;
            switch (log.Type)
            {
                case LogType.Warning: GUI.color = new Color(1f, 0.9f, 0.5f); break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception: GUI.color = new Color(1f, 0.6f, 0.6f); break;
                default: GUI.color = Color.white; break;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(
                $"[{log.Time:HH:mm:ss}] ({log.ShortCategory}{(log.Count > 1 ? $" x{log.Count}" : "")}) {log.FirstLineCached}",
                EditorStyles.boldLabel);

            // 展开详情
            if (EditorGUILayout.BeginFoldoutHeaderGroup(false, "详情"))
            {
                EditorGUILayout.LabelField("完整消息:", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(log.Condition, GUILayout.MinHeight(40));

                if (_includeStackTrace && !string.IsNullOrEmpty(log.StackTrace))
                {
                    EditorGUILayout.LabelField("StackTrace:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.TextArea(log.StackTrace, GUILayout.MinHeight(60));
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.EndVertical();

            GUI.color = color;
        }

        private void DrawExportArea()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("预览导出（只显示前 10 行，不代表最终完整内容）", EditorStyles.miniBoldLabel);
            var preview = BuildExportString(limitPreviewLines: 10);
            EditorGUILayout.TextArea(preview, GUILayout.MinHeight(80));
            EditorGUILayout.EndVertical();
        }

        private string BuildExportString(int limitPreviewLines = -1)
        {
            var sb = new StringBuilder(8 * 1024);
            if (_showHeaderMeta)
            {
                sb.AppendLine("=== Console Log Export ===");
                sb.AppendLine($"GeneratedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Unity: {Application.unityVersion}  Platform: {Application.platform}");
                sb.AppendLine($"Filters: Info={_showInfo} Warn={_showWarning} Err={_showError} Include='{_includeKeyword}' Exclude='{_excludeKeyword}' LimitLast={_limitLast}");
                sb.AppendLine($"IncludeStack={_includeStackTrace} Markdown={_asMarkdown} Collapse={_buffer.Collapse}");
                sb.AppendLine();
            }

            if (_asMarkdown) sb.AppendLine("```text");

            int count = 0;
            int filteredTotal = GetFilteredCount();
            foreach (var log in _buffer.Logs)
            {
                if (!PassFilter(log)) continue;
                if (_limitLast > 0 && (filteredTotal - count) > _limitLast)
                {
                    // Skip until we are in last N
                    filteredTotal--;
                    continue;
                }

                string line = FormatLogLine(log);
                sb.AppendLine(line);
                if (_includeStackTrace && !string.IsNullOrEmpty(log.StackTrace))
                {
                    sb.AppendLine(IndentMultiline(log.StackTrace, "    "));
                }

                count++;
                if (limitPreviewLines > 0 && count >= limitPreviewLines)
                {
                    sb.AppendLine("... (preview truncated)");
                    break;
                }
            }

            if (_asMarkdown) sb.AppendLine("```");
            return sb.ToString();
        }

        private string FormatLogLine(CapturedLog log)
        {
            var prefix = log.ShortCategory;
            string head = $"[{log.Time:HH:mm:ss}] [{prefix}]";
            if (log.Count > 1) head += $"(x{log.Count})";
            // 仅首行
            return $"{head} {log.FirstLineCached}";
        }

        private static string IndentMultiline(string text, string indent)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
                lines[i] = indent + lines[i];
            return string.Join("\n", lines);
        }
    }
}
#endif