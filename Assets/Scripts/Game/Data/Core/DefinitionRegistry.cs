using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 通用定义注册表。可直接使用；若需要扩展方法，可派生。
    /// </summary>
    public class DefinitionRegistry<TDef> : IDefinitionRegistry where TDef : class, IDefinition
    {
        protected readonly Dictionary<int, TDef> _map = new();
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
    }
}
