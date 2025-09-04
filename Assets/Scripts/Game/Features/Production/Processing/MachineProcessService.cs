using System;
using Game.Data;
using Game.Data.Definition.Recipes;
using Game.Features.Production.Machines;
using Game.Features.Production.Inventory;

namespace Game.Features.Production.Processing
{
    /// <summary>
    /// 生产核心逻辑（加入：取消/切换配方返还未完成输入）。
    /// </summary>
    public class MachineProcessService
    {
        #region Public API

        /// <summary>
        /// 尝试启动（或切换到）指定配方。
        /// - 若同配方已在运行：根据 resetProgress 仅在 Idle 时重置；Processing 中不做破坏性操作。
        /// - 若不同配方，且当前在 Processing 未完成 -> 返还旧配方消耗的输入，再切换。
        /// - 若当前已完成但输出未取走 (OutputReady/BlockedOutput) -> 默认禁止切换（防止白嫖），返回 false。
        /// </summary>
        public bool TryStart(MachineRuntimeState machine, int recipeId, bool resetProgress = true)
        {
            if (machine == null) return false;

            // 同配方
            if (machine.ActiveRecipeId == recipeId)
            {
                if (machine.Phase == MachinePhase.Idle && resetProgress)
                {
                    machine.Progress = 0f;
                    machine.Phase = MachinePhase.PendingInput;
                    MachineRuntimeDirty.Mark();
                }
                // 其他阶段不重置，避免无意清进度
                return true;
            }

            // 不同配方：根据当前阶段决定能否切换 / 是否先返还资源
            if (machine.ActiveRecipeId != null)
            {
                var currentRecipe = SafeGetRecipe(machine.ActiveRecipeId.Value);
                if (currentRecipe != null)
                {
                    switch (machine.Phase)
                    {
                        case MachinePhase.Processing:
                            // 未完成产出，返还输入
                            RefundInputs(machine, currentRecipe);
                            break;

                        case MachinePhase.PendingInput:
                        case MachinePhase.BlockedInput:
                            // 尚未消耗输入（或缺料），直接切换即可
                            break;

                        case MachinePhase.OutputReady:
                        case MachinePhase.BlockedOutput:
                            // 已经完成产出但尚未取走，禁止直接切换（避免产出+返还双重收益）
                            return false;

                        case MachinePhase.Idle:
                            // 可直接切换
                            break;
                    }
                }
            }

            machine.ActiveRecipeId = recipeId;
            if (resetProgress) machine.Progress = 0f;
            machine.Phase = MachinePhase.PendingInput;
            MachineRuntimeDirty.Mark();
            return true;
        }

        /// <summary>
        /// 停止当前配方（可选择是否返还已消耗输入）。
        /// - refundInputs=true 且处于未完成生产 (Processing) 时返还材料
        /// - PendingInput/BlockedInput：尚未扣料，无需返还
        /// - OutputReady/BlockedOutput：默认不返还（已经完成产出）
        /// </summary>
        public void Stop(MachineRuntimeState machine, bool refundInputs = true)
        {
            if (machine == null) return;
            if (machine.ActiveRecipeId == null)
            {
                machine.Phase = MachinePhase.Idle;
                machine.Progress = 0f;
                return;
            }

            if (refundInputs)
            {
                if (machine.Phase == MachinePhase.Processing)
                {
                    var recipe = SafeGetRecipe(machine.ActiveRecipeId.Value);
                    if (recipe != null && machine.Progress < recipe.TimeSeconds)
                    {
                        RefundInputs(machine, recipe);
                    }
                }
                else if (machine.Phase == MachinePhase.PendingInput || machine.Phase == MachinePhase.BlockedInput)
                {
                    // 输入仍在 InputBuffer 中，不需要额外操作
                }
                // OutputReady / BlockedOutput：不返还
            }

            machine.ActiveRecipeId = null;
            machine.Progress = 0f;
            machine.Phase = MachinePhase.Idle;
            MachineRuntimeDirty.Mark();
        }

