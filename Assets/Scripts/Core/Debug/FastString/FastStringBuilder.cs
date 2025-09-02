using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Core.Debug.FastString
{
    /// <summary>
    /// 面向用户的 Fluent API (IDisposable)。
    /// 内部包装 CorePooledBuilder。调用 Dispose() 自动回收。
    /// </summary>
    public readonly struct FastStringBuilder : IDisposable
    {
        private readonly CorePooledBuilder _core;
        private readonly bool _pooled;

        internal FastStringBuilder(CorePooledBuilder core, bool pooled)
        {
            _core = core;
            _pooled = pooled;
        }

        public bool IsValid => _core != null;

        public void Dispose()
        {
            if (_pooled && _core != null)
            {
                FastStringBuilderPool.Release(_core);
            }
        }

        #region 基础 Append

        public FastStringBuilder T(string text) { _core?.SB.Append(text); return this; } // Text
        public FastStringBuilder S(string text) { _core?.SB.Append(text); return this; } // String alias
        public FastStringBuilder C(char c) { _core?.SB.Append(c); return this; }
        public FastStringBuilder NL() { _core?.SB.Append('\n'); return this; }
        public FastStringBuilder SP() { _core?.SB.Append(' '); return this; }
        public FastStringBuilder Tab() { _core?.SB.Append('\t'); return this; }

        public FastStringBuilder I(int v) { _core?.SB.Append(v); return this; }
        public FastStringBuilder L(long v) { _core?.SB.Append(v); return this; }
        public FastStringBuilder U(uint v) { _core?.SB.Append(v); return this; }
        public FastStringBuilder UL(ulong v) { _core?.SB.Append(v); return this; }
        public FastStringBuilder F(float v, int decimals = -1)
        {
            if (_core == null) return this;
            if (decimals >= 0) _core.SB.Append(v.ToString("F" + decimals));
            else _core.SB.Append(v);
            return this;
        }
        public FastStringBuilder D(double v, int decimals = -1)
        {
            if (_core == null) return this;
            if (decimals >= 0) _core.SB.Append(v.ToString("F" + decimals));
            else _core.SB.Append(v);
            return this;
        }
        public FastStringBuilder B(bool v) { _core?.SB.Append(v ? "true" : "false"); return this; }
        public FastStringBuilder Obj(object o) { _core?.SB.Append(o); return this; }

        #endregion

        #region Unity 常用结构

        public FastStringBuilder V(Vector2 v, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append('(').Append(v.x.ToString("F" + d)).Append(',').Append(v.y.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder V(Vector3 v, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append('(')
                .Append(v.x.ToString("F" + d)).Append(',')
                .Append(v.y.ToString("F" + d)).Append(',')
                .Append(v.z.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder V(Vector4 v, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append('(')
                .Append(v.x.ToString("F" + d)).Append(',')
                .Append(v.y.ToString("F" + d)).Append(',')
                .Append(v.z.ToString("F" + d)).Append(',')
                .Append(v.w.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder Q(Quaternion q, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append("Quat(")
                .Append(q.x.ToString("F" + d)).Append(',')
                .Append(q.y.ToString("F" + d)).Append(',')
                .Append(q.z.ToString("F" + d)).Append(',')
                .Append(q.w.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder Col(Color c, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append("RGBA(")
                .Append(c.r.ToString("F" + d)).Append(',')
                .Append(c.g.ToString("F" + d)).Append(',')
                .Append(c.b.ToString("F" + d)).Append(',')
                .Append(c.a.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder R(Rect r, int d = 2)
        {
            if (_core == null) return this;
            _core.SB.Append("Rect(")
                .Append(r.x.ToString("F" + d)).Append(',')
                .Append(r.y.ToString("F" + d)).Append(',')
                .Append(r.width.ToString("F" + d)).Append(',')
                .Append(r.height.ToString("F" + d)).Append(')');
            return this;
        }
        public FastStringBuilder Bounds(Bounds b, int d = 2)
        {
            if (_core == null) return this;
            V(b.center, d); _core.SB.Append("~"); V(b.extents, d);
            return this;
        }

        #endregion

        #region 集合 / 枚举

        public FastStringBuilder J<T>(IEnumerable<T> enumerable, string sep = ",")
        {
            if (_core == null || enumerable == null) return this;
            bool first = true;
            foreach (var e in enumerable)
            {
                if (!first) _core.SB.Append(sep);
                _core.SB.Append(e);
                first = false;
            }
            return this;
        }

        public FastStringBuilder Arr<T>(T[] arr, string sep = ",")
        {
            if (_core == null)
                return this;
            if (arr == null) { _core.SB.Append("null"); return this; }
            _core.SB.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) _core.SB.Append(sep);
                _core.SB.Append(arr[i]);
            }
            _core.SB.Append(']');
            return this;
        }

        #endregion

        #region 条件追加 & 富文本

        public FastStringBuilder If(bool condition, Func<FastStringBuilder, FastStringBuilder> writer)
        {
            if (condition && writer != null) return writer(this);
            return this;
        }

        public FastStringBuilder Color(string text, Color color, bool condition = true)
        {
            if (!condition || _core == null) return this;
            _core.SB.Append("<color=#");
            _core.SB.Append(ColorUtility.ToHtmlStringRGBA(color));
            _core.SB.Append('>').Append(text).Append("</color>");
            return this;
        }

        public FastStringBuilder Bold(string text, bool condition = true)
        {
            if (!condition || _core == null) return this;
            _core.SB.Append("<b>").Append(text).Append("</b>");
            return this;
        }

        public FastStringBuilder Italic(string text, bool condition = true)
        {
            if (!condition || _core == null) return this;
            _core.SB.Append("<i>").Append(text).Append("</i>");
            return this;
        }

        public FastStringBuilder Tag(string tag = "DBG")
        {
            if (_core == null) return this;
            _core.SB.Append('[').Append(tag).Append(']').Append(' ');
            return this;
        }
        public FastStringBuilder WarnTag() => Color("[WARN] ", new Color(1f, .6f, 0f));
        public FastStringBuilder ErrTag() => Color("[ERR] ", new Color(1f, 0.5f, 0.5f));

        #endregion

        #region 输出 & 获取字符串

        public string ToStringValue() => _core?.SB.ToString() ?? string.Empty;

        public string ToStringAndRelease()
        {
            var s = ToStringValue();
            Dispose();
            return s;
        }

        public FastStringBuilder Clear()
        {
            _core?.SB.Clear();
            return this;
        }

        #endregion

        #region 日志

        public FastStringBuilder Log(UnityEngine.Object context = null, bool release = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Debug.Log(ToStringValue(), context);
#endif
            if (release) Dispose();
            return this;
        }

        public FastStringBuilder LogWarning(UnityEngine.Object context = null, bool release = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Debug.LogWarning(ToStringValue(), context);
#endif
            if (release) Dispose();
            return this;
        }

        public FastStringBuilder LogError(UnityEngine.Object context = null, bool release = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Debug.LogError(ToStringValue(), context);
#endif
            if (release) Dispose();
            return this;
        }

        public void LogAndRelease(UnityEngine.Object ctx = null) => Log(ctx, true);
        public void LogWarningAndRelease(UnityEngine.Object ctx = null) => LogWarning(ctx, true);
        public void LogErrorAndRelease(UnityEngine.Object ctx = null) => LogError(ctx, true);

        #endregion
    }

    /// <summary>内部包装真正的 StringBuilder。</summary>
    internal sealed class CorePooledBuilder
    {
        public readonly StringBuilder SB;

        public CorePooledBuilder(int capacity)
        {
            SB = new StringBuilder(capacity);
        }
    }

    internal static class FastStringBuilderPool
    {
        // 简易池（主线程使用足够）。可换 ConcurrentStack 用于多线程。
        private static readonly Stack<CorePooledBuilder> Pool = new(32);

        public static CorePooledBuilder Get(int capacity)
        {
            lock (Pool)
            {
                while (Pool.Count > 0)
                {
                    var b = Pool.Pop();
                    if (b.SB.Capacity >= capacity)
                    {
                        b.SB.Clear();
                        return b;
                    }
                }
            }
            return new CorePooledBuilder(capacity);
        }

        public static void Release(CorePooledBuilder b)
        {
            if (b == null) return;
            if (b.SB.Capacity > Game.Core.Debug.FastString.FastString.MaxRetainCapacity) return; // 太大不回池
            b.SB.Clear();
            lock (Pool)
            {
                Pool.Push(b);
            }
        }
    }
}