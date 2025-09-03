# Asaki Factory Game 工具与效率提升 TODO

本文件汇总：Unity 2D 独立项目开发中可快速提升效率的 Editor / Runtime 工具计划、优先级、实施建议与脚手架代码。  
目标：逐步减少重复劳动、降低调试/资源管理成本、提升可维护性。

---

## 0. 使用说明

- 勾选方式：完成后把 `[ ]` 改成 `[x]`；如部分完成可写成 `[~]` 并在备注列注明剩余项。  
- 如需弃用某工具：在行末加 `(弃用原因: xxx)`，便于未来回顾。  
- 若实现方式与本文件规划不同，请更新“实现差异”备注。  
- 如果一个工具衍生出新的子任务，在其下面缩进添加二级复选框。

---

## 1. 优先级矩阵（简化）

| 编号                | 任务         | Impact | 成本估计      | 建议批次 |
| ----------------- | ---------- | ------ | --------- | ---- |
| 1/2/3/4/5/6/7     | Quick Wins | 高      | 0.5h ~ 4h | 第一轮  |
| 8/10/11/12/16/20  | 中高价值       | 中-高    | 1h ~ 5h   | 第二轮  |
| 13/14/15/17/18/19 | 中          | 中-偏高   | 2h ~ 6h   | 第三轮  |
| 21~25             | 趣味/附加      | 低      | 不定        | 视资源  |

---

## 2. Quick Wins （高 ROI，建议最先完成）

| 状态  | 编号  | 名称                      | 简述                        | 预估     | 备注                |
| --- | --- | ----------------------- | ------------------------- | ------ | ----------------- |
| [ ] | 1   | 批量资源重命名工具               | 统一命名规范（前缀/去空格/Regex）      | 0.5~1h | 支持 Dry Run        |
| [ ] | 2   | Sprite/Prefab 快速替换器     | 保留 Transform/引用快速换资源      | 1h     | Prefab Variant 注意 |
| [ ] | 3   | Runtime Debug Overlay   | FPS/内存/Active GOs         | 1~1.5h | 用 Define 控制编译     |
| [ ] | 4   | 2D Collider 可视化 Gizmos  | Box/Circle/Polygon 轮廓     | 1~2h   | 仅编辑器显示            |
| [ ] | 5   | CSV→ScriptableObject 生成 | 数据驱动 + ID 常量              | 2~4h   | 首版反射赋值            |
| [ ] | 6   | Build 配置快速切换器           | Dev/Test/Release 宏 & 日志级别 | 1.5h   | 后续可接 CI           |
| [ ] | 7   | 资源脏数据扫描                 | 找未引用/超尺寸/重复               | 2h     | 先做统计后做清理建议        |

---

## 3. 第二阶段（价值延伸）

| 状态  | 编号  | 名称              | 说明                   | 预估   |
| --- | --- | --------------- | -------------------- | ---- |
| [ ] | 8   | 动画事件校验器         | 检查事件方法存在性            | 1~2h |
| [ ] | 9   | 音频响度分析 (RMS)    | 标记音量不均一致资源           | 3h   |
| [ ] | 10  | 存档 JSON Diff 面板 | 快照对比调试               | 3~4h |
| [ ] | 11  | 事件/消息监听器        | 最近 N 次事件日志           | 2h   |
| [ ] | 12  | 数值调优面板          | 可视化调节 SO 参数          | 2~3h |
| [ ] | 16  | 常量代码生成          | Tags/Layers/Scenes 等 | 2h   |
| [ ] | 20  | 崩溃捕获 + 截图       | 下次启动提示               | 3~5h |

---

## 4. 第三阶段（结构与中长线）

| 状态  | 编号  | 名称         | 说明          | 预估   |
| --- | --- | ---------- | ----------- | ---- |
| [ ] | 13  | 输入录制 & 回放  | 重现复杂 bug    | 4~6h |
| [ ] | 14  | 资源体积快照对比   | 构建前后差异      | 2h   |
| [ ] | 15  | 寻路/阻挡调试着色  | Tile/格子调试   | 3h   |
| [ ] | 17  | 模块化日志系统    | 分类/级别/上传钩子  | 3~5h |
| [ ] | 18  | 简版加载系统     | 资源分组 + 异步包装 | 5h+  |
| [ ] | 19  | 状态机/AI 可视化 | 当前节点路径显示    | 4~6h |

---

## 5. 附加/锦上添花（非刚需）

