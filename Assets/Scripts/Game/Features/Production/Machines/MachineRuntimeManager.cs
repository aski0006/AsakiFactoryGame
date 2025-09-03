using System.Collections.Generic;
using UnityEngine;
using Game.Data;
using Game.Data.Definition.Machines;

namespace Game.Features.Production.Machines
{
    /// <summary>
    /// 运行时机器管理器：负责实例化与查询 & 支持快照恢复。
    /// </summary>
    public class MachineRuntimeManager : MonoBehaviour
    {
        private readonly List<MachineRuntimeState> _machines = new();
        private int _nextId = 1;

        public IReadOnlyList<MachineRuntimeState> Machines => _machines;

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
            for (int i = 0; i < _machines.Count; i++)
                if (_machines[i].InstanceId == id) return _machines[i];
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
                    State = MachineProcessState.Idle // 重新计算状态，由 Tick 决定
                };

                if (dto.input != null)
                    foreach (var inp in dto.input)
                        if (inp.itemId > 0 && inp.count > 0)
                            st.InputBuffer.Add((inp.itemId, inp.count));

                if (dto.output != null)
                    foreach (var op in dto.output)
                        if (op.itemId > 0 && op.count > 0)
                            st.OutputBuffer.Add((op.itemId, op.count));

                _machines.Add(st);
                if (st.InstanceId > maxInstance) maxInstance = st.InstanceId;
            }
            _nextId = maxInstance + 1;
            MachineRuntimeDirty.Mark();
        }
    }
}