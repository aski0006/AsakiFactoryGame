using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 泛型 Section 基类：持有一组定义，Build 时创建 DefinitionRegistry。
    /// 若需要自定义 Registry 类型，可重写 CreateRegistry。
    /// </summary>
    public abstract class DefinitionSectionBase<TDef> : ScriptableObject, IContextSection
        where TDef : class, IDefinition
    {
        [Serializable]
        public class DefinitionItem
        {
            public string description;
            public TDef definition;
        }

        [SerializeField] protected List<DefinitionItem> definitions = new();
        public virtual string SectionName => GetType().Name;

        protected virtual IDefinitionRegistry CreateRegistry()
        {
            return new DefinitionRegistry<TDef>(definitions.Select(x => x.definition));
        }


        public virtual void Build(GameRuntimeData data)
        {
            var registry = CreateRegistry();
            data.RegisterSection(SectionName, registry);
        }

#if UNITY_EDITOR
        public IReadOnlyCollection<DefinitionItem> Editor_Definitions => definitions;
#endif
    }
}