        /// <summary>
        /// 推进生产（固定时间步长）。
        /// </summary>
        public void Advance(MachineRuntimeState machine, float deltaSeconds, bool continuousProduction = true)
        {
            if (machine == null || deltaSeconds <= 0f) return;

            if (machine.ActiveRecipeId == null)
            {
                if (machine.Phase != MachinePhase.Idle)
                {
                    machine.Phase = MachinePhase.Idle;
                    MachineRuntimeDirty.Mark();
                }
                return;
            }

            var recipe = SafeGetRecipe(machine.ActiveRecipeId.Value);
            if (recipe == null)
            {
                machine.ActiveRecipeId = null;
                machine.Progress = 0f;
                machine.Phase = MachinePhase.Idle;
                MachineRuntimeDirty.Mark();
                return;
            }

            // 进入生产阶段前处理输入
            switch (machine.Phase)
            {
                case MachinePhase.PendingInput:
                    if (!HasAllInputs(machine, recipe))
                    {
                        machine.Phase = MachinePhase.BlockedInput;
                        return;
                    }
                    ConsumeInputs(machine, recipe);
                    machine.Progress = 0f;
                    machine.Phase = MachinePhase.Processing;
                    ProductionEvents.OnRecipeStarted?.Invoke(machine, recipe);
                    MachineRuntimeDirty.Mark();
                    break;

                case MachinePhase.BlockedInput:
                    if (HasAllInputs(machine, recipe))
                    {
                        ConsumeInputs(machine, recipe);
                        machine.Progress = 0f;
                        machine.Phase = MachinePhase.Processing;
                        ProductionEvents.OnRecipeStarted?.Invoke(machine, recipe);
                        MachineRuntimeDirty.Mark();
                    }
                    return;
            }

            if (machine.Phase == MachinePhase.Processing)
            {
                machine.Progress += deltaSeconds * machine.Definition.ProcessingSpeedMultiplier;

                if (machine.Progress >= recipe.TimeSeconds)
                {
                    if (!TryPlaceOutputs(machine, recipe))
                    {
                        machine.Progress = recipe.TimeSeconds;
                        machine.Phase = MachinePhase.BlockedOutput;
                        ProductionEvents.OnOutputBlocked?.Invoke(machine, recipe);
                        return;
                    }

                    machine.CompletedCycles++;
                    ProductionEvents.OnRecipeCompleted?.Invoke(machine, recipe);

                    machine.Progress = 0f;
                    machine.Phase = continuousProduction ? MachinePhase.PendingInput : MachinePhase.OutputReady;
                    MachineRuntimeDirty.Mark();
                }
            }
            else if (machine.Phase == MachinePhase.BlockedOutput)
            {
                // 保持：等待外部清理输出
            }
        }

        public int TryCollectOutputs(MachineRuntimeState machine, InventoryComponent targetInventory)
        {
            if (machine == null || targetInventory == null) return 0;
            if (machine.OutputBuffer.Count == 0) return 0;

            int movedStacks = 0;
            for (int i = machine.OutputBuffer.Count - 1; i >= 0; i--)
            {
                var slot = machine.OutputBuffer[i];
                if (slot.itemId <= 0 || slot.count <= 0) continue;

                int before = targetInventory.TotalCount(slot.itemId);
                int accepted = targetInventory.AddItem(slot.itemId, slot.count);
                if (accepted <= 0) continue;

                int after = targetInventory.TotalCount(slot.itemId);
                int delta = after - before;
                if (delta >= slot.count)
                {
                    machine.OutputBuffer.RemoveAt(i);
                    movedStacks++;
                }
                else
                {
                    slot.count -= delta;
                    machine.OutputBuffer[i] = slot;
                    movedStacks++;
                }
            }

            if (movedStacks > 0)
            {
                if (machine.Phase == MachinePhase.BlockedOutput && machine.ActiveRecipeId != null)
                {
                    machine.Phase = MachinePhase.PendingInput;
                }
                MachineRuntimeDirty.Mark();
            }

            return movedStacks;
        }

        public bool TryRefillInputs(MachineRuntimeState machine, InventoryComponent sourceInventory)
        {
            if (machine == null || sourceInventory == null) return false;
            if (machine.ActiveRecipeId == null) return false;

            var recipe = SafeGetRecipe(machine.ActiveRecipeId.Value);
            if (recipe == null) return false;

            bool changed = false;
            foreach (var inp in recipe.Inputs)
            {
                int currentInBuffer = GetBufferCount(machine.InputBuffer, inp.ItemId);
                int missing = inp.Count - currentInBuffer;
                if (missing <= 0) continue;
                if (sourceInventory.TotalCount(inp.ItemId) < missing)
                    return false;
            }

            foreach (var inp in recipe.Inputs)
            {
                int currentInBuffer = GetBufferCount(machine.InputBuffer, inp.ItemId);
                int missing = inp.Count - currentInBuffer;
                if (missing <= 0) continue;

                if (!sourceInventory.TryConsume(inp.ItemId, missing))
                    return false;

                MergeIntoBuffer(machine.InputBuffer, inp.ItemId, missing);
                changed = true;
            }

            if (changed)
            {
                MachineRuntimeDirty.Mark();
                if (machine.Phase == MachinePhase.BlockedInput)
                    machine.Phase = MachinePhase.PendingInput;
            }

            return true;
        }