| 状态  | 编号  | 名称             | 说明       |
| --- | --- | -------------- | -------- |
| [ ] | 21  | GIF/短视频录制      | 演示用途     |
| [ ] | 22  | TODO/FIXME 扫描器 | 聚合导航     |
| [ ] | 23  | UI 锚点/安全区辅助    | 自动布局调整   |
| [ ] | 24  | 资源导入规则报告       | 与约定差异高亮  |
| [ ] | 25  | Sprite 像素 Diff | A/B 差异调试 |

---

## 6. 第一周推荐执行计划（示例）

| 日   | 任务                            | 结果目标          |
| --- | ----------------------------- | ------------- |
| 晚1  | #1 重命名 + #3 Overlay           | 输出工具菜单 + 运行验证 |
| 晚2  | #2 替换器 + #4 Collider Gizmos   | 可视调试上线        |
| 晚3  | #5 CSV→SO                     | 导入一份示例表       |
| 晚4  | #6 Build Profiles + #7 资源扫描初版 | 生成扫描日志        |
| 周末  | #8 动画校验 + #10 Save Diff       | 减少运行期判错时间     |

---

## 7. 统一规范约定

- 命名空间前缀：`AsakiTools.*`
- Editor 菜单：`AsakiTools/<Category>/<ToolName>`
- 预定义宏（Scripting Define Symbols 建议）：
  - `ENABLE_RUNTIME_OVERLAY`
  - `ENABLE_EVENT_MONITOR`
  - `ENABLE_SAVE_DIFF`
- 资源命名规则（建议）：
  - Sprites：`spr_<功能>_<语义>`
  - Prefabs：`pf_<模块>_<语义>`
  - ScriptableObjects：`so_<类型>_<ID>`
- 生成代码写入路径：`Assets/Generated/`（git 可追踪；大文件或频繁更新的中间产物可加 `.gitignore`）
- 每个工具建立 README（简述 / 使用步骤 / 注意事项 / 扩展方向）

---

## 8. 风险与踩坑备忘

| 主题              | 风险               | 对策                |
| --------------- | ---------------- | ----------------- |
| 资源重命名           | 代码硬编码路径失效        | 提供 Dry Run / 统计引用 |
| 反射赋值 (CSV)      | 字段名不一致           | 规范：列名 = 字段名       |
| Overlay 性能      | 查找对象耗时           | 统计频率 ≥ 0.5s       |
| Collider Gizmos | 大量场景开销           | 仅当场景激活且开关开启       |
| 资源扫描误判          | 动态加载未引用          | 允许排除列表 (白名单)      |
| Build 切换遗忘      | Debug 宏进 Release | 构建前自动校验脚本         |
| 日志膨胀            | 控制台泛滥            | 日志分类 + 等级过滤       |

---

## 9. 后续自动化方向（规划条目）

| 状态  | 条目              | 描述                        |
| --- | --------------- | ------------------------- |
| [ ] | Pre-commit Hook | 运行资源扫描输出摘要                |
| [ ] | 构建 CLI          | `-executeMethod` 夜间批量构建   |
| [ ] | 工具注册中心          | 自动发现所有 EditorWindow 列表 UI |
| [ ] | 使用统计            | 简单记录工具调用频次，用于裁剪           |

---

## 10. 进度记录（填写示例）

| 日期         | 完成项 | 备注 / 产出 |
| ---------- | --- | ------- |
| 2025-09-__ | -   | -       |

---

## 11. Scaffold（可直接使用/调整）

以下脚手架代码可直接放入对应目录。若已实现，请在上方任务打勾并删除不再需要的参考段落或移动到 `/Docs/Archive/`。

### 11.1 批量资源重命名 (Tool #1)

```csharp
using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;

public class BatchRenameWindow : EditorWindow
{
    private string prefix = "";
    private string suffix = "";
    private string replacePattern = "";
    private string replaceTo = "";
    private bool trimSpaces = true;
    private bool dryRun = true;

    [MenuItem("AsakiTools/Assets/Batch Rename")]
    public static void Open()
    {
        GetWindow<BatchRenameWindow>("Batch Rename");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("批量重命名 (选中 Project 资源)", EditorStyles.boldLabel);
        prefix = EditorGUILayout.TextField("前缀", prefix);
        suffix = EditorGUILayout.TextField("后缀", suffix);
        replacePattern = EditorGUILayout.TextField("替换(Regex)", replacePattern);
        replaceTo = EditorGUILayout.TextField("替换为", replaceTo);
        trimSpaces = EditorGUILayout.Toggle("去除空格", trimSpaces);
        dryRun = EditorGUILayout.Toggle("Dry Run (只打印)", dryRun);

        if (GUILayout.Button("执行"))
        {
            var objs = Selection.objects;
            foreach (var obj in objs)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                string ext = System.IO.Path.GetExtension(path);

                string newName = name;
                if (trimSpaces) newName = newName.Replace(" ", "_");
                if (!string.IsNullOrEmpty(replacePattern))
                {
                    try { newName = Regex.Replace(newName, replacePattern, replaceTo); }
                    catch { Debug.LogWarning("正则不合法: " + replacePattern); }
                }
                newName = prefix + newName + suffix;

                if (newName != name)
                {
                    if (dryRun)
                        Debug.Log($"[DryRun] {name} -> {newName}");
                    else
                        AssetDatabase.RenameAsset(path, newName);
                }
            }
            if (!dryRun) AssetDatabase.SaveAssets();
        }
    }
}
```

