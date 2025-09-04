using System;

namespace Game.CSV
{
    /// <summary>
    /// 标记一个定义类(实现 IDefinition)支持 CSV 代码生成。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CsvDefinitionAttribute : Attribute
    {
        /// <summary>数组字段默认分隔符，若 CsvField 未指定 CustomArraySeparator，则使用这里。</summary>
        public char ArraySeparator { get; set; } = ';';

        /// <summary>是否为该定义自动生成 Section 适配 partial（实现 Import/Export）。</summary>
        public bool GenerateSectionAdapters { get; set; } = true;

        public CsvDefinitionAttribute() {}
    }

    /// <summary>
    /// 标记一个字段作为 CSV 列。
    /// 仅支持字段（推荐 private [SerializeField]），不支持属性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class CsvFieldAttribute : Attribute
    {
        /// <summary>列英文名（程序用，第二行写出）。</summary>
        public string Column;

        /// <summary>是否必填（导入时校验）。</summary>
        public bool Required;

        /// <summary>导出时忽略该列。</summary>
        public bool IgnoreExport;

        /// <summary>导入时忽略该列。</summary>
        public bool IgnoreImport;

        /// <summary>数组分隔符（覆盖 CsvDefinitionAttribute.ArraySeparator）。</summary>
        public string CustomArraySeparator;

        /// <summary>资产引用模式。</summary>
        public AssetRefMode AssetMode = AssetRefMode.None;

        /// <summary>资产引用额外列（如同时写 guid 与 path）。</summary>
        public string AssetColumn;

        /// <summary>允许用空字符串覆盖已有值（否则空=忽略）。</summary>
        public bool AllowEmptyOverwrite;

        /// <summary>中文/本地化备注（第一行写出，用于给设计查看）。</summary>
        public string Remark;

        public CsvFieldAttribute(string column = null, string remark = null)
        {
            Column = column;
            Remark = remark;
        }
    }

    public enum AssetRefMode
    {
        None = 0,
        Guid = 1,
        Path = 2,
        AddressKey = 3,
        Custom = 9
    }
}
