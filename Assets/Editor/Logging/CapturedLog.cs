#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Editor.Logging
{
    [Serializable]
    public class CapturedLog
    {
        public DateTime Time;
        public string Condition;
        public string StackTrace;
        public LogType Type;
        public int Count; // Collapse 次数
        public string FirstLineCached;

        public string ShortCategory =>
            Type switch
            {
                LogType.Warning => "W",
                LogType.Error => "E",
                LogType.Assert => "A",
                LogType.Exception => "EX",
                _ => "I"
            };
    }

    /// <summary>
    /// 日志捕获缓冲：线程安全入队 -> 主线程刷新。
    /// </summary>
    public class LogCaptureBuffer
    {
        private readonly List<CapturedLog> _logs = new();
        private readonly ConcurrentQueue<CapturedLog> _incoming = new();
        private bool _collapse;

        public IReadOnlyList<CapturedLog> Logs => _logs;
        public bool Collapse
        {
            get => _collapse;
            set => _collapse = value;
        }

        public void Enqueue(CapturedLog log)
        {
            _incoming.Enqueue(log);
        }

        public void FlushIncoming()
        {
            while (_incoming.TryDequeue(out var log))
            {
                if (_collapse && _logs.Count > 0)
                {
                    var last = _logs[_logs.Count - 1];
                    if (last.Type == log.Type && last.Condition == log.Condition && last.StackTrace == log.StackTrace)
                    {
                        last.Count++;
                        continue;
                    }
                }
                log.Count = 1;
                log.FirstLineCached = FirstLine(log.Condition);
                _logs.Add(log);
            }
        }

        public void Clear()
        {
            _logs.Clear();
            while (_incoming.TryDequeue(out _)) { }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int idx = s.IndexOf('\n');
            return idx >= 0 ? s[..idx] : s;
        }
    }
}
#endif