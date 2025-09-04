using Game.CSV;
using Game.Data.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Definition.Machines
{
    /// <summary>
    /// 机器静态定义（数据驱动 / 无运行时状态）
    /// VS1 目标：
    ///   - Furnace / Assembler 两种
    ///   - 仅需尺寸 / 机器类型 / IO 基础信息 / 产能限制占位
    /// 设计原则：
    ///   1. 不包含运行逻辑（Tick / 当前进度）——这些放到 MachineRuntimeState
    ///   2. 可扩展（电力 / 模块化 / 多 IO / 流体）字段预留但不强耦合
    ///   3. 生成器 (ConfigIdCodeGenerator) 依赖 Id / codeName
    /// </summary>
    [CsvDefinition(ArraySeparator = ';')]
    [Serializable]
    public partial class BaseMachineDefinition : IDefinition
    {
        #region Identity
        [CsvField("id", remark: "唯一ID", Required = true)]
        [SerializeField, Tooltip("全局唯一 Id (>0)。")]
        private int id;

        [CsvField("codeName", remark: "内部代码名", Required = true)]
        [SerializeField, Tooltip("内部代码名（稳定英文标识，用于生成常量）。")]
        private string codeName;

        [CsvField("displayName", remark: "显示名称")]
        [SerializeField, Tooltip("展示名称（UI 友好文本，若为空回退 codeName）。")]
        private string displayName;
        #endregion

        #region Core Machine Attributes
        [CsvField("machineType", remark: "机器类型")]
        [SerializeField, Tooltip("机器类别（决定可用配方集合的第一层过滤）。")]
        private MachineType machineType = MachineType.Assembler;

        [CsvField("size", remark: "占格尺寸(W;H)")]
        [SerializeField, Tooltip("占用网格尺寸（W×H，统一整格制）。")]
        private Vector2Int size = new Vector2Int(2, 2);

        [CsvField("rotatable", remark: "可旋转")]
        [SerializeField, Tooltip("是否允许旋转（若 false 则始终使用默认朝向）。")]
        private bool rotatable = true;

        [CsvField("defaultRotation", remark: "默认朝向")]
        [SerializeField, Tooltip("默认朝向（0=Up/或你的坐标系定义，可与放置系统协同）。")]
        private int defaultRotation = 0;
        #endregion

        #region Processing & Capacity
        [CsvField("processingSpeedMultiplier", remark: "处理速度倍率")]
        [SerializeField, Min(0.01f), Tooltip("基础处理倍速（1 = 配方原始耗时；2 = 时间减半）。仅 VS1 暂不使用可保持 1。")]
        private float processingSpeedMultiplier = 1f;

        [CsvField("singleQueue", remark: "串行队列")]
        [SerializeField, Tooltip("是否只允许串行配方（VS1 固定 true；预留并行框架）。")]
        private bool singleQueue = true;

        [CsvField("inputSlotCapacity", remark: "输入槽位数")]
        [SerializeField, Min(1), Tooltip("输入缓冲最大槽位（VS1 建议 4）。")]
        private int inputSlotCapacity = 4;

        [CsvField("outputSlotCapacity", remark: "输出槽位数")]
        [SerializeField, Min(1), Tooltip("输出缓冲最大槽位（VS1 建议 2）。")]
        private int outputSlotCapacity = 2;

        [CsvField("autoPullInputs", remark: "自动拉取")]
        [SerializeField, Tooltip("是否自动拉取周围输入（VS1 不实现，仅占位）。")]
        private bool autoPullInputs = false;

        [CsvField("autoPushOutputs", remark: "自动推送")]
        [SerializeField, Tooltip("是否自动推送输出（VS1 不实现，仅占位）。")]
        private bool autoPushOutputs = false;
        #endregion
        
        #region Power / Future Systems
        [CsvField("requiresPower", remark: "需电力")]
        [SerializeField, Tooltip("是否需要电力（VS1=False；后续引入电力系统时启用）。")]
        private bool requiresPower = false;

        [CsvField("basePowerConsumption", remark: "基础功耗")]
        [SerializeField, Tooltip("基础耗电（功率单位：kW 或自定义；VS1 未使用）。")]
        private float basePowerConsumption = 0f;

        [CsvField("idleConsumesPower", remark: "空转耗电")]
        [SerializeField, Tooltip("空转是否耗电（VS1 未使用）。")]
        private bool idleConsumesPower = false;
        #endregion

        #region Recipe Filtering
        [CsvField("useWhitelist", remark: "启用白名单")]
        [SerializeField, Tooltip("是否限制为一个显式白名单（若列表为空且 useWhitelist=true 则无可用配方）。")]
        private bool useWhitelist = false;

        [CsvField("recipeWhitelistIds", remark: "白名单配方ID(;)")]
        [SerializeField, Tooltip("可用配方白名单 Id 列表（与 useWhitelist 配合）。")]
        private int[] recipeWhitelistIds = Array.Empty<int>();

        [CsvField("recipeBlacklistIds", remark: "黑名单配方ID(;)")]
        [SerializeField, Tooltip("可选：黑名单（useWhitelist=false 时可排除个别配方）。")]
        private int[] recipeBlacklistIds = Array.Empty<int>();

        [CsvField("includeHandRecipes", remark: "包含手搓配方")]
        [SerializeField, Tooltip("是否允许手工配方（MachineType.Hand）也在此机器 UI 中展示。")]
        private bool includeHandRecipes = false;
        #endregion

        #region UI / Visual / Audio
        [CsvField("icon", remark: "图标引用")]
        [SerializeField, Tooltip("主图标（放置面板 / 建筑选择 UI）。")]
        private Sprite icon;

        [CsvField("description", remark: "描述")]
        [SerializeField, TextArea(2,4), Tooltip("描述文本 / 使用提示 / 解锁说明。")]
        private string description;

        [CsvField("prefab", remark: "预制体")]
        [SerializeField, Tooltip("放置时的预制体引用（若使用地址able / 资源路径，VS1 可忽略）。")]
        private GameObject prefab;
        #endregion

        #region Unlock / Progression
        [CsvField("unlockTier", remark: "解锁层级")]
        [SerializeField, Tooltip("解锁层级（科技树接入前默认 0）。")]
        private int unlockTier = 0;

        [CsvField("unlockedByDefault", remark: "默认解锁")]
        [SerializeField, Tooltip("是否默认解锁（VS1 两台机器都应 true）。")]
        private bool unlockedByDefault = true;
        #endregion

        #region Tags / Metadata
        [CsvField("tags", remark: "标签(分号;)")]
        [SerializeField, Tooltip("标签（过滤 / 模块匹配 / 统计）。示例：Smelting, Crafting, Starter")]
        private string[] tags;

        [CsvField("sortOrder", remark: "排序权重")]
        [SerializeField, Tooltip("排序权重（建造面板显示顺序；越小越靠前）。")]
        private int sortOrder = 0;
        #endregion

        #region IDefinition
        public int Id => id;
        public string CodeName => codeName;
        #endregion

        #region Public Accessors
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;
        public MachineType MachineType => machineType;
        public Vector2Int Size => size;
        public bool Rotatable => rotatable;
        public int DefaultRotation => defaultRotation;
        public float ProcessingSpeedMultiplier => processingSpeedMultiplier <= 0f ? 1f : processingSpeedMultiplier;
        public bool SingleQueue => singleQueue;
        public int InputSlotCapacity => Mathf.Max(1, inputSlotCapacity);
        public int OutputSlotCapacity => Mathf.Max(1, outputSlotCapacity);
        public bool AutoPullInputs => autoPullInputs;
        public bool AutoPushOutputs => autoPushOutputs;
        public bool RequiresPower => requiresPower;
        public float BasePowerConsumption => basePowerConsumption;
        public bool IdleConsumesPower => idleConsumesPower;
        public bool UseWhitelist => useWhitelist;
        public IReadOnlyList<int> RecipeWhitelistIds => recipeWhitelistIds ?? Array.Empty<int>();
        public IReadOnlyList<int> RecipeBlacklistIds => recipeBlacklistIds ?? Array.Empty<int>();
        public bool IncludeHandRecipes => includeHandRecipes;
        public Sprite Icon => icon;
        public string Description => description;
        public GameObject Prefab => prefab;
        public int UnlockTier => unlockTier;
        public bool UnlockedByDefault => unlockedByDefault;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public int SortOrder => sortOrder;
        #endregion

        #region Helper Methods
        public bool HasTag(string tag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        public bool AllowsRecipe(int recipeId)
        {
            if (useWhitelist)
            {
                if (recipeWhitelistIds == null || recipeWhitelistIds.Length == 0) return false;
                for (int i = 0; i < recipeWhitelistIds.Length; i++)
                    if (recipeWhitelistIds[i] == recipeId) return true;
                return false;
            }
            if (recipeBlacklistIds != null && recipeBlacklistIds.Length > 0)
            {
                for (int i = 0; i < recipeBlacklistIds.Length; i++)
                    if (recipeBlacklistIds[i] == recipeId) return false;
            }
            return true;
        }
        #endregion

#if UNITY_EDITOR
        #region Validation (Editor Only)
        private void OnValidate()
        {
            if (id < 0) id = 0;
            if (!string.IsNullOrEmpty(codeName)) codeName = codeName.Trim();
            if (size.x < 1) size.x = 1;
            if (size.y < 1) size.y = 1;
            if (processingSpeedMultiplier <= 0f) processingSpeedMultiplier = 1f;
            if (inputSlotCapacity < 1) inputSlotCapacity = 1;
            if (outputSlotCapacity < 1) outputSlotCapacity = 1;

            if (tags != null && tags.Length > 0)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] != null) tags[i] = tags[i].Trim();
                }
            }

            if (useWhitelist && (recipeWhitelistIds == null || recipeWhitelistIds.Length == 0))
            {
                // 提示：白名单模式但白名单为空
            }
        }
        #endregion
#endif
    }

    public enum MachineType
    {
        Furnace = 0,
        Assembler = 1,
        Hand = 2
    }
}