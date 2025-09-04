using Game.CSV;
using System.Globalization;

namespace Game.Data.Definition.Recipes
{
    /// <summary>
    /// ItemStack 转换器。
    /// 标准格式：ItemId:Count （示例 1001:3）
    /// 兼容格式：ItemId,Count 或 ItemIdxCount
    /// </summary>
    public sealed class ItemStackCsvConverter : CsvTypeConverter<ItemStack>
    {
        protected override string SerializeTyped(ItemStack value)
        {
            if (!value.IsValid) return "";
            return $"{value.ItemId}:{value.Count}";
        }

        protected override bool TryDeserializeTyped(string text, out ItemStack value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();

            // 标准冒号
            int sep = text.IndexOf(':');
            if (sep < 0)
            {
                // 尝试逗号
                sep = text.IndexOf(',');
                if (sep < 0)
                {
                    // 尝试 'x' (兼容老写法 1001x3 或 1001 x3)
                    sep = text.IndexOf('x');
                }
            }

            if (sep <= 0 || sep >= text.Length - 1)
                return false;

            var a = text.Substring(0, sep).Trim();
            var b = text.Substring(sep + 1).Trim();
            if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId)) return false;
            if (!int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) return false;
            if (itemId <= 0 || count <= 0) return false;

            value = new ItemStack(itemId, count);
            return true;
        }
    }
}
