using Game.Data.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data.Definition.Items
{
    /// <summary>
    /// 物品静态定义（数据驱动，VS1 最小 + 预留扩展位）。
    /// - 不继承 ScriptableObject：由 Section(DefinitionSectionBase) 持有。
    /// - 仅保存“静态”描述，不包含运行时状态（数量 / 耐久等）。
    /// - 避免放入业务逻辑；只做数据与基础校验。
    /// 生成器字段命名说明：
    ///   Id / codeName / displayName 符合 ConfigIdCodeGenerator 的名称收集优先级。
    /// 注意：
    ///   修改 codeName / Id 会影响生成常量与存档兼容，应走迁移流程。
    /// </summary>
    [Serializable]
    public class BaseItemDefinition : IDefinition
    {
        #region Core Identity (必须字段)
        [SerializeField, Tooltip("全局唯一 Id（>0）。生成常量引用 ConfigIds.ItemDefinition.X")]
        private int id;

        [SerializeField, Tooltip("内部代码名（稳定标识，推荐 PascalCase 或 下划线风格，不含空格）。")]
        private string codeName;

        [SerializeField, Tooltip("展示名称（可本地化）。")]
        private string displayName;
        #endregion

        #region Classification
        [SerializeField, Tooltip("物品分类（用于配方过滤 / UI 分组 / 逻辑判断）。")]
        private ItemCategory category = ItemCategory.Resource;

        [SerializeField, Tooltip("稀有度（可选，VS1 暂不使用，可用于掉落颜色等）。")]
        private ItemRarity rarity = ItemRarity.Common;

        [SerializeField, Tooltip("排序权重（同类别内显示顺序，越小越靠前）。")]
        private int sortOrder = 0;
        #endregion

        #region Stack & Handling
        [SerializeField, Min(1), Tooltip("最大堆叠数量（=1 表示不可堆叠）。")]
        private int maxStack = 100;

        [SerializeField, Tooltip("是否允许丢弃（VS1 可统一 true，扩展用）。")]
        private bool droppable = true;

        [SerializeField, Tooltip("是否允许放入通用背包（某些内部技术物品可关闭避免玩家拿到）。")]
        private bool storable = true;
        #endregion

        #region Economy / Progression (预留)
        [SerializeField, Tooltip("基础价值（货币系统引入前作为占位）。")]
        private int baseValue = 0;

        [SerializeField, Tooltip("解锁层级（未来科技树 gating；VS1 可保持 0）。")]
        private int unlockTier = 0;
        #endregion

        #region Visual / Presentation
        [SerializeField, Tooltip("主图标（UI 显示）。")]
        private Sprite icon;

        [SerializeField, TextArea(2,4), Tooltip("描述文本（提示 / 风味）。")]
        private string description;
        #endregion

        #region Tag System (扩展)
        [SerializeField, Tooltip("标签（轻量型匹配：如 Fuel / Metal / Organic）。纯字符串避免枚举膨胀。")]
        private string[] tags;
        #endregion

        #region Interface (IDefinition & 公共只读访问)
        public int Id => id;
        public string CodeName => codeName;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? codeName : displayName;
        public ItemCategory Category => category;
        public ItemRarity Rarity => rarity;
        public int SortOrder => sortOrder;
        public int MaxStack => maxStack;
        public bool Droppable => droppable;
        public bool Storable => storable;
        public int BaseValue => baseValue;
        public int UnlockTier => unlockTier;
        public Sprite Icon => icon;
        public string Description => description;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public bool IsStackable => maxStack > 1;
        #endregion

        #region Validation & Utility (Editor Only)
#if UNITY_EDITOR
        /// <summary>
        /// OnValidate 仅在编辑器内运行：做基础健壮性与安全修正。
        /// </summary>
        private void OnValidate()
        {
            if (id < 0)
                id = 0; // 由 Section 校验重复与是否为 0

            if (maxStack < 1)
                maxStack = 1;

            // 规范 codeName：去前后空格
            if (!string.IsNullOrEmpty(codeName))
                codeName = codeName.Trim();

            // displayName 为空时允许回退
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(codeName))
                displayName = codeName;

            // 去除空 Tag
            if (tags != null && tags.Length > 0)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] != null) tags[i] = tags[i].Trim();
                }
            }
        }
#endif
        #endregion

        #region Helper Predicates
        /// <summary>是否包含给定标签（大小写精确匹配）。</summary>
        public bool HasTag(string tag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag) return true;
            return false;
        }
        #endregion
    }

    /// <summary>物品分类（VS1 约束范围）。</summary>
    public enum ItemCategory
    {
        Resource = 0,
        Intermediate = 1,
        Key = 2,
        RareDrop = 3,
        Tool = 4
    }

    /// <summary>稀有度（VS1 可不使用，预留未来 UI / 掉落加权）。</summary>
    public enum ItemRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }
}