### 11.2 Runtime Overlay (Tool #3)

```csharp
using UnityEngine;
using System.Text;

public class RuntimeOverlay : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F1;
    public bool show = true;
    public int fontSize = 14;
    private float _lastUpdate;
    private int _frameCount;
    private float _fps;
    private StringBuilder _sb = new StringBuilder(256);

#if !ENABLE_RUNTIME_OVERLAY
    void Awake() { Destroy(this); }
#endif

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) show = !show;
        _frameCount++;
        if (Time.unscaledTime - _lastUpdate >= 0.5f)
        {
            _fps = _frameCount / (Time.unscaledTime - _lastUpdate);
            _frameCount = 0;
            _lastUpdate = Time.unscaledTime;
        }
    }

    public static void AddCustom(string key, string value)
    {
        // 可扩展为存储字典
    }

    void OnGUI()
    {
        if (!show) return;
        GUI.skin.label.fontSize = fontSize;
        Rect r = new Rect(10, 10, 420, Screen.height - 20);
        _sb.Length = 0;
        _sb.AppendLine($"FPS: {_fps:0.0}");
        _sb.AppendLine($"Memory(MB): {System.GC.GetTotalMemory(false)/1024f/1024f:0.0}");
        _sb.AppendLine($"Active GOs: {FindObjectsOfType<GameObject>().Length}");
        GUI.Box(r, GUIContent.none);
        GUI.Label(r, _sb.ToString());
    }
}
```

### 11.3 Collider2D Gizmos (Tool #4)

```csharp
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class Collider2DDebugDrawer
{
    private static bool enabled = true;
    static Collider2DDebugDrawer()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    [MenuItem("AsakiTools/Debug/Toggle Collider2D Gizmos %&c")]
    private static void Toggle()
    {
        enabled = !enabled;
        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView sv)
    {
        if (!enabled) return;
        Handles.color = Color.green;
        foreach (var col in Object.FindObjectsOfType<Collider2D>())
        {
            if (!col.enabled) continue;
            switch (col)
            {
                case BoxCollider2D box:
                    DrawBox(box);
                    break;
                case CircleCollider2D circle:
                    DrawCircle(circle);
                    break;
                case PolygonCollider2D poly:
                    DrawPoly(poly);
                    break;
            }
        }
    }

    private static void DrawBox(BoxCollider2D box)
    {
        var t = box.transform;
        var c = (Vector2)t.TransformPoint(box.offset);
        var size = box.size;
        Vector3 right = t.right * size.x * 0.5f;
        Vector3 up = t.up * size.y * 0.5f;
        Vector3 a = c - (Vector2)right - (Vector2)up;
        Vector3 b = c + (Vector2)right - (Vector2)up;
        Vector3 d = c + (Vector2)right + (Vector2)up;
        Vector3 e = c - (Vector2)right + (Vector2)up;
        Handles.DrawLine(a, b);
        Handles.DrawLine(b, d);
        Handles.DrawLine(d, e);
        Handles.DrawLine(e, a);
    }

    private static void DrawCircle(CircleCollider2D circle)
    {
        var t = circle.transform;
        var center = (Vector2)t.TransformPoint(circle.offset);
        Handles.DrawWireDisc(center, Vector3.forward, circle.radius * t.lossyScale.x);
    }

    private static void DrawPoly(PolygonCollider2D poly)
    {
        var t = poly.transform;
        for (int p = 0; p < poly.pathCount; p++)
        {
            var path = poly.GetPath(p);
            for (int i = 0; i < path.Length; i++)
            {
                var a = t.TransformPoint(path[i] + poly.offset);
                var b = t.TransformPoint(path[(i + 1) % path.Length] + poly.offset);
                Handles.DrawLine(a, b);
            }
        }
    }
}
```

