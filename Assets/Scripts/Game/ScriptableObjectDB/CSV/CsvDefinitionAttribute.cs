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
        public string Column;
        public bool Required;
        public bool IgnoreExport;
        public bool IgnoreImport;
        public string CustomArraySeparator;
        public AssetRefMode AssetMode = AssetRefMode.None;
        public string AssetColumn;
        public bool AllowEmptyOverwrite;

        public CsvFieldAttribute(string column = null) => Column = column;
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
