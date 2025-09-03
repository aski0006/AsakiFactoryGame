using Game.Data;
using Game.Data.Definition.Items;
using Game.Data.Definition.Machines;
using Game.Data.Definition.Recipes;
using Game.Data.Generated;
using UnityEngine;

// 生成的常量命名空间

namespace Demo
{
    public class QuickDataSmokeTest : MonoBehaviour
    {
        void Start()
        {
            var ctx = GameContext.Instance;

            var wood = ctx.GetDefinition<BaseItemDefinition>(ConfigIds.BaseItemDefinition.Wood);
            Debug.Log($"[Smoke] Wood -> MaxStack={wood.MaxStack}");

            var recipe = ctx.GetDefinition<BaseRecipeDefinition>(ConfigIds.BaseRecipeDefinition.WoodToWoodPlank);
            Debug.Log($"[Smoke] Recipe {recipe.CodeName} Inputs={recipe.Inputs.Length} Time={recipe.TimeSeconds}");

            var furnace = ctx.GetDefinition<BaseMachineDefinition>(ConfigIds.BaseMachineDefinition.Furnace);
            Debug.Log($"[Smoke] Machine {furnace.CodeName} Size={furnace.Size}");
        }
    }
}
