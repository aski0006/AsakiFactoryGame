using System.Collections.Generic;
using Game.Data.Definition.Machines;

namespace Game.Features.Production.Machines
{
    public enum MachineProcessState
    {
        Idle,
        Processing,
        BlockedInput,
        BlockedOutput
    }

    /// <summary>
    /// 机器的运行时实例状态（非持久类，可被保存成 DTO）。
    /// </summary>
    public class MachineRuntimeState
    {
        public int InstanceId;
        public BaseMachineDefinition Definition;

        public int? ActiveRecipeId;
        public float Progress;

        // 简单的输入/输出缓冲（聚合形式：同 itemId 合并）
        public readonly List<(int itemId, int count)> InputBuffer = new();
        public readonly List<(int itemId, int count)> OutputBuffer = new();

        public MachineProcessState State = MachineProcessState.Idle;

        // 统计：完成的生产循环次数
        public int CompletedCycles;

        public override string ToString()
            => $"Machine[{InstanceId}] {Definition?.CodeName} Recipe={ActiveRecipeId?.ToString() ?? "None"} Prog={Progress:0.00} Cycles={CompletedCycles}";
    }
}
