using System.Collections.Generic;

namespace Game.CSV
{
    /// <summary>
    /// 标记一个 ScriptableObject 支持通过 CSV 导入自身内部的数据。
    /// 建议：只导入“数据内容”而非自身基础元字段（比如 config 标志等）。
    /// 调用顺序：
    /// 1. PrepareForCsvImport()
    /// 2. ImportCsvRow(row, helper, lineIndex) （针对每一行数据）
    /// 3. FinalizeCsvImport(successCount, errorCount)
    /// </summary>
    public interface ICsvImportableConfig
    {
        /// <summary>返回该配置期望的列集合（用于校验 & 提示）。允许返回 null 表示不强制。</summary>
        IReadOnlyList<string> GetExpectedColumns();

        /// <summary>在开始导入前调用，可清空内部列表或建立索引。</summary>
        void PrepareForCsvImport(bool clearExisting);

        /// <summary>
        /// 导入单行。
        /// lineIndex: 从 1 开始的 CSV 行号（含表头时第一数据行为 2），用于报告。
        /// 返回 true 代表成功（计入成功数），false 代表失败（计入错误数）。
        /// 抛异常也会被外层抓取并计入错误。
        /// </summary>
        bool ImportCsvRow(Dictionary<string, string> row, ICsvImportHelper helper, int lineIndex);

        /// <summary>全部行处理后调用，可做排序 / 去重校验 / 构建缓存等。</summary>
        void FinalizeCsvImport(int success, int error);
    }
}
