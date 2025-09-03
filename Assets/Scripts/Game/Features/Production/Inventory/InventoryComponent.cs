using System;
using UnityEngine;
using Game.Data;
using Game.Data.Definition.Items;
using Game.Save;             
using Game.Save.Core;         

namespace Game.Features.Production.Inventory
{
    /// <summary>
    /// 背包组件 + 存档 Provider（含脏标记）。
    /// 存档结构仅存 (ItemId, Count)，DisplayName 运行时恢复再填。
    /// </summary>
    public class InventoryComponent : MonoBehaviour,
        IDirtySaveSectionProvider,
        IExposeSectionType,
        ICustomSaveSectionKey
    {
        [Serializable]
        public class InventorySection : ISaveSection
        {
            public int version = 1;
            public SlotDTO[] slots;
        }

        [Serializable]
        public struct SlotDTO
        {
            public int itemId;
            public int count;
        }

        [SerializeField] private int capacity = 40;
        [SerializeField] private InventorySlot[] slots;

        public int Capacity => capacity;
        public InventorySlot[] Slots => slots;

        private bool _dirty;

        public string Key => "PlayerInventory";
        public Type SectionType => typeof(InventorySection);
        public bool Ready => true;
        public bool Dirty => _dirty;

        public void ClearDirty() => _dirty = false;

        private void Awake()
        {
            if (slots == null || slots.Length != capacity)
                slots = new InventorySlot[capacity];
        }

        void OnEnable()
        {
            SaveManager.Instance?.RegisterProvider(this);
        }

        void OnDisable()
        {
            SaveManager.Instance?.UnregisterProvider(this);
        }

        /// <summary>添加物品，返回实际添加数量。</summary>
        public int AddItem(int itemId, int count)
        {
            if (count <= 0) return 0;
            var ctx = GameContext.Instance;
            var def = ctx.GetDefinition<BaseItemDefinition>(itemId);
            int remain = count;

            // 合并已有
            for (int i = 0; i < slots.Length && remain > 0; i++)
            {
                ref var s = ref slots[i];
                if (s.IsEmpty || s.ItemId != itemId) continue;
                int space = def.MaxStack - s.Count;
                if (space <= 0) continue;
                int take = Math.Min(space, remain);
                s.Count += take;
                s.ItemDisplayName = def.DisplayName;
                remain -= take;
            }

            // 找空位
            for (int i = 0; i < slots.Length && remain > 0; i++)
            {
                ref var s = ref slots[i];
                if (!s.IsEmpty) continue;
                int take = Math.Min(def.MaxStack, remain);
                s.ItemId = itemId;
                s.Count = take;
                s.ItemDisplayName = def.DisplayName;
                remain -= take;
            }

            if (count != remain) _dirty = true;
            return count - remain;
        }

        /// <summary>尝试消耗指定数量，成功返回 true。</summary>
        public bool TryConsume(int itemId, int count)
        {
            if (count <= 0) return true;
            if (TotalCount(itemId) < count) return false;

            int remain = count;
            for (int i = 0; i < slots.Length && remain > 0; i++)
            {
                ref var s = ref slots[i];
                if (s.ItemId != itemId) continue;
                int take = Math.Min(s.Count, remain);
                s.Count -= take;
                remain -= take;
                if (s.Count <= 0) s.Clear();
            }
            _dirty = true;
            return true;
        }

        public int TotalCount(int itemId)
        {
            int sum = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.ItemId == itemId) sum += s.Count;
            }
            return sum;
        }

        #region Save Provider

        public ISaveSection Capture()
        {
            // 若不脏可以返回 null（节省存档写入），也可强行返回
            if (!Dirty) return null;

            var list = new SlotDTO[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var s = slots[i];
                list[i].itemId = s.ItemId;
                list[i].count = s.Count;
            }
            return new InventorySection
            {
                version = 1,
                slots = list
            };
        }

        public void Restore(ISaveSection section)
        {
            if (section is not InventorySection sec || sec.slots == null)
            {
                // 空存档：清空
                for (int i = 0; i < capacity; i++)
                    slots[i].Clear();
                _dirty = false;
                return;
            }

            if (slots == null || slots.Length != capacity)
                slots = new InventorySlot[capacity];

            var ctx = GameContext.Instance;
            for (int i = 0; i < capacity; i++)
            {
                if (i >= sec.slots.Length)
                {
                    slots[i].Clear();
                    continue;
                }
                var dto = sec.slots[i];
                if (dto.itemId <= 0 || dto.count <= 0)
                {
                    slots[i].Clear();
                    continue;
                }
                slots[i].ItemId = dto.itemId;
                slots[i].Count = dto.count;
                // 恢复展示名称
                try
                {
                    var def = ctx.GetDefinition<BaseItemDefinition>(dto.itemId);
                    slots[i].ItemDisplayName = def.DisplayName;
                }
                catch
                {
                    slots[i].ItemDisplayName = "Unknown";
                }
            }
            _dirty = false;
        }

        #endregion
    }
}