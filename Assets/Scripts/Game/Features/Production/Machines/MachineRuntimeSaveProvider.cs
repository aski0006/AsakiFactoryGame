using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Save;
using Game.Save.Core;
using Game.Features.Production.Machines;

namespace Game.Features.Production.Machines
{
    /// <summary>
    /// 保存 / 恢复所有机器运行时状态（不含实际游戏世界位置，仅逻辑）。
    /// </summary>
    public class MachineRuntimeSaveProvider : MonoBehaviour,
        IDirtySaveSectionProvider,
        IExposeSectionType,
        ICustomSaveSectionKey
    {
        [Serializable]
        public class MachinesSection : ISaveSection
        {
            public int version = 1;
            public List<MachineDTO> machines = new();
        }

        [Serializable]
        public class MachineDTO
        {
            public int definitionId;
            public int instanceId;
            public int? activeRecipeId;
            public float progress;
            public int completedCycles;
            public List<ItemStackDTO> input = new();
            public List<ItemStackDTO> output = new();
        }

        [Serializable]
        public class ItemStackDTO
        {
            public int itemId;
            public int count;
        }

        public string Key => "Machines";
        public Type SectionType => typeof(MachinesSection);
        public bool Ready => _manager != null;

        public bool Dirty => _dirty;
        private bool _dirty;

        private MachineRuntimeManager _manager;

        void Awake()
        {
            _manager = FindFirstObjectByType<MachineRuntimeManager>();
        }

        void OnEnable()
        {
            SaveManager.Instance?.RegisterProvider(this);
            MachineRuntimeDirty.OnDirty += HandleDirty;
        }

        void OnDisable()
        {
            SaveManager.Instance?.UnregisterProvider(this);
            MachineRuntimeDirty.OnDirty -= HandleDirty;
        }

        private void HandleDirty() => _dirty = true;
        public void ClearDirty() => _dirty = false;

        public ISaveSection Capture()
        {
            if (!Dirty || !Ready) return null;

            var section = new MachinesSection { version = 1 };
            foreach (var m in _manager.Machines)
            {
                var dto = new MachineDTO
                {
                    definitionId = m.Definition.Id,
                    instanceId = m.InstanceId,
                    activeRecipeId = m.ActiveRecipeId,
                    progress = m.Progress,
                    completedCycles = m.CompletedCycles
                };
                foreach (var inp in m.InputBuffer)
                    dto.input.Add(new ItemStackDTO { itemId = inp.itemId, count = inp.count });
                foreach (var op in m.OutputBuffer)
                    dto.output.Add(new ItemStackDTO { itemId = op.itemId, count = op.count });

                section.machines.Add(dto);
            }
            return section;
        }

        public void Restore(ISaveSection section)
        {
            if (!Ready)
                return;

            if (section is not MachinesSection sec)
            {
                // 没有历史数据 -> 清空
                _manager.RebuildFromSnapshot(null);
                _dirty = false;
                return;
            }

            _manager.RebuildFromSnapshot(sec.machines);
            _dirty = false;
        }
    }
}