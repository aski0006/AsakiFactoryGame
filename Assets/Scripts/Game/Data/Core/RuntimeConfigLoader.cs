using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.Data.Core
{
    /// <summary>
    /// 只做一件事：反射找到所有 IContextSection 类型，加载全部资产实例。
    /// </summary>
    public static class RuntimeConfigLoader
    {
        private static List<Type> _sectionTypes;

        public static IReadOnlyList<Type> GetSectionTypes(bool forceRescan = false)
        {
            if (_sectionTypes == null || forceRescan)
            {
                _sectionTypes = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    })
                    .Where(t =>
                        t is { IsAbstract: false, IsGenericType: false } &&
                        typeof(ScriptableObject).IsAssignableFrom(t) &&
                        typeof(IContextSection).IsAssignableFrom(t))
                    .ToList();
            }
            return _sectionTypes;
        }

        public static List<IContextSection> LoadAllSectionsOfType(Type sectionType, bool verbose)
        {
            var list = new List<IContextSection>();

#if UNITY_EDITOR
            // Editor: 精确查找项目中所有资产
            var guids = AssetDatabase.FindAssets("t:" + sectionType.Name);
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath(path, sectionType) as ScriptableObject;
                if (asset is IContextSection s) list.Add(s);
            }
#else
            // Player: 约定放在 Resources/Config/
            var loaded = Resources.LoadAll("Config", sectionType);
            foreach (var obj in loaded)
                if (obj is IContextSection s) list.Add(s);
#endif

            if (verbose)
                Debug.Log($"[RuntimeConfigLoader] {sectionType.Name} 资产数={list.Count}");

            return list;
        }
    }
}