using System;

namespace Game.Data.Definition.Recipes
{
    [Serializable]
    public struct ItemStack
    {
        public int ItemId;
        public int Count;

        public ItemStack(int itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public bool IsValid => ItemId > 0 && Count > 0;
        public override string ToString() => $"[{ItemId} x {Count}]";
    }
}
