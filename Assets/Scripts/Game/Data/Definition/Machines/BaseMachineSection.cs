using Game.Data.Core;
using Game.ScriptableObjectDB;
using UnityEngine;

namespace Game.Data.Definition.Machines
{
    [CustomConfig]
    [CreateAssetMenu(fileName = "BaseMachineSection", menuName = "Game/Definition/Machines/BaseMachineSection", order = 0)]
    public partial class BaseMachineSection : DefinitionSectionBase<BaseMachineDefinition>
    {
        override public string SectionName => "BaseMachineSection";
    }
}
