using Game.Data.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

// 若需要引用物品分类过滤
// 若需要与配方声明解耦（可选）

// 若已有 MachineType 枚举放在该命名空间

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
    /// 修改兼容提示：
    ///   - Id 变更：需迁移存档与常量引用
    ///   - codeName 变更：重新生成常量并替换代码引用
    /// </summary>
    [Serializable]
    public class BaseMachineDefinition : IDefinition
    {
        #region Identity
        [SerializeField, Tooltip("全局唯一 Id (>0)。")]
        private int id;

        [SerializeField, Tooltip("内部代码名（稳定英文标识，用于生成常量）。")]
        private string codeName;

        [SerializeField, Tooltip("展示名称（UI 友好文本，若为空回退 codeName）。")]
        private string displayName;
        #endregion

        #region Core Machine Attributes
        [SerializeField, Tooltip("机器类别（决定可用配方集合的第一层过滤）。")]
        private MachineType machineType = MachineType.Assembler;

        [SerializeField, Tooltip("占用网格尺寸（W×H，统一整格制）。")]
        private Vector2Int size = new Vector2Int(2, 2);

        [SerializeField, Tooltip("是否允许旋转（若 false 则始终使用默认朝向）。")]
        private bool rotatable = true;

        [SerializeField, Tooltip("默认朝向（0=Up/或你的坐标系定义，可与放置系统协同）。")]
        private int defaultRotation = 0;
        #endregion

        #region Processing & Capacity
        [SerializeField, Min(0.01f), Tooltip("基础处理倍速（1 = 配方原始耗时；2 = 时间减半）。仅 VS1 暂不使用可保持 1。")]
        private float processingSpeedMultiplier = 1f;

        [SerializeField, Tooltip("是否只允许串行配方（VS1 固定 true；预留并行框架）。")]
        private bool singleQueue = true;

        [SerializeField, Min(1), Tooltip("输入缓冲最大槽位（VS1 建议 4）。")]
        private int inputSlotCapacity = 4;

        [SerializeField, Min(1), Tooltip("输出缓冲最大槽位（VS1 建议 2）。")]
        private int outputSlotCapacity = 2;

        [SerializeField, Tooltip("是否自动拉取周围输入（VS1 不实现，仅占位）。")]
        private bool autoPullInputs = false;

        [SerializeField, Tooltip("是否自动推送输出（VS1 不实现，仅占位）。")]
        private bool autoPushOutputs = false;
        #endregion

        #region Power / Future Systems (占位)
        [SerializeField, Tooltip("是否需要电力（VS1=False；后续引入电力系统时启用）。")]
        private bool requiresPower = false;

        [SerializeField, Tooltip("基础耗电（功率单位：kW 或自定义；VS1 未使用）。")]
        private float basePowerConsumption = 0f;

        [SerializeField, Tooltip("空转是否耗电（VS1 未使用）。")]
        private bool idleConsumesPower = false;
        #endregion

        #region Recipe Filtering (扩展策略)
        [SerializeField, Tooltip("是否限制为一个显式白名单（若列表为空且 useWhitelist=true 则无可用配方）。")]
        private bool useWhitelist = false;

        [SerializeField, Tooltip("可用配方白名单 Id 列表（与 useWhitelist 配合）。")]
        private int[] recipeWhitelistIds = Array.Empty<int>();

        [SerializeField, Tooltip("可选：黑名单（useWhitelist=false 时可排除个别配方）。")]
        private int[] recipeBlacklistIds = Array.Empty<int>();

        [SerializeField, Tooltip("是否允许手工配方（MachineType.Hand）也在此机器 UI 中展示。")]
        private bool includeHandRecipes = false;
        #endregion

        #region UI / Visual / Audio
        [SerializeField, Tooltip("主图标（放置面板 / 建筑选择 UI）。")]
        private Sprite icon;

        [SerializeField, TextArea(2,4), Tooltip("描述文本 / 使用提示 / 解锁说明。")]
        private string description;

        [SerializeField, Tooltip("放置时的预制体引用（若使用地址able / 资源路径，VS1 可忽略）。")]
        private GameObject prefab;

        [SerializeField, Tooltip("加工完成特效 / 动画触发标记（VS1 不用）。")]
        private string completeEffectKey;

        [SerializeField, Tooltip("是否在简易调试/构建面板中隐藏。")]
        private bool hideInBuildMenu = false;
        #endregion

        #region Unlock / Progression
        [SerializeField, Tooltip("解锁层级（科技树接入前默认 0）。")]
        private int unlockTier = 0;

        [SerializeField, Tooltip("是否默认解锁（VS1 两台机器都应 true）。")]
        private bool unlockedByDefault = true;
        #endregion

        #region Tags / Metadata
        [SerializeField, Tooltip("标签（过滤 / 模块匹配 / 统计）。示例：Smelting, Crafting, Starter")]
        private string[] tags;

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
        public string CompleteEffectKey => completeEffectKey;
        public bool HideInBuildMenu => hideInBuildMenu;
        public int UnlockTier => unlockTier;
        public bool UnlockedByDefault => unlockedByDefault;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public int SortOrder => sortOrder;
        #endregion

        #region Helper Methods

        /// <summary>是否拥有指定标签（大小写精确匹配）。</summary>
        public bool HasTag(string tag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag) return true;
            return false;
        }

        /// <summary>判定某配方是否允许（仅在需要时调用；可由缓存优化）。</summary>
        public bool AllowsRecipe(int recipeId)
        {
            if (useWhitelist)
            {
                if (recipeWhitelistIds == null || recipeWhitelistIds.Length == 0) return false;
                for (int i = 0; i < recipeWhitelistIds.Length; i++)
                    if (recipeWhitelistIds[i] == recipeId) return true;
                return false;
            }
            // 黑名单
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

            // 清理标签空白
            if (tags != null && tags.Length > 0)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] != null) tags[i] = tags[i].Trim();
                }
            }

            // 白名单模式但白名单为空 -> 明确提示（不直接填充，保持设计显性）
            if (useWhitelist && (recipeWhitelistIds == null || recipeWhitelistIds.Length == 0))
            {
                // Debug.LogWarning($"[BaseMachineDefinition:{codeName}] 使用白名单但列表为空，将导致无可用配方。");
            }
        }
        #endregion
#endif
    }

    /// <summary>
    /// 机器类型枚举（若你已经在别处定义，可删除本枚举避免重复）。
    /// </summary>
    public enum MachineType
    {
        Furnace = 0,
        Assembler = 1,
        Hand = 2   // 手工合成虚拟类型（不一定需要实体机器）
    }
}