using System;
using UnityEngine;
using Game.Features.Production.Machines;
using Game.Data;
using Game.Data.Definition.Recipes;

namespace Game.Features.Production.Processing
{
    public class ProductionTickService : MonoBehaviour
    {
        [SerializeField] private float tickInterval = 0.2f;
        [SerializeField] private MachineRuntimeManager machineManager;

        private float _accum;

        private void Awake()
        {
            if (!machineManager)
                machineManager = FindFirstObjectByType<MachineRuntimeManager>();
        }

        private void Update()
        {
            _accum += Time.deltaTime;
            if (_accum < tickInterval) return;
            _accum = 0f;

            foreach (var m in machineManager.Machines)
            {
                try
                {
                    ProcessMachine(m);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[ProductionTick] Machine {m.InstanceId} error: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private void ProcessMachine(MachineRuntimeState m)
        {
            if (m.ActiveRecipeId == null)
            {
                m.State = MachineProcessState.Idle;
                m.Progress = 0f;
                return;
            }

            var recipe = GameContext.Instance.GetDefinition<BaseRecipeDefinition>(m.ActiveRecipeId.Value);

            // 输入阶段
            if (m.Progress <= 0.0001f)
            {
                if (!HasAllInputs(m, recipe))
                {
                    m.State = MachineProcessState.BlockedInput;
                    return;
                }
                ConsumeInputs(m, recipe);
                m.Progress = 0.00011f;
                m.State = MachineProcessState.Processing;
                ProductionEvents.OnRecipeStarted?.Invoke(m, recipe);
                MachineRuntimeDirty.Mark(); // 输入消耗改变状态
            }

            // 推进
            m.Progress += tickInterval * m.Definition.ProcessingSpeedMultiplier;

            // 完成
            if (m.Progress >= recipe.TimeSeconds)
            {
                if (!TryPlaceOutputs(m, recipe))
                {
                    m.State = MachineProcessState.BlockedOutput;
                    m.Progress = recipe.TimeSeconds;
                    ProductionEvents.OnOutputBlocked?.Invoke(m, recipe);
                    return;
                }

                m.CompletedCycles++;
                ProductionEvents.OnRecipeCompleted?.Invoke(m, recipe);
                m.Progress = 0f;
                m.State = MachineProcessState.Idle;
                MachineRuntimeDirty.Mark(); // 输出/进度更新
            }
        }

        private bool HasAllInputs(MachineRuntimeState m, BaseRecipeDefinition r)
        {
            var inputs = r.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                int needId = inputs[i].ItemId;
                int needCount = inputs[i].Count;
                int have = 0;
                foreach (var slot in m.InputBuffer)
                    if (slot.itemId == needId) have += slot.count;
                if (have < needCount) return false;
            }
            return true;
        }

        private void ConsumeInputs(MachineRuntimeState m, BaseRecipeDefinition r)
        {
            var inputs = r.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                int needId = inputs[i].ItemId;
                int remain = inputs[i].Count;
                for (int b = 0; b < m.InputBuffer.Count && remain > 0; b++)
                {
                    var slot = m.InputBuffer[b];
                    if (slot.itemId != needId) continue;
                    int take = Math.Min(slot.count, remain);
                    slot.count -= take;
                    remain -= take;
                    if (slot.count <= 0)
                    {
                        m.InputBuffer.RemoveAt(b);
                        b--;
                    }
                    else
                        m.InputBuffer[b] = slot;
                }
            }
        }

        private bool TryPlaceOutputs(MachineRuntimeState m, BaseRecipeDefinition r)
        {
            var outputs = r.Outputs;
            int capacity = m.Definition.OutputSlotCapacity;

            for (int i = 0; i < outputs.Length; i++)
            {
                int id = outputs[i].ItemId;
                int count = outputs[i].Count;
                bool merged = false;

                for (int k = 0; k < m.OutputBuffer.Count; k++)
                {
                    if (m.OutputBuffer[k].itemId == id)
                    {
                        var tuple = m.OutputBuffer[k];
                        tuple.count += count;
                        m.OutputBuffer[k] = tuple;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    if (m.OutputBuffer.Count >= capacity)
                        return false;
                    m.OutputBuffer.Add((id, count));
                }
            }
            return true;
        }
    }
}