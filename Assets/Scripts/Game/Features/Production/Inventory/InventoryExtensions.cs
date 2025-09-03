using Game.Data.Definition.Items;
using Game.Data;

namespace Game.Features.Production.Inventory
{
    /// <summary>
    /// 一些便捷扩展（未来可拆）。
    /// </summary>
    public static class InventoryExtensions
    {
        /// <summary>
        /// 检查物品是否足够。
        /// </summary>
        public static bool HasAtLeast(this InventoryComponent inv, int itemId, int count)
            => inv.TotalCount(itemId) >= count;

        /// <summary>
        /// 获取物品定义。
        /// </summary>
        public static BaseItemDefinition GetItemDef(this InventoryComponent inv, int itemId)
            => GameContext.Instance.GetDefinition<BaseItemDefinition>(itemId);
    }
}
