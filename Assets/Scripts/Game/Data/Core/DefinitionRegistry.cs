using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 通用定义注册表。可直接使用；若需要扩展可派生。
    /// </summary>
    public class DefinitionRegistry<TDef> : IDefinitionRegistry where TDef : class, IDefinition
    {
        protected readonly Dictionary<int, TDef> _map = new();
        private IReadOnlyList<IDefinition> _rawCache;     // 缓存给接口的原始列表
        private TDef[] _allArrayCache;                    // 强类型数组缓存（可选）

        public IReadOnlyDictionary<int, TDef> Map => _map;
        public virtual Type DefinitionType => typeof(TDef);

        public DefinitionRegistry(IEnumerable<TDef> defs, bool logDuplicate = true)
        {
            if (defs == null) return;
            foreach (var d in defs)
            {
                if (d == null) continue;
                if (_map.ContainsKey(d.Id))
                {
                    if (logDuplicate)
                        Debug.LogError($"[DefinitionRegistry<{typeof(TDef).Name}>] 重复 Id={d.Id}");
                    continue;
                }
                _map[d.Id] = d;
            }
        }

        public bool TryGet(int id, out TDef def) => _map.TryGetValue(id, out def);

        public TDef Get(int id)
        {
            if (_map.TryGetValue(id, out var v)) return v;
            throw new Exception($"[DefinitionRegistry<{typeof(TDef).Name}>] 未找到 Id={id}");
        }

        public bool TryGetRaw(int id, out IDefinition def)
        {
            if (_map.TryGetValue(id, out var v))
            {
                def = v;
                return true;
            }
            def = null;
            return false;
        }

        /// <summary>
        /// 强类型批量获取（复制为数组缓存，后续调用不再分配）。
        /// </summary>
        public bool TryGetAll(out TDef[] all)
        {
            if (_map.Count == 0)
            {
                all = Array.Empty<TDef>();
                return false;
            }
            if (_allArrayCache == null)
            {
                _allArrayCache = new TDef[_map.Count];
                int i = 0;
                foreach (var v in _map.Values)
                    _allArrayCache[i++] = v;
            }
            all = _allArrayCache;
            return true;
        }

        /// <summary>
        /// IDefinitionRegistry 通用批量接口。
        /// 返回只读缓存列表（按需生成一次）。
        /// </summary>
        public bool TryGetAllRaw(out IReadOnlyList<IDefinition> defs)
        {
            if (_map.Count == 0)
            {
                defs = Array.Empty<IDefinition>();
                return false;
            }
            if (_rawCache == null)
            {
                var list = new List<IDefinition>(_map.Count);
                foreach (var v in _map.Values) list.Add(v);
                _rawCache = list;
            }
            defs = _rawCache;
            return true;
        }
    }
}