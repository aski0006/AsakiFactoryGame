using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game.CSV
{
    /// <summary>
    /// 自定义类型 CSV 转换器注册表。
    /// 支持懒加载自动扫描：首次使用时寻找所有 ICsvTypeConverter 实现并注册。
    /// </summary>
    public static class CsvTypeConverterRegistry
    {
        private static readonly Dictionary<Type, ICsvTypeConverter> _map = new();
        private static bool _scanned;
        private static readonly object _lock = new();

        public static void Register(ICsvTypeConverter converter, bool overwrite = false)
        {
            if (converter == null) return;
            lock (_lock)
            {
                if (_map.ContainsKey(converter.TargetType))
                {
                    if (overwrite) _map[converter.TargetType] = converter;
                }
                else
                {
                    _map.Add(converter.TargetType, converter);
                }
            }
        }

        private static void EnsureScanned()
        {
            if (_scanned) return;
            lock (_lock)
            {
                if (_scanned) return;
                try
                {
                    var asms = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in asms)
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch { continue; }

                        foreach (var t in types)
                        {
                            if (t == null || t.IsAbstract || t.IsInterface) continue;
                            if (!typeof(ICsvTypeConverter).IsAssignableFrom(t)) continue;
                            if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                            try
                            {
                                var inst = (ICsvTypeConverter)Activator.CreateInstance(t);
                                Register(inst);
                            }
                            catch { /* 忽略单个失败 */ }
                        }
                    }
                }
                finally
                {
                    _scanned = true;
                }
            }
        }

        public static bool TrySerialize(Type t, object value, out string text)
        {
            EnsureScanned();
            text = string.Empty;
            if (value == null) return true;
            if (_map.TryGetValue(t, out var conv))
            {
                text = conv.Serialize(value) ?? string.Empty;
                return true;
            }
            return false;
        }

        public static bool TryDeserialize(Type t, string text, out object value)
        {
            EnsureScanned();
            if (_map.TryGetValue(t, out var conv))
            {
                return conv.TryDeserialize(text, out value);
            }
            value = null;
            return false;
        }
    }
}