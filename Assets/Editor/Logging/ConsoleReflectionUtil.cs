#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace Editor.Logging
{
    /// <summary>
    /// 通过反射访问 Unity 内部 Console 历史日志。
    /// 可在窗口中执行一次性抓取，把已有 Console 中内容同步到自维护缓冲。
    /// 注意：此实现依赖 Unity 内部 API，版本差异可能失效。
    /// </summary>
    public static class ConsoleReflectionUtil
    {
        private static Type _logEntriesType;
        private static Type _logEntryType;
        private static MethodInfo _startGettingEntries;
        private static MethodInfo _endGettingEntries;
        private static MethodInfo _getEntryInternal;
        private static MethodInfo _getCount;

        private static FieldInfo _conditionField;
        private static FieldInfo _stackTraceField;
        private static FieldInfo _modeField;
        private static bool _initialized;

        public static bool Initialize()
        {
            if (_initialized) return true;
            _logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor.CoreModule");
            _logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor.CoreModule");
            if (_logEntriesType == null || _logEntryType == null) return false;

            _startGettingEntries = _logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
            _endGettingEntries = _logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
            _getEntryInternal = _logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
            _getCount = _logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);

            _conditionField = _logEntryType.GetField("condition");
            _stackTraceField = _logEntryType.GetField("stackTrace");
            _modeField = _logEntryType.GetField("mode");

            _initialized = _startGettingEntries != null &&
                           _endGettingEntries != null &&
                           _getEntryInternal != null &&
                           _getCount != null &&
                           _conditionField != null &&
                           _stackTraceField != null &&
                           _modeField != null;
            return _initialized;
        }

        public static int GetConsoleCount()
        {
            if (!Initialize()) return 0;
            return (int)_getCount.Invoke(null, null);
        }

        /// <summary>
        /// 遍历 Console 内部日志，调用回调。回调返回 false 终止。
        /// </summary>
        public static void ForEachEntry(Func<string,string,int,bool> visitor, int max = int.MaxValue)
        {
            if (!Initialize()) return;

            var logEntry = Activator.CreateInstance(_logEntryType);
            int total = GetConsoleCount();
            if (total == 0) return;

            _startGettingEntries.Invoke(null, null);
            try
            {
                int limit = Math.Min(total, max);
                for (int i = 0; i < limit; i++)
                {
                    object[] args = { i, logEntry };
                    _getEntryInternal.Invoke(null, args);
                    string condition = (string)_conditionField.GetValue(logEntry);
                    string stack = (string)_stackTraceField.GetValue(logEntry);
                    int mode = (int)_modeField.GetValue(logEntry);

                    // mode 位掩码：参考 Unity 内部 LogType 与 Flags（这里简单判断）
                    // 参考：1(错误) 2(Assert) 4(脚本错误) 16(Info) 32(Warning) 等
                    LogType type = LogType.Log;
                    if ((mode & 1) != 0 || (mode & 4) != 0) type = LogType.Error;
                    else if ((mode & 2) != 0) type = LogType.Assert;
                    else if ((mode & 16) != 0) type = LogType.Log;
                    else if ((mode & 32) != 0) type = LogType.Warning;

                    if (!visitor(condition, stack, (int)type)) break;
                }
            }
            finally
            {
                _endGettingEntries.Invoke(null, null);
            }
        }
    }
}
#endif