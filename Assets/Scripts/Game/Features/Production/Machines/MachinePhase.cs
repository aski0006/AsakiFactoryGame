namespace Game.Features.Production.Machines
{
    /// <summary>
    /// 机器运行状态
    /// </summary>
    public enum MachinePhase
    {
        /// <summary>无活跃配方，机器处于闲置状态。</summary>
        Idle = 0,

        /// <summary>已设置配方；等待所需输入材料被（重新）确认并消耗。</summary>
        PendingInput = 1,

        /// <summary>所有输入材料已消耗；生产计时器正在推进。</summary>
        Processing = 2,

        /// <summary>生产已完成；产出已完全放入输出缓冲区（如果连续模式需要连锁生产，仍有可用空间）。</summary>
        OutputReady = 3,

        /// <summary>输入材料不足（或已被移除），无法开始/继续生产。</summary>
        BlockedInput = 4,

        /// <summary>无法放置产出（缓冲区已满/插槽容量限制）。</summary>
        BlockedOutput = 5
    }
}
