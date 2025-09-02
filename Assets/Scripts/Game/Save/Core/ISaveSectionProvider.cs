using System;

namespace Game.Save.Core
{
    /// <summary>
    /// 功能模块实现：负责将运行时状态打包为 Section，及从 Section 恢复。
    /// </summary>
    public interface ISaveSectionProvider
    {
        /// <summary>是否已准备好参与存档（例如模块已初始化）。</summary>
        bool Ready { get; }

        /// <summary>导出快照。返回 null 表示该模块当前不需要写入。</summary>
        ISaveSection Capture();

        /// <summary>从快照恢复。传入 null 代表没有对应数据(需用默认值)。</summary>
        void Restore(ISaveSection section);
    }
}
