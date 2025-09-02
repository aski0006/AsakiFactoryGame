# 集中管理 ScriptableObject 数据库 (Config Database)

## 目标
- 统一查看 / 搜索 / 多选管理带 [CustomConfig] 标记的 ScriptableObject
- 支持批量操作：创建、删除、复制、移动、重命名、导出 JSON、添加/移除标签
- 支持复选框多选 + 反选/全选
- 内嵌 Inspector（单选或多选逐个显示）
- 窗口状态持久化 (最后选中类型、搜索词、操作参数)
- 可扩展（标签批量编辑、导入、校验、引用分析）

## 目录结构建议
```
Assets/
  Editor/
    ConfigDatabase/
      CustomConfigAttribute.cs
      ConfigDatabaseState.cs
      ConfigTypeCache.cs
      ConfigDatabaseWindow.cs
      README_ConfigDatabase.md
```

## 使用步骤
1. 在你的配置类前加上属性：
   ```csharp
   [CustomConfig]
   public class EnemyConfig : ScriptableObject {
       public int hp;
       public float speed;
   }
   ```
2. 打开窗口：Tools → Config DB → Config Database
3. 左侧类型列表自动出现所有被标记的 ScriptableObject 子类
4. 选择类型 → 中间列表显示该类型所有资源（支持搜索）
5. 勾选资产复选框进行多选
6. 使用顶部“批量操作”执行相应功能
7. 右侧内嵌 Inspector 可直接修改内容（修改后记得保存工程或 Apply）

## 状态持久化
窗口状态存储在：
`Assets/Editor/ConfigDatabase/ConfigDatabaseState.asset`
包含：
- selectedTypeIndex
- searchText / caseSensitive
- 各批量操作参数 (新建目录 / 移动目录 / 导出目录 / 重命名模板等)
- 每类型的多选 GUID 集合
- 是否显示预览、标签、嵌入 Inspector

## 批量操作说明
| 操作 | 描述 |
|------|------|
| 复制选中 | 在新建目录中生成拷贝（名称加 _Copy 或其它模板逻辑） |
| 删除选中 | 可配置是否确认 |
| 移动到目录 | 将资产文件移动到配置中的 lastMoveFolder |
| 批量重命名 | 使用重命名模板：{name} 原名 / {guid} GUID前8位 / {time} 当前HHmmss |
| 导出 JSON | 把选中的资源序列化为 JSON 写到 lastExportFolder |
| 添加标签 | 示例里演示写死，实际可自扩展输入弹窗 |
| 移除标签 | 同上，示例中写死标签名 (SampleTag) |

## 重命名模板示例
| 模板 | 结果示例 |
|------|----------|
| {name}_Copy | EnemyConfig_Copy |
| NEW_{name}_{time} | NEW_EnemyConfig_152233 |
| {name}_{guid} | EnemyConfig_1a2b3c4d |

## 快速扩展建议
| 需求 | 思路 |
|------|------|
| 校验引用 | 遍历 SerializedObject 查找引用类型字段 |
| JSON 导入 | 解析 JSON -> CreateInstance -> 覆盖字段 -> 保存 |
| Diff 比较 | 生成两个 JSON 的行级差异显示 |
| 标签 UI | 通过 AssetDatabase.GetLabels / SetLabels 增强输入 |
| 多类型批量 | 取消按类型隔离（高级模式） |
| 分页 | 资源超多时加入“分页大小 + 当前页” |
| 异步扫描 | 使用 EditorApplication.update 分帧加载 |

## 性能 / 注意事项
- 反射扫描类型每 10 秒自动刷新，可手动“刷新类型”
- 大量资源时列表刷新可能较慢，可后续做延迟刷新或分页
- 重命名 / 移动 / 删除后会自动刷新
- 导出 JSON 使用 JsonUtility，无法处理复杂多态 / 字典，可换 Newtonsoft

## 常见问题
Q: 新加了 [CustomConfig] 的类没出现？  
A: 点击“刷新类型”或脚本重新编译。

Q: 重命名模板失效？  
A: 确认模板中变量写法正确：{name} / {guid} / {time}。

Q: 多选后 Inspector 编辑显示不全？  
A: 目前多选 Inspector 为简化实现：逐个前 N（示例限制）资产显示。可扩展为真正的 Multi-Edit（同步字段更新所有）。

Q: 想阻止用户错误删除？  
A: 在 ConfigDatabaseState 中保持 confirmBeforeDelete = true，并考虑加入“回收站”逻辑（移动到 Archive 目录而非直接删）。

## 代码内可快速定位扩展点
| 代码区域 | 说明 |
|----------|------|
| ShowBatchMenu() | 批量操作菜单扩展入口 |
| BatchXXX() 系列 | 各批量操作实现 |
| DrawInspectorPanel() | 内嵌 Inspector 渲染逻辑 |
| RefreshAssetList() | 应用搜索过滤 |
| ConfigTypeCache | 类型扫描 (添加缓存策略或排除规则) |

## 安全建议
- 如果以后支持热更新或外部 JSON 导入，需做字段存在性与类型校验
- 批量操作前可加入 Undo.RecordObject 以整合 Unity Undo 系统

祝你开发顺利，若需要：
- 真正多选同步编辑版本
- JSON 导入功能
- 自动引用校验/重命名联动
- 资源依赖图可视化

随时继续提出需求。