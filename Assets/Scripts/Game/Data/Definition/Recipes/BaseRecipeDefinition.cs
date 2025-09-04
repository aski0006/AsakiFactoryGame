using Game.Data.Core;
using Game.Data.Definition.Machines;
using System;
using System.Collections.Generic;
using UnityEngine;

// 若 ItemStack / ItemId 定义在此命名空间

// 若 MachineType 在此命名空间

namespace Game.Data.Definition.Recipes
{
    /// <summary>
    /// 配方静态定义（数据驱动）。
    /// VS1 目标：最小字段 + 明确扩展位，不包含任何运行时状态。
    /// - 不继承 ScriptableObject：由 Section (DefinitionSectionBase) 容器持有。
    /// - 仅描述“输入 -> 输出” 与 “耗时” 与 “适用机器类型”。
    /// - 可通过 ConfigIdCodeGenerator 生成常量：依赖 id / codeName 字段。
    /// 修改兼容注意：
    /// - 修改 Id：会破坏已有存档/引用，需迁移。
    /// - 修改 codeName：会影响已生成常量名（重新生成后需替换代码引用）。
    /// </summary>
    [Serializable]
    public class BaseRecipeDefinition : IDefinition
    {
        #region Identity

        [SerializeField, Tooltip("全局唯一 Id (>0)。")]
        private int id;

        [SerializeField, Tooltip("内部代码名（稳定英文标识，生成常量使用）。推荐格式：Source_To_Target / CompositeName。")]
        private string codeName;

        [SerializeField, Tooltip("展示名称（可本地化；为空时 UI 可回退 codeName）。")]
        private string displayName;

        #endregion

        #region IO

        [SerializeField, Tooltip("输入物品列表（全部满足才开始）。")]
        private ItemStack[] inputs = Array.Empty<ItemStack>();

        [SerializeField, Tooltip("输出物品列表（全部一次性生成）。")]
        private ItemStack[] outputs = Array.Empty<ItemStack>();

        #endregion

        #region Processing Attributes

        [SerializeField, Min(0.01f), Tooltip("完成一次生产所需秒数。Tick 结束判定：累计≥TimeSeconds。")]
        private float timeSeconds = 1f;

        [SerializeField, Tooltip("适用的机器类型（Hand 表示可手搓，VS1 可限制只在机器内）。")]
        private MachineType machineType = MachineType.Assembler;

        [SerializeField, Tooltip("是否允许玩家手工快速制作（与 machineType 逻辑可叠加控制 UI 入口）。")]
        private bool allowHandCraft = false;

        [SerializeField, Tooltip("一次生产的最大并行批量（VS1 不使用，预留；未来机器可叠加批量提升）。")]
        private int batchLimit = 1;

        [SerializeField, Tooltip("可选：生产时是否立即消耗输入（否则可做“准入占位”，VS1 使用立即消耗）。")]
        private bool consumeInputsAtStart = true;

        #endregion

        #region Progression / Unlock

        [SerializeField, Tooltip("解锁层级 / 科技等级要求（VS1 可忽略）。")]
        private int unlockTier = 0;

        [SerializeField, Tooltip("是否为默认解锁（true：无需科技即可使用）。")]
        private bool unlockedByDefault = true;

        #endregion

        #region Meta / Tags

        [SerializeField, TextArea(2, 4), Tooltip("描述 / 备注（设计 & 平衡信息）。")]
        private string description;

        [SerializeField, Tooltip("标签（过滤 / 分类，如 Smelting / Basic / KeyProgress 等）。")]
        private string[] tags;

        #endregion

        #region Optional Balancing (预留，不在 VS1 内部使用)

        [SerializeField, Tooltip("理论能量消耗（电力系统引入后再用）。")]
        private float energyCost = 0f;

        [SerializeField, Tooltip("经验或科技点奖励（未来使用）。")]
        private int xpReward = 0;

        #endregion

        #region IDefinition

        public int Id => id;
        public string CodeName => codeName;

        #endregion

        #region Public Readonly Accessors

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;
        public ReadOnlySpan<ItemStack> InputsSpan => inputs;
        public ReadOnlySpan<ItemStack> OutputsSpan => outputs;
        public ItemStack[] Inputs => inputs;    // 若外部需要数组遍历
        public ItemStack[] Outputs => outputs;
        public float TimeSeconds => timeSeconds;
        public MachineType MachineType => machineType;
        public bool AllowHandCraft => allowHandCraft;
        public bool ConsumeInputsAtStart => consumeInputsAtStart;
        public int BatchLimit => batchLimit <= 0 ? 1 : batchLimit;
        public int UnlockTier => unlockTier;
        public bool UnlockedByDefault => unlockedByDefault;
        public string Description => description;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public float EnergyCost => energyCost;
        public int XpReward => xpReward;

        #endregion

        #region Derived Helpers

        /// <summary>单次输出总物品数量（所有输出 Count 求和）。</summary>
        public int TotalOutputCount
        {
            get
            {
                int sum = 0;
                if (outputs != null)
                    for (int i = 0; i < outputs.Length; i++) sum += outputs[i].Count;
                return sum;
            }
        }

        /// <summary>简单吞吐量：每秒输出物品数量合计。</summary>
        public float ThroughputItemsPerSecond => TimeSeconds <= 0 ? 0f : TotalOutputCount / TimeSeconds;

        /// <summary>是否包含指定标签（精确匹配，区分大小写）。</summary>
        public bool HasTag(string tag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrEmpty(tag))
                return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag)
                    return true;
            return false;
        }

        #endregion

#if UNITY_EDITOR
        #region Validation (Editor Only)

        private void OnValidate()
        {
            if (id < 0) id = 0;
            if (timeSeconds < 0.01f) timeSeconds = 0.01f;
            if (batchLimit < 1) batchLimit = 1;

            // 修剪 codeName
            if (!string.IsNullOrEmpty(codeName))
                codeName = codeName.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(codeName))
                displayName = codeName;

            // 去除空标签
            if (tags != null && tags.Length > 0)
            {
                for (int i = 0; i < tags.Length; i++)
                    if (tags[i] != null)
                        tags[i] = tags[i].Trim();
            }

            // 简单校验：输入/输出为空时警告
            if (inputs == null || inputs.Length == 0)
                Debug_EnsureWarning("Inputs 列表为空（可能是占位配方？）");
            if (outputs == null || outputs.Length == 0)
                Debug_EnsureWarning("Outputs 列表为空（将不会产生任何物品）");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debug_EnsureWarning(string msg)
        {
            // 不用 Debug.LogWarning 每次刷屏，可视需要打开
            // Debug.LogWarning($"[BaseRecipeDefinition:{codeName}] {msg}");
        }

        #endregion
#endif
    }

    /// <summary>
    /// 物品堆栈结构（如果你已有全局定义，可删除此重复声明）。
    /// 强烈建议与 ItemDefinition 中的 ItemId 协同使用。
    /// </summary>
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
        public override string ToString() => $"[{ItemId} x{Count}]";
    }
}