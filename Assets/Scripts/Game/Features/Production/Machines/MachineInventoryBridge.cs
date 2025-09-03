using Game.Data;
using Game.Data.Definition.Items;
using Game.Data.Definition.Recipes;
using Game.Features.Production.Inventory;

namespace Game.Features.Production.Machines
{
    public static class MachineInventoryBridge
    {
        public static bool InventoryHasAllInputs(InventoryComponent inv, BaseRecipeDefinition recipe)
        {
            foreach (var inp in recipe.Inputs)
            {
                if (inv.TotalCount(inp.ItemId) < inp.Count)
                    return false;
            }
            return true;
        }

        public static bool TryMoveInputs(InventoryComponent inv, MachineRuntimeState machine, BaseRecipeDefinition recipe)
        {
            if (!InventoryHasAllInputs(inv, recipe))
                return false;

            foreach (var inp in recipe.Inputs)
            {
                inv.TryConsume(inp.ItemId, inp.Count);
                MergeIntoMachineInput(machine, inp.ItemId, inp.Count);
            }
            MachineRuntimeDirty.Mark();
            return true;
        }

        public static bool TryRefillMissing(InventoryComponent inv, MachineRuntimeState machine, BaseRecipeDefinition recipe)
        {
            // 预检查
            foreach (var inp in recipe.Inputs)
            {
                int have = 0;
                for (int i = 0; i < machine.InputBuffer.Count; i++)
                    if (machine.InputBuffer[i].itemId == inp.ItemId)
                        have += machine.InputBuffer[i].count;

                int needRemain = inp.Count - have;
                if (needRemain <= 0) continue;

                if (inv.TotalCount(inp.ItemId) < needRemain)
                    return false;
            }

            // 扣除缺口
            bool changed = false;
            foreach (var inp in recipe.Inputs)
            {
                int have = 0;
                for (int i = 0; i < machine.InputBuffer.Count; i++)
                    if (machine.InputBuffer[i].itemId == inp.ItemId)
                        have += machine.InputBuffer[i].count;

                int needRemain = inp.Count - have;
                if (needRemain <= 0) continue;

                inv.TryConsume(inp.ItemId, needRemain);
                MergeIntoMachineInput(machine, inp.ItemId, needRemain);
                changed = true;
            }

            if (changed) MachineRuntimeDirty.Mark();
            return true;
        }

        private static void MergeIntoMachineInput(MachineRuntimeState machine, int itemId, int count)
        {
            for (int i = 0; i < machine.InputBuffer.Count; i++)
            {
                if (machine.InputBuffer[i].itemId == itemId)
                {
                    var tup = machine.InputBuffer[i];
                    tup.count += count;
                    machine.InputBuffer[i] = tup;
                    return;
                }
            }
            machine.InputBuffer.Add((itemId, count));
        }

        public static string FormatRecipeIO(BaseRecipeDefinition recipe)
        {
            var ctx = GameContext.Instance;
            System.Text.StringBuilder sb = new();
            sb.Append("In: ");
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var st = recipe.Inputs[i];
                var idef = ctx.GetDefinition<BaseItemDefinition>(st.ItemId);
                if (i > 0) sb.Append(", ");
                sb.Append($"{idef.DisplayName} x{st.Count}");
            }
            sb.Append("  ->  Out: ");
            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                var st = recipe.Outputs[i];
                var idef = ctx.GetDefinition<BaseItemDefinition>(st.ItemId);
                if (i > 0) sb.Append(", ");
                sb.Append($"{idef.DisplayName} x{st.Count}");
            }
            return sb.ToString();
        }
    }
}