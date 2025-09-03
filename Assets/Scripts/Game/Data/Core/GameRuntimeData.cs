using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 动态运行时数据容器（无硬编码字段）。
    /// - Type -> 实例
    /// - SectionName -> 实例
    /// - DefinitionType -> IDefinitionRegistry
    /// </summary>
    public class GameRuntimeData
    {
        private readonly Dictionary<Type, object> _typeMap = new();
        private readonly Dictionary<string, object> _sectionMap = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, IDefinitionRegistry> _definitionRegistries = new();

        #region Registration

        /// <summary>
        /// 注册任意实例（按其运行时实际类型）。
        /// </summary>
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

        /// <summary>
        /// Section 构建时调用：同时按类型与 SectionName 注册。
        /// </summary>
        public void RegisterSection(string sectionName, object inst)
        {
            if (inst == null || string.IsNullOrEmpty(sectionName)) return;
            _sectionMap[sectionName] = inst;
            Set(inst); // 复用 Set 逻辑
        }

        #endregion

        #region Generic Access

        public bool TryGet<T>(out T inst) where T : class
        {
            // 优先精确类型
            if (_typeMap.TryGetValue(typeof(T), out var obj) && obj is T cast)
            {
                inst = cast;
                return true;
            }

            // 再尝试从实例集合里找可赋值类型（支持接口）
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

        #region Definition Access

        public bool TryGetDefinition<TDef>(int id, out TDef def) where TDef : class, IDefinition
        {
            def = null;
            if (_definitionRegistries.TryGetValue(typeof(TDef), out var reg))
            {
                if (reg is DefinitionRegistry<TDef> concrete)
                {
                    return concrete.TryGet(id, out def);
                }
                // 如果是自定义实现
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

        #region Diagnostics

        public IEnumerable<object> EnumerateAllInstances() => _typeMap.Values;
        public IEnumerable<KeyValuePair<string, object>> EnumerateSections() => _sectionMap;
        public IEnumerable<Type> EnumerateDefinitionTypes() => _definitionRegistries.Keys;

        public void LogSummary()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var fs = $"[GameRuntimeData] Registries={_typeMap.Count}, Sections={_sectionMap.Count}, DefinitionTypes={_definitionRegistries.Count}";
            Debug.Log(fs);
#endif
        }

        #endregion
    }
}