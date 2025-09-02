
using Core.Debug.FastString;
using UnityEngine;

namespace Game.Core.Debug.FastString
{
    /// <summary>
    /// 可放一些与业务或常见格式相关的便捷扩展，不污染核心类。
    /// </summary>
    public static class FastStringExtensions
    {
        /// <summary>追加时间戳 (HH:mm:ss.fff)</summary>
        public static FastStringBuilder TimeStamp(this FastStringBuilder fs)
        {
            var now = System.DateTime.Now;
            return fs.T(now.ToString("HH:mm:ss.fff")).SP();
        }

        /// <summary>追加帧计数</summary>
        public static FastStringBuilder Frame(this FastStringBuilder fs)
            => fs.T("F=").I(Time.frameCount).SP();

        /// <summary>追加 deltaTime (ms)</summary>
        public static FastStringBuilder Dt(this FastStringBuilder fs, int decimals = 3)
            => fs.T("dt=").F(Time.deltaTime * 1000f, decimals).T("ms").SP();

        /// <summary>追加游戏时间 (since start)</summary>
        public static FastStringBuilder Tm(this FastStringBuilder fs, int decimals = 2)
            => fs.T("t=").F(Time.time, decimals).SP();

        /// <summary>向量若接近整数则用整数显示。</summary>
        public static FastStringBuilder VNice(this FastStringBuilder fs, Vector3 v, float epsilon = 0.0001f)
        {
            float RoundIf(float val)
            {
                var iv = Mathf.Round(val);
                return Mathf.Abs(iv - val) < epsilon ? iv : val;
            }
            v.x = RoundIf(v.x);
            v.y = RoundIf(v.y);
            v.z = RoundIf(v.z);
            return fs.V(v, 3);
        }

        /// <summary>用于范围显示。</summary>
        public static FastStringBuilder Range(this FastStringBuilder fs, float min, float max, int d = 2)
            => fs.C('[').F(min, d).T(" ~ ").F(max, d).C(']');
    }
}
