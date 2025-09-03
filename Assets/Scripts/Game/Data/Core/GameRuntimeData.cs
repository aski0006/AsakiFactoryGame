using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Core
{
    public class GameRuntimeData
    {
        private readonly Dictionary<Type, object> _typeMap = new();
        private readonly Dictionary<string, object> _sectionMap = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, IDefinitionRegistry> _definitionRegistries = new();

        #region Registration
        public void Set<T>(T inst) where T : class
        {
            if (inst == null) return;
            _typeMap[inst.GetType()] = inst;
            if (inst is IDefinitionRegistry defReg)
            {
                var defType = defReg.DefinitionType;
                _definitionRegistries[defType] = defReg;
            }
        }

        public void RegisterSection(string sectionName, object inst)
        {
            if (inst == null || string.IsNullOrEmpty(sectionName)) return;
            _sectionMap[sectionName] = inst;
            Set(inst);
        }
        #endregion

        #region Generic Access
        public bool TryGet<T>(out T inst) where T : class
        {
            if (_typeMap.TryGetValue(typeof(T), out var obj) && obj is T cast)
            {
                inst = cast;
                return true;
            }
            foreach (var kv in _typeMap)
            {
                if (typeof(T).IsAssignableFrom(kv.Key) && kv.Value is T cast2)
                {
                    inst = cast2;
                    return true;
                }
            }
            inst = null;
            return false;
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var v)) return v;
            throw new InvalidOperationException($"[GameRuntimeData] 未找到类型 {typeof(T).Name} 实例。");
        }

        public bool TryGetBySection<T>(string sectionName, out T inst) where T : class
        {
            if (_sectionMap.TryGetValue(sectionName, out var obj) && obj is T cast)
            {
                inst = cast;
                return true;
            }
            inst = null;
            return false;
        }

        public object GetBySection(string sectionName)
        {
            if (_sectionMap.TryGetValue(sectionName, out var o)) return o;
            throw new KeyNotFoundException($"[GameRuntimeData] SectionName={sectionName} 未注册。");
        }
        #endregion

        #region Definition (single)
        public bool TryGetDefinition<TDef>(int id, out TDef def) where TDef : class, IDefinition
        {
            def = null;
            if (_definitionRegistries.TryGetValue(typeof(TDef), out var reg))
            {
                if (reg is DefinitionRegistry<TDef> concrete)
                {
                    return concrete.TryGet(id, out def);
                }
                if (reg.TryGetRaw(id, out var raw) && raw is TDef cast)
                {
                    def = cast;
                    return true;
                }
            }
            return false;
        }

        public TDef GetDefinition<TDef>(int id) where TDef : class, IDefinition
        {
            if (TryGetDefinition<TDef>(id, out var def)) return def;
            throw new KeyNotFoundException($"[GameRuntimeData] 未找到定义: {typeof(TDef).Name} Id={id}");
        }
        #endregion

        #region Definition (enumeration)
        public bool TryGetDefinitionRegistry<TDef>(out DefinitionRegistry<TDef> registry) where TDef : class, IDefinition
        {
            registry = null;
            if (_definitionRegistries.TryGetValue(typeof(TDef), out var reg))
            {
                registry = reg as DefinitionRegistry<TDef>;
                return registry != null;
            }
            return false;
        }

        public IEnumerable<TDef> EnumerateDefinitions<TDef>() where TDef : class, IDefinition
        {
            if (_definitionRegistries.TryGetValue(typeof(TDef), out var reg))
            {
                if (reg is DefinitionRegistry<TDef> typed)
                {
                    if (typed.TryGetAll(out var all))
                    {
                        for (int i = 0; i < all.Length; i++)
                            yield return all[i];
                    }
                    yield break;
                }

                if (reg.TryGetAllRaw(out var rawList))
                {
                    for (int i = 0; i < rawList.Count; i++)
                        if (rawList[i] is TDef cast) yield return cast;
                }
                yield break;
            }
            yield break;
        }

        public List<TDef> GetAllDefinitions<TDef>() where TDef : class, IDefinition
        {
            var list = new List<TDef>(32);
            foreach (var d in EnumerateDefinitions<TDef>())
                if (d != null) list.Add(d);
            return list;
        }

        public IEnumerable<Type> EnumerateDefinitionTypes() => _definitionRegistries.Keys;
        #endregion

        #region Diagnostics
        public IEnumerable<object> EnumerateAllInstances() => _typeMap.Values;
        public IEnumerable<KeyValuePair<string, object>> EnumerateSections() => _sectionMap;

        public void LogSummary()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[GameRuntimeData] Registries={_typeMap.Count}, Sections={_sectionMap.Count}, DefinitionTypes={_definitionRegistries.Count}");
#endif
        }
        #endregion
    }
}