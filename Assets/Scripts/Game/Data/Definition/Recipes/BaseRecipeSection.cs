using Game.Data.Core;
using Game.ScriptableObjectDB;
using UnityEngine;

namespace Game.Data.Definition.Recipes
{
    [CustomConfig]
    [CreateAssetMenu(fileName = "BaseRecipeSection", menuName = "Game/Definition/Recipes/BaseRecipeSection", order = 0)]
    public partial class BaseRecipeSection : DefinitionSectionBase<BaseRecipeDefinition>
    {
        public override string SectionName => "BaseRecipeSection";
    }
}
