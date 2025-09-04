# CSV 模块使用说明

## 1. 概述

CSV模块提供了一套完整的CSV与ScriptableObject之间的导入/导出机制，支持通过CSV文件快速配置游戏数据，并能将配置好的ScriptableObject数据导出为CSV进行备份或编辑。该模块核心是通过接口定义和代码生成实现自动化的数据绑定。

## 2. 核心接口

### 2.1 ICsvImportableConfig
标记ScriptableObject支持从CSV导入数据，定义了导入过程的三个关键阶段：

```csharp
public interface ICsvImportableConfig
{
    // 返回期望的列集合（用于校验）
    IReadOnlyList<string> GetExpectedColumns();
    
    // 导入前准备（如清空现有数据）
    void PrepareForCsvImport(bool clearExisting);
    
    // 导入单行数据
    bool ImportCsvRow(Dictionary<string, string> row, ICsvImportHelper helper, int lineIndex);
    
    // 导入完成后处理（如排序、校验）
    void FinalizeCsvImport(int success, int error);
}
```

### 2.2 ICsvExportableConfig
标记ScriptableObject支持导出为CSV，定义了导出所需的接口：

```csharp
public interface ICsvExportableConfig
{
    // 返回导出的列头顺序
    IReadOnlyList<string> GetCsvHeader();
    
    // 生成所有行数据
    IEnumerable<Dictionary<string, string>> ExportCsvRows();
}
```

### 2.3 ICsvImportHelper
提供CSV导入过程中的类型转换与资源加载辅助：

```csharp
public interface ICsvImportHelper
{
    // 类型转换方法
    bool TryParseInt(string s, out int v);
    bool TryParseFloat(string s, out float v);
    bool TryParseBool(string s, out bool v);
    bool TryParseEnum<T>(string s, out T v) where T : struct;
    
    // 资源加载方法
    T LoadByGuid<T>(string guid) where T : Object;
    T LoadByPath<T>(string path) where T : Object;
    Sprite LoadSpriteByGuid(string guid, string spriteName = null);
    // 其他资源加载方法...
    
    // 日志方法
    void LogWarning(int line, string msg);
    void LogError(int line, string msg);
}
```

## 3. 代码生成配置

### 3.1 特性标记
通过特性标记需要生成CSV绑定代码的类和字段：

```csharp
// 标记定义类支持CSV代码生成
[CsvDefinition(ArraySeparator = ';')]
public class ItemDefinition : IDefinition
{
    [CsvField("id", Required = true)]
    [SerializeField] private int _id;
    
    [CsvField("name")]
    [SerializeField] private string _name;
    
    [CsvField("tags", CustomArraySeparator = ",")]
    [SerializeField] private string[] _tags;
    
    // 其他字段...
}
```

`CsvDefinitionAttribute` 特性参数：
- `ArraySeparator`：数组字段的默认分隔符
- `GenerateSectionAdapters`：是否自动生成Section适配代码

`CsvFieldAttribute` 特性参数：
- `Column`：CSV列名
- `Required`：是否为必填字段
- `IgnoreExport`：导出时忽略该字段
- `IgnoreImport`：导入时忽略该字段
- `CustomArraySeparator`：数组的自定义分隔符
- `AssetMode`：资源引用模式（GUID/Path等）

### 3.2 代码生成设置
通过 `CsvCodeGenSettings` 配置代码生成：

```csharp
[CreateAssetMenu(fileName = "CsvCodeGenSettings", menuName = "Game/CSV/CodeGen Settings")]
public class CsvCodeGenSettings : ScriptableObject
{
    public string outputFolder = "Assets/Generated/CsvBindings";
    public bool verbose = true;
    public bool generateEditorSetters = true;
}
```

## 4. 使用流程

### 4.1 从CSV导入数据
1. 在Unity编辑器中打开ConfigDatabase窗口
2. 选择需要导入数据的ScriptableObject
3. 右键选择 "CSV/从文件导入 (覆盖)" 或 "CSV/从文件导入 (合并追加)"
4. 选择CSV文件完成导入

导入过程会：
- 验证CSV列与预期列是否匹配
- 逐行解析CSV数据并转换为对应字段
- 处理资源引用（通过GUID或路径加载）
- 导入完成后进行排序和校验

### 4.2 导出数据为CSV
1. 在Unity编辑器中打开ConfigDatabase窗口
2. 选择需要导出数据的ScriptableObject
3. 右键选择 "CSV/导出当前为CSV"
4. 选择保存路径完成导出

导出过程会：
- 按照定义的列头顺序生成CSV
- 处理数组类型（使用指定分隔符）
- 处理资源引用（导出GUID或路径）
- 对特殊字符（逗号、引号、换行）进行转义

## 5. CSV格式要求

- 第一行为列头，列名需与代码中定义的保持一致（大小写不敏感）
- 数组类型字段使用指定分隔符（默认为`;`）
- 布尔值支持：`true`/`false`、`1`/`0`、`yes`/`no`
- 资源引用可使用GUID或资源路径
- 包含特殊字符（逗号、引号、换行）的字段需要用双引号包裹
- 双引号在字段内部需要替换为两个双引号（`""`）

## 6. 扩展与自定义

### 6.1 自定义导入/导出逻辑
通过实现 `ICsvImportableConfig` 和 `ICsvExportableConfig` 接口，可自定义导入/导出逻辑：

```csharp
public class CustomConfig : ScriptableObject, ICsvImportableConfig, ICsvExportableConfig
{
    // 实现接口方法...
    
    public bool ImportCsvRow(Dictionary<string, string> row, ICsvImportHelper helper, int lineIndex)
    {
        // 自定义单行导入逻辑
    }
    
    public IEnumerable<Dictionary<string, string>> ExportCsvRows()
    {
        // 自定义导出逻辑
    }
}
```

### 6.2 扩展代码生成器
代码生成器支持扩展以处理更多类型或自定义逻辑，可通过修改 `CsvBindingGeneratorMenu` 类实现：

- 添加新的类型处理逻辑
- 自定义生成代码的格式
- 增加额外的代码生成步骤

## 7. 常见问题

### 7.1 导入失败
- 检查CSV列名是否与预期列匹配
- 验证数据格式是否正确（特别是数字、日期等）
- 检查资源引用是否有效（GUID或路径是否正确）
- 查看控制台错误信息，定位具体错误行

### 7.2 导出数据不完整
- 检查是否有字段标记了 `IgnoreExport`
- 确保所有需要导出的字段都标记了 `CsvField` 特性
- 检查自定义导出逻辑是否正确

### 7.3 数组导入/导出异常
- 确认使用的分隔符与定义一致
- 检查数组元素是否包含分隔符（需特殊处理）