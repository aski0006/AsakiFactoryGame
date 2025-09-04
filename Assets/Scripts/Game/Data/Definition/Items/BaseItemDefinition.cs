// Assets/Scripts/Game/Data/Definition/Items/BaseItemDefinition.cs

using Game.CSV;
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
    [CsvDefinition]
    public partial class BaseItemDefinition : IDefinition
    {
        #region Core Identity

        [CsvField("id", remark: "唯一ID", Required = true)]
        [SerializeField, Tooltip("全局唯一 Id（>0）。生成常量引用 ConfigIds.ItemDefinition.X")]
        private int id;

        [CsvField("codeName", remark: "内部代码名", Required = true)]
        [SerializeField, Tooltip("内部代码名（稳定标识）")]
        private string codeName;

        [CsvField("displayName", remark: "显示名称")]
        [SerializeField, Tooltip("展示名称（可本地化）。")]
        private string displayName;

        #endregion

        #region Classification

        [CsvField("category", remark: "物品分类")]
        [SerializeField, Tooltip("物品分类")]
        private ItemCategory category = ItemCategory.Resource;

        [CsvField("rarity", remark: "稀有度")]
        [SerializeField, Tooltip("稀有度")]
        private ItemRarity rarity = ItemRarity.Common;

        [CsvField("sortOrder", remark: "排序权重")]
        [SerializeField, Tooltip("排序权重")]
        private int sortOrder = 0;

        #endregion

        #region Stack & Handling

        [CsvField("maxStack", remark: "最大堆叠")]
        [SerializeField, Min(1), Tooltip("最大堆叠")]
        private int maxStack = 100;

        [CsvField("droppable", remark: "可丢弃")]
        [SerializeField, Tooltip("是否可丢弃")]
        private bool droppable = true;

        [CsvField("storable", remark: "可进通用背包")]
        [SerializeField, Tooltip("是否可放入通用背包")]
        private bool storable = true;

        #endregion

        #region Economy

        [CsvField("baseValue", remark: "基础价值")]
        [SerializeField, Tooltip("基础价值")]
        private int baseValue = 0;

        [CsvField("unlockTier", remark: "解锁层级")]
        [SerializeField, Tooltip("解锁层级")]
        private int unlockTier = 0;

        #endregion

        #region Visual

        [CsvField("iconGuid", remark: "图标GUID", AssetMode = AssetRefMode.Guid)]
        [SerializeField, Tooltip("主图标")]
        private Sprite icon;

        [CsvField("description", remark: "描述")]
        [SerializeField, TextArea(2, 4), Tooltip("描述文本")]
        private string description;

        #endregion

        #region Tags

        [CsvField("tags", remark: "标签(分号;分隔)", CustomArraySeparator = ";")] // 使用 CsvDefinition(ArraySeparator=';') 的默认分隔
        [SerializeField, Tooltip("标签列表")]
        private string[] tags;

        #endregion

        #region Public Readonly

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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (id < 0) id = 0;
            if (maxStack < 1) maxStack = 1;

            if (!string.IsNullOrEmpty(codeName)) codeName = codeName.Trim();
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(codeName))
                displayName = codeName;

            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                    if (!string.IsNullOrEmpty(tags[i]))
                        tags[i] = tags[i].Trim();
            }
        }
#endif

        public bool HasTag(string tag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag)
                    return true;
            return false;
        }
    }
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
