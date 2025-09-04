# CSV 模块使用说明

## 1. 概述

CSV模块提供了一套完整的CSV与ScriptableObject之间的导入/导出机制，支持通过CSV文件快速配置游戏数据，并能将配置好的ScriptableObject数据导出为CSV进行备份或编辑。该模块核心是通过接口定义和代码生成实现自动化的数据绑定。

## 2. 核心接口

### 2.1 ICsvImportableConfig
标记ScriptableObject支持从CSV导入数据，定义了导入过程的四个关键阶段：

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
    // 基础类型转换方法
    bool TryParseInt(string s, out int v);
    bool TryParseFloat(string s, out float v);
    bool TryParseBool(string s, out bool v);
    bool TryParseEnum<T>(string s, out T v) where T : struct;
    string Normalize(string s);

    // Unity 类型解析
    bool TryParseVector2(string s, out Vector2 v);
    bool TryParseVector3(string s, out Vector3 v);
    bool TryParseVector4(string s, out Vector4 v);
    bool TryParseVector2Int(string s, out Vector2Int v);
    bool TryParseVector3Int(string s, out Vector3Int v);
    bool TryParseQuaternion(string s, out Quaternion q);
    bool TryParseColor(string s, out Color c);
    
    // 资源加载方法
    T LoadByGuid<T>(string guid) where T : Object;
    T LoadByPath<T>(string path) where T : Object;
    Sprite LoadSpriteByGuid(string guid, string spriteName = null);
    Sprite LoadSpriteByPath(string path, string spriteName = null);
    GameObject LoadPrefabByGuid(string guid, string prefabName = null);
    GameObject LoadPrefabByPath(string path, string prefabName = null);
    Transform LoadTransformByScenePath(string scenePath);
    Transform LoadTransformFromPrefab(string prefabGuid, string childPath = null);
    
    // 日志方法
    void LogWarning(int line, string msg);
    void LogError(int line, string msg);
}
```

### 2.4 ICsvRemarkProvider
提供CSV中文备注行（第一行），与GetCsvHeader对应：

```csharp
public interface ICsvRemarkProvider
{
    IReadOnlyList<string> GetCsvRemarks();
}
```

### 2.5 ICsvTypeConverter
用于自定义类型的序列化与反序列化：

```csharp
public interface ICsvTypeConverter
{
    Type TargetType { get; }
    string Serialize(object value);
    bool TryDeserialize(string text, out object value);
}

// 泛型辅助基类
public abstract class CsvTypeConverter<T> : ICsvTypeConverter
{
    public Type TargetType => typeof(T);
    public string Serialize(object value);
    public bool TryDeserialize(string text, out object value);
    protected abstract string SerializeTyped(T value);
    protected abstract bool TryDeserializeTyped(string text, out T value);
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
- `AssetColumn`：资产引用额外列（如同时写guid与path）
- `AllowEmptyOverwrite`：允许用空字符串覆盖已有值
- `Remark`：中文/本地化备注（第一行写出，用于给设计查看）

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
- 向量类型支持多种格式：`x,y,z`、`(x,y,z)`、`[x,y,z]` 或 `{x,y,z}`
- 颜色支持十六进制（`#RRGGBB`、`#RRGGBBAA`）和RGB/RGBA格式（`r,g,b` 或 `r,g,b,a`）

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

### 6.2 自定义类型转换器
通过实现 `CsvTypeConverter<T>` 并注册到 `CsvTypeConverterRegistry`，可支持自定义类型的序列化与反序列化：

```csharp
public class DateTimeConverter : CsvTypeConverter<DateTime>
{
    protected override string SerializeTyped(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    protected override bool TryDeserializeTyped(string text, out DateTime value)
    {
        return DateTime.TryParse(text, out value);
    }
}

// 注册转换器
CsvTypeConverterRegistry.Register(new DateTimeConverter());
```

### 6.3 扩展代码生成器
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
- 确认必填字段是否都提供了值

### 7.2 导出数据不完整
- 检查是否有字段标记了 `IgnoreExport`
- 确保所有需要导出的字段都标记了 `CsvField` 特性
- 检查自定义导出逻辑是否正确
- 确认集合类型是否正确处理了所有元素

### 7.3 数组导入/导出异常
- 确认使用的分隔符与定义一致
- 检查数组元素是否包含分隔符（需特殊处理）
- 确保数组元素的格式符合要求
- 对于自定义类型数组，确认已注册相应的类型转换器

### 7.4 资源引用无法加载
- 检查资源GUID或路径是否正确
- 确认资源存在于项目中且未被移动或删除
- 对于Sprite，确认是否需要指定图集中的子Sprite名称
- 检查资源是否被正确导入到Unity项目中

### 7.5 特殊字符处理问题
- 包含逗号、引号或换行符的字段必须用双引号包裹
- 字段内的双引号需要替换为两个双引号（`""`）
- 导出时系统会自动处理特殊字符，但手动编辑CSV时需注意遵循格式要求

## 8. 工具类说明

### 8.1 CsvExportUtility
提供CSV文件导出功能，支持备注行、特殊字符转义：

```csharp
// 导出CSV
CsvExportUtility.Write(
    path,
    header,       // 列头
    rows,         // 数据行
    remarks,      // 备注行（可选）
    utf8Bom: true // 是否包含UTF8 BOM
);
```

### 8.2 SimpleCsvParser
提供CSV行解析功能，支持双引号包裹和转义：

```csharp
// 解析单行CSV
List<string> fields = SimpleCsvParser.ParseLine(line);
```

### 8.3 CsvTypeConverterRegistry
管理类型转换器的注册与查询：

```csharp
// 注册转换器
CsvTypeConverterRegistry.Register(new MyCustomConverter());

// 序列化对象
if (CsvTypeConverterRegistry.TrySerialize(typeof(MyType), value, out string text))
{
    // 处理序列化结果
}
```

### 8.4 CsvImportHelper
提供类型转换和资源加载的默认实现，可在自定义导入逻辑中使用：

```csharp
var helper = new CsvImportHelper("配置名称");
if (helper.TryParseInt(row["id"], out int id))
{
    // 处理ID
}
```