        public void RebuildStateAfterLoad(MachineRuntimeState machine)
        {
            if (machine == null) return;

            if (machine.ActiveRecipeId == null)
            {
                machine.Phase = MachinePhase.Idle;
                machine.Progress = 0f;
                return;
            }

            var recipe = SafeGetRecipe(machine.ActiveRecipeId.Value);
            if (recipe == null)
            {
                machine.ActiveRecipeId = null;
                machine.Progress = 0f;
                machine.Phase = MachinePhase.Idle;
                return;
            }

            if (machine.Progress < 0f) machine.Progress = 0f;
            if (machine.Progress > recipe.TimeSeconds)
                machine.Progress = recipe.TimeSeconds;

            if (machine.Progress <= 0f)
                machine.Phase = MachinePhase.PendingInput;
            else if (machine.Progress < recipe.TimeSeconds)
                machine.Phase = MachinePhase.Processing;
            else
                machine.Phase = MachinePhase.OutputReady;
        }

        #endregion

        #region Internal Helpers

        private BaseRecipeDefinition SafeGetRecipe(int recipeId)
        {
            try { return GameContext.Instance.GetDefinition<BaseRecipeDefinition>(recipeId); }
            catch { return null; }
        }

        private bool HasAllInputs(MachineRuntimeState machine, BaseRecipeDefinition recipe)
        {
            var inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                var req = inputs[i];
                int have = GetBufferCount(machine.InputBuffer, req.ItemId);
                if (have < req.Count) return false;
            }
            return true;
        }

        private void ConsumeInputs(MachineRuntimeState machine, BaseRecipeDefinition recipe)
        {
            var inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                var req = inputs[i];
                int remain = req.Count;
                for (int b = 0; b < machine.InputBuffer.Count && remain > 0; b++)
                {
                    var slot = machine.InputBuffer[b];
                    if (slot.itemId != req.ItemId) continue;
                    int take = Math.Min(slot.count, remain);
                    slot.count -= take;
                    remain -= take;
                    if (slot.count <= 0)
                    {
                        machine.InputBuffer.RemoveAt(b);
                        b--;
                    }
                    else
                    {
                        machine.InputBuffer[b] = slot;
                    }
                }
            }
            MachineRuntimeDirty.Mark();
        }

        /// <summary>
        /// 返还旧配方已消耗的输入（全额）。
        /// 当前实现：直接将 recipe.Inputs 加回 InputBuffer。
        /// 可扩展：按 Progress/TimeSeconds 比例返还。
        /// </summary>
        private void RefundInputs(MachineRuntimeState machine, BaseRecipeDefinition oldRecipe)
        {
            var inputs = oldRecipe.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                MergeIntoBuffer(machine.InputBuffer, inputs[i].ItemId, inputs[i].Count);
            }
            // 清除进度
            machine.Progress = 0f;
            MachineRuntimeDirty.Mark();
        }

        private bool TryPlaceOutputs(MachineRuntimeState machine, BaseRecipeDefinition recipe)
        {
            var outputs = recipe.Outputs;
            int capacity = machine.Definition.OutputSlotCapacity;

            for (int i = 0; i < outputs.Length; i++)
            {
                int id = outputs[i].ItemId;
                int count = outputs[i].Count;
                bool merged = false;

                for (int k = 0; k < machine.OutputBuffer.Count; k++)
                {
                    if (machine.OutputBuffer[k].itemId == id)
                    {
                        var tup = machine.OutputBuffer[k];
                        tup.count += count;
                        machine.OutputBuffer[k] = tup;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    if (machine.OutputBuffer.Count >= capacity)
                        return false;
                    machine.OutputBuffer.Add((id, count));
                }
            }
            MachineRuntimeDirty.Mark();
            return true;
        }

        private int GetBufferCount(System.Collections.Generic.List<(int itemId, int count)> buffer, int itemId)
        {
            int sum = 0;
            for (int i = 0; i < buffer.Count; i++)
                if (buffer[i].itemId == itemId)
                    sum += buffer[i].count;
            return sum;
        }

        private void MergeIntoBuffer(System.Collections.Generic.List<(int itemId, int count)> buffer, int itemId, int count)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].itemId == itemId)
                {
                    var slot = buffer[i];
                    slot.count += count;
                    buffer[i] = slot;
                    return;
                }
            }
            buffer.Add((itemId, count));
        }

        #endregion
    }
}