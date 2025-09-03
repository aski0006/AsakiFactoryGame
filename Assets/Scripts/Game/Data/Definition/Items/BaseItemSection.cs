    using Game.Data.Core;
    using Game.ScriptableObjectDB;
    using UnityEngine;

    namespace Game.Data.Definition.Items
    {
        [CustomConfig]
        [CreateAssetMenu(fileName = "BaseItemSection", menuName = "Game/Definition/Items/BaseItemSection", order = 0)]
        public partial class BaseItemSection : DefinitionSectionBase<BaseItemDefinition>
        {
            public override string SectionName => "BaseItemSection";
        }
    }
