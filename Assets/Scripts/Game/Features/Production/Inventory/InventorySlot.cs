using System;

namespace Game.Features.Production.Inventory
{
    /// <summary>
    /// 背包槽位（最小数据）。可序列化。
    /// </summary>
    [Serializable]
    public struct InventorySlot
    {
        public int ItemId;
        public string ItemDisplayName;
        public int Count;

        public bool IsEmpty => ItemId <= 0 || Count <= 0;

        public void Clear()
        {
            ItemId = 0;
            Count = 0;
            ItemDisplayName = null;
        }

        public override string ToString() => IsEmpty ? "(empty)" : $"{ItemId}: {ItemDisplayName} x {Count}";
    }
}
