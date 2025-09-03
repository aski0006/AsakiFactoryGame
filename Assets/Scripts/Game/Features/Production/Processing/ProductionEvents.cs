using System;
using Game.Features.Production.Machines;
using Game.Data.Definition.Recipes;

namespace Game.Features.Production.Processing
{
    /// <summary>
    /// 生产相关事件（后续可换成事件总线）。
    /// </summary>
    public static class ProductionEvents
    {
        public static Action<MachineRuntimeState, BaseRecipeDefinition> OnRecipeStarted;
        public static Action<MachineRuntimeState, BaseRecipeDefinition> OnRecipeCompleted;
        public static Action<MachineRuntimeState, BaseRecipeDefinition> OnOutputBlocked;
    }
}
