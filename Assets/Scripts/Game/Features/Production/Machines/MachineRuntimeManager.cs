using System.Collections.Generic;
using UnityEngine;
using Game.Data;
using Game.Data.Definition.Machines;
using Game.Features.Production.Processing;
using System;

namespace Game.Features.Production.Machines
{
    /// <summary>
    /// 运行时机器管理器：负责实例化与查询 & 支持快照恢复。
    /// </summary>
    public class MachineRuntimeManager : MonoBehaviour
    {
        private readonly List<MachineRuntimeState> _machines = new();
        private int _nextId = 1;
        private MachineProcessService _rebuildHelper; // 用于重建时的状态计算
        public IReadOnlyList<MachineRuntimeState> Machines => _machines;

        private void Awake()
        {
            _rebuildHelper = new MachineProcessService();
        }

        public MachineRuntimeState SpawnMachine(int defId)
        {
            var def = GameContext.Instance.GetDefinition<BaseMachineDefinition>(defId);
            var state = new MachineRuntimeState
            {
                InstanceId = _nextId++,
                Definition = def
            };
            _machines.Add(state);
            MachineRuntimeDirty.Mark();
            return state;
        }

        public MachineRuntimeState GetByInstanceId(int id)
        {
            foreach (MachineRuntimeState t in _machines)
                if (t.InstanceId == id)
                    return t;
            return null;
        }

        /// <summary>
        /// 通过存档快照重建（清空旧列表）。
        /// </summary>
        public void RebuildFromSnapshot(List<MachineRuntimeSaveProvider.MachineDTO> dtos)
        {
            _machines.Clear();
            _nextId = 1;
            if (dtos == null || dtos.Count == 0)
            {
                MachineRuntimeDirty.Mark();
                return;
            }

            int maxInstance = 0;
            foreach (var dto in dtos)
            {
                var def = GameContext.Instance.GetDefinition<BaseMachineDefinition>(dto.definitionId);
                var st = new MachineRuntimeState
                {
                    InstanceId = dto.instanceId,
                    Definition = def,
                    ActiveRecipeId = dto.activeRecipeId,
                    Progress = dto.progress,
                    CompletedCycles = dto.completedCycles,
                    Phase = MachinePhase.Idle, 
                };

                if (dto.input != null)
                    foreach (var inp in dto.input)
                        if (inp.itemId > 0 && inp.count > 0)
                            st.InputBuffer.Add((inp.itemId, inp.count));

                if (dto.output != null)
                    foreach (var op in dto.output)
                        if (op.itemId > 0 && op.count > 0)
                            st.OutputBuffer.Add((op.itemId, op.count));
                _rebuildHelper.RebuildStateAfterLoad(st);
                
                _machines.Add(st);
                if (st.InstanceId > maxInstance) maxInstance = st.InstanceId;
            }
            _nextId = maxInstance + 1;
            MachineRuntimeDirty.Mark();
        }
    }
}
