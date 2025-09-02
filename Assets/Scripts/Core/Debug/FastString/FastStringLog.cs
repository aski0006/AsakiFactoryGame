using System.Diagnostics;
using UnityEngine;

namespace Game.Core.Debug.FastString
{
    /// <summary>
    /// 一些快捷静态方法：避免手写 Acquire()。
    /// </summary>
    public static class FastStringLog
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(object a, object b = null, object c = null)
        {
            using var fs = FastString.Acquire().Tag();
            fs.Obj(a);
            if (b != null) fs.SP().Obj(b);
            if (c != null) fs.SP().Obj(c);
            fs.Log();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(object msg)
        {
            using var fs = FastString.Acquire().WarnTag().Obj(msg);
            fs.LogWarning();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object msg)
        {
            using var fs = FastString.Acquire().ErrTag().Obj(msg);
            fs.LogError();
        }
    }
}
