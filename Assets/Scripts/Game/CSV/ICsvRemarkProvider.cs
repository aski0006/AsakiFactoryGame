using System.Collections.Generic;

namespace Game.CSV
{
    /// <summary>
    /// 提供 CSV 中文备注行（第一行），与 GetCsvHeader 对应。
    /// </summary>
    public interface ICsvRemarkProvider
    {
        IReadOnlyList<string> GetCsvRemarks();
    }
}
