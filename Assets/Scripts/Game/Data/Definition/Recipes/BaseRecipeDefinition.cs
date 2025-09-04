using Game.CSV;
using Game.Data.Core;
using Game.Data.Definition.Machines;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Definition.Recipes
{
    /// <summary>
    /// 配方静态定义（数据驱动）。
    /// - 不继承 ScriptableObject
    /// - 定义 输入 -> 输出 / 耗时 / 适用机器
    /// </summary>
    [CsvDefinition]
    [Serializable]
    public partial class BaseRecipeDefinition : IDefinition
    {
        #region Identity

        [CsvField("id", remark: "唯一ID", Required = true)]
        [SerializeField, Tooltip("全局唯一 Id (>0)。")]
        private int id;

        [CsvField("codeName", remark: "内部代码名", Required = true)]
        [SerializeField, Tooltip("内部代码名（稳定英文标识，生成常量使用）。推荐格式：Source_To_Target / CompositeName。")]
        private string codeName;

        [CsvField("displayName", remark: "显示名称")]
        [SerializeField, Tooltip("展示名称（可本地化；为空时 UI 可回退 codeName）。")]
        private string displayName;

        #endregion

        #region IO

        [CsvField("inputs", remark: "输入(ItemId:数量,逗号分隔)", CustomArraySeparator = ",")]
        [SerializeField, Tooltip("输入物品列表（全部满足才开始）。")]
        private ItemStack[] inputs = Array.Empty<ItemStack>();

        [CsvField("outputs", remark: "输出(ItemId:数量,逗号分隔)", CustomArraySeparator = ",")]
        [SerializeField, Tooltip("输出物品列表（全部一次性生成）。")]
        private ItemStack[] outputs = Array.Empty<ItemStack>();

        #endregion

        #region Processing Attributes

        [CsvField("timeSeconds", remark: "耗时秒")]
        [SerializeField, Min(0.01f), Tooltip("完成一次生产所需秒数。")]
        private float timeSeconds = 1f;

        [CsvField("machineType", remark: "机器类型")]
        [SerializeField, Tooltip("适用的机器类型。")]
        private MachineType machineType = MachineType.Assembler;

        [CsvField("allowHandCraft", remark: "允许手搓")]
        [SerializeField, Tooltip("是否允许玩家手工快速制作。")]
        private bool allowHandCraft = false;

        [CsvField("batchLimit", remark: "批量上限")]
        [SerializeField, Tooltip("一次生产的最大并行批量（预留）。")]
        private int batchLimit = 1;

        [CsvField("consumeInputsAtStart", remark: "开始即消耗")]
        [SerializeField, Tooltip("是否在开始时立即消耗输入。")]
        private bool consumeInputsAtStart = true;

        #endregion

        #region Progression / Unlock

        [CsvField("unlockTier", remark: "解锁层级")]
        [SerializeField, Tooltip("解锁层级 / 科技等级要求。")]
        private int unlockTier = 0;

        [CsvField("unlockedByDefault", remark: "默认解锁")]
        [SerializeField, Tooltip("是否为默认解锁。")]
        private bool unlockedByDefault = true;

        #endregion

        #region Meta / Tags

        [CsvField("description", remark: "描述")]
        [SerializeField, TextArea(2, 4), Tooltip("描述 / 备注（设计 & 平衡信息）。")]
        private string description;

        [CsvField("tags", remark: "标签(分号;)")]
        [SerializeField, Tooltip("标签（过滤 / 分类）。")]
        private string[] tags;

        #endregion

        #region Optional Balancing

        [CsvField("energyCost", remark: "能量消耗")]
        [SerializeField, Tooltip("理论能量消耗。")]
        private float energyCost = 0f;

        [CsvField("xpReward", remark: "经验奖励")]
        [SerializeField, Tooltip("经验或科技点奖励。")]
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
        public ItemStack[] Inputs => inputs;
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

        public int TotalOutputCount
        {
            get
            {
                int sum = 0;
                if (outputs != null)
                    for (int i = 0; i < outputs.Length; i++)
                        sum += outputs[i].Count;
                return sum;
            }
        }

        public float ThroughputItemsPerSecond => TimeSeconds <= 0 ? 0f : TotalOutputCount / TimeSeconds;

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

            if (!string.IsNullOrEmpty(codeName))
                codeName = codeName.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(codeName))
                displayName = codeName;

            if (tags != null && tags.Length > 0)
            {
                for (int i = 0; i < tags.Length; i++)
                    if (tags[i] != null)
                        tags[i] = tags[i].Trim();
            }

            if (inputs == null || inputs.Length == 0)
                Debug_EnsureWarning("Inputs 列表为空（可能是占位配方？）");
            if (outputs == null || outputs.Length == 0)
                Debug_EnsureWarning("Outputs 列表为空（将不会产生任何物品）");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debug_EnsureWarning(string msg)
        {
            // Debug.LogWarning($"[BaseRecipeDefinition:{codeName}] {msg}");
        }

        #endregion
#endif
    }

}
