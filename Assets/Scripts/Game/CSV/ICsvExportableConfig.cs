using System.Collections.Generic;

namespace Game.CSV
{
    /// <summary>
    /// 标记一个 ScriptableObject 支持导出为 CSV。
    /// 行 = 若干列的键值对（列名大小写不敏感，最终按 GetCsvHeader 的顺序写出）。
    /// </summary>
    public interface ICsvExportableConfig
    {
        /// <summary>返回导出列头顺序（必须与导入时期望列兼容，便于往返）。</summary>
        IReadOnlyList<string> GetCsvHeader();

        /// <summary>生成所有行（每行一个列名->文本）。缺失的列将写为空字符串。</summary>
        IEnumerable<Dictionary<string, string>> ExportCsvRows();
    }
}
