# ID 代码生成器使用说明

## 1. 功能
扫描所有实现 IContextSection 的 Section 资产，抽取其中的 IDefinition 条目，生成集中常量 `ConfigIds.g.cs`，用于避免在运行时代码中硬编码数字 Id。

## 2. 菜单
Tools / Config DB / Generate Config Id Constants

## 3. 生成位置
`Assets/Scripts/Generated/ConfigIds.g.cs`  
若目录不存在会自动创建。

## 4. 常量结构
```csharp
public static partial class ConfigIds {
    public static class ItemDefinition {
        public const int Wood = 1;
        public const int Stone = 2;
    }
}
```

## 5. 命名规则
字段优先级：codeName > internalName > name > resourceName > displayName > editorName > EditorName  
无则使用 `<TypeName>_<Id>`  
再做：
- 非字母数字替换为 `_`
- 分词转 PascalCase
- 开头为数字补 `_`
- 冲突自动追加 `_<Id>`

## 6. 反查
编辑器/开发构建下提供 `ConfigIds.TryGetConstName(defType, id)` 反查常量名。

## 7. 注意
- 修改定义 Id 后需重新生成。
- 生成文件带有 auto-generated 头。
- 想扩展可写另一个 `partial class ConfigIds`，不要改生成文件。

## 8. 可扩展方向
- 输出枚举（enum）版本
- 导出 JSON 对应表
- 生成哈希辅助类
- 与外部表（Excel/CSV）对齐校验

## 9. 常见问题
| 问题 | 说明 |
| ---- | ---- |
| 名称冲突追加 _Id | 不同条目归一化后同名 |
| 未生成某类型 | 确认该 Section 资产 definitions 列表非空 |
| 想要抛异常而不是容错 | 修改生成脚本中冲突处理逻辑 |

## 10. 再生成功能建议
若后续需要“增量更新”，可记录上次生成快照（hash）避免不必要的磁盘写入。

---