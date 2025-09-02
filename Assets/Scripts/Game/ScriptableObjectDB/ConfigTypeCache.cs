using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Game.ScriptableObjectDB
{
    /// <summary>
    /// 反射扫描带 [CustomConfig] 的 ScriptableObject 子类并缓存。
    /// </summary>
    public static class ConfigTypeCache
    {
        public class TypeInfo
        {
            public Type type;
            public string displayName;
            public string fullName;
        }

        private static double _lastScanTime;
        private static List<TypeInfo> _types;
        private const double RescanInterval = 10; // 秒

        public static IReadOnlyList<TypeInfo> GetTypes(bool forceRescan = false)
        {
            if (_types == null || forceRescan || (EditorApplication.timeSinceStartup - _lastScanTime) > RescanInterval)
                Scan();
            return _types;
        }

        private static bool HasCustomConfigAttribute(Type t)
        {
            return t.GetCustomAttribute<CustomConfig>() != null;
        }

        private static void Scan()
        {
            _lastScanTime = EditorApplication.timeSinceStartup;
            var result = new List<TypeInfo>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract) continue;
                    if (!typeof(ScriptableObject).IsAssignableFrom(t)) continue;
                    if (!HasCustomConfigAttribute(t)) continue;
                    result.Add(new TypeInfo
                    {
                        type = t,
                        displayName = t.Name,
                        fullName = t.FullName
                    });
                }
            }
            _types = result.OrderBy(t => t.displayName).ToList();
        }
    }
}