### 11.4 CSV→ScriptableObject (Tool #5)

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class CSVToScriptableObjects : EditorWindow
{
    private TextAsset csv;
    private string outputFolder = "Assets/GameData/Generated";
    private string assetTypeName = "MyDataRow";
    private bool generateIdConst = true;
    private string idField = "Id";

    [MenuItem("AsakiTools/Data/CSV -> ScriptableObjects")]
    public static void Open()
    {
        GetWindow<CSVToScriptableObjects>("CSV -> SO");
    }

    void OnGUI()
    {
        csv = (TextAsset)EditorGUILayout.ObjectField("CSV 文件", csv, typeof(TextAsset), false);
        outputFolder = EditorGUILayout.TextField("输出目录", outputFolder);
        assetTypeName = EditorGUILayout.TextField("行类型名", assetTypeName);
        idField = EditorGUILayout.TextField("ID 字段名", idField);
        generateIdConst = EditorGUILayout.Toggle("生成 ID 常量类", generateIdConst);

        if (GUILayout.Button("生成"))
        {
            Generate();
        }
    }

    void Generate()
    {
        if (csv == null)
        {
            Debug.LogError("CSV 未选择");
            return;
        }
        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        var lines = csv.text.Replace("\r", "").Split('\n');
        if (lines.Length < 2)
        {
            Debug.LogError("CSV 行数不足");
            return;
        }
        var headers = lines[0].Split(',');
        int idIndex = System.Array.IndexOf(headers, idField);
        if (idIndex < 0)
        {
            Debug.LogWarning("未找到 ID 字段，不生成常量。");
            generateIdConst = false;
        }

        List<string> ids = new List<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length != headers.Length) continue;

            string idVal = idIndex >= 0 ? cols[idIndex] : $"Row{i}";
            string assetPath = $"{outputFolder}/{assetTypeName}_{idVal}.asset";
            var so = ScriptableObject.CreateInstance(assetTypeName);
            var type = so.GetType();
            for (int c = 0; c < headers.Length; c++)
            {
                var field = type.GetField(headers[c]);
                if (field == null) continue;
                object v = ParseValue(field.FieldType, cols[c]);
                field.SetValue(so, v);
            }
            AssetDatabase.CreateAsset(so, assetPath);
            ids.Add(idVal);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"生成完成: {ids.Count} 条");

        if (generateIdConst)
        {
            string constCodePath = $"{outputFolder}/{assetTypeName}Ids.cs";
            using (var sw = new StreamWriter(constCodePath, false))
            {
                sw.WriteLine("public static class " + assetTypeName + "Ids {");
                foreach (var id in ids)
                {
                    string safeId = MakeConstName(id);
                    sw.WriteLine($"    public const string {safeId} = \"{id}\";");
                }
                sw.WriteLine("}");
            }
            AssetDatabase.ImportAsset(constCodePath);
        }
    }

    object ParseValue(System.Type t, string raw)
    {
        if (t == typeof(int)) return int.TryParse(raw, out var v) ? v : 0;
        if (t == typeof(float)) return float.TryParse(raw, out var f) ? f : 0f;
        if (t == typeof(bool)) return raw == "1" || raw.ToLower() == "true";
        if (t == typeof(string)) return raw;
        return null;
    }

    string MakeConstName(string id)
    {
        var s = id.Replace("-", "_").Replace(" ", "_");
        if (!string.IsNullOrEmpty(s) && char.IsDigit(s[0])) s = "_" + s;
        return s;
    }
}
```

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Data/MyDataRow")]
public class MyDataRow : ScriptableObject
{
    public string Id;
    public string DisplayName;
    public int Power;
    public float Speed;
    public bool Unlock;
}
```

---

## 12. 未来可加的派生任务（空白模板）

```
- [ ] <编号或新> <任务名称>
    描述：
    触发条件：
    输入输出：
    风险：
```

---

## 13. 价值回顾（留作后期整理）

| 工具  | 已节省时间估计 | 问题减少情况      | 是否继续维护 | 备注  |
| --- | ------- | ----------- | ------ | --- |
| 示例  | +2h/周   | 动画事件错误 -80% | 是      | -   |

---

## 14. 变更日志 (手动更新)

| 日期         | 变更  | 说明           |
| ---------- | --- | ------------ |
| 2025-09-03 | 初始化 | 基于 Chat 规划整理 |
| 2025-__-__ |     |              |

---

如需新增更贴合“玩法/敌人/数值”类专用调试工具，请在 Issue 中引用本文件并补充使用场景。  
愉快开发！🚀
