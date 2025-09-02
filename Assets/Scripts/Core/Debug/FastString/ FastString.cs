#if UNITY_2021_3_OR_NEWER
#define FASTSTRING_SPAN
#endif

using Core.Debug.FastString;
using System.Diagnostics;

namespace Game.Core.Debug.FastString
{
    public static class FastString
    {
        public const int DefaultCapacity = 256;
        public const int MaxRetainCapacity = 16 * 1024;

        [Conditional("FAST_STRING_ENABLE"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warmup(int count = 4)
        {
            for (int i = 0; i < count; i++)
            {
                var b = FastStringBuilderPool.Get(DefaultCapacity);
                FastStringBuilderPool.Release(b);
            }
        }

        public static FastStringBuilder Acquire(int capacity = DefaultCapacity)
        {
#if FAST_STRING_DISABLE
            return new FastStringBuilder(null, false);
#else
            var core = FastStringBuilderPool.Get(capacity);
            return new FastStringBuilder(core, true);
#endif
        }
    }
}
