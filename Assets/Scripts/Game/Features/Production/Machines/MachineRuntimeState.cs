using Game.Data.Definition.Machines;
using System;
using System.Collections.Generic;

namespace Game.Features.Production.Machines
{
    
    /// <summary>
    /// 运行时机器实例状态（逻辑层 DTO）。
    /// 任务1：引入<see cref="Phase"/>以取代Progress + MachineProcessState的模糊用法。
    /// 
    /// 迁移说明：
    ///  - 新代码应仅依赖Phase。
    ///  - 暂时保留State字段，避免现有系统（UI/Tick）立即崩溃。
    ///  - 提供同步辅助方法（SyncLegacyStateFromPhase / SyncPhaseFromLegacyState）。
    ///  - 完全迁移后，删除State、MachineProcessState及辅助方法。
    /// </summary>
    public class MachineRuntimeState
    {
        public int InstanceId;                 // 实例ID
        public BaseMachineDefinition Definition; // 机器定义

        /// <summary>当前激活的配方ID（无则为null）。</summary>
        public int? ActiveRecipeId;

        /// <summary>
        /// 生产进度时间（秒）。进入Processing时总是重置为0。
        /// </summary>
        public float Progress;

        /// <summary>合并后的输入缓冲区（相同itemId已合并）。</summary>
        public readonly List<(int itemId, int count)> InputBuffer = new();

        /// <summary>合并后的输出缓冲区。</summary>
        public readonly List<(int itemId, int count)> OutputBuffer = new();

        /// <summary>
        /// 新的详细生命周期阶段。
        /// </summary>
        public MachinePhase Phase = MachinePhase.Idle;
        
        /// <summary>已成功完成的生产周期数（统计用）。</summary>
        public int CompletedCycles;

        /// <summary>
        /// 未来可选标志：是否自动收集输出（后续任务引入；留作前向兼容）。
        /// </summary>
        public bool AutoCollectOutputs;
        
        public override string ToString()
            => $"机器[{InstanceId}] {Definition?.CodeName} 配方={ActiveRecipeId?.ToString() ?? "无"} 阶段={Phase} 进度={Progress:0.00} 周期={CompletedCycles}";
    }
}