/*
 * ConfigIdCodeGenerator
 * ---------------------------------------------------------
 * 作用:
 *   扫描所有实现 IContextSection 的 Section 资产，提取其中的 IDefinition（id 与可用名称字段），
 *   生成一个集中常量文件 ConfigIds.g.cs，便于在代码中通过常量访问定义 Id，降低硬编码 & 魔法数字。
 *
 * 菜单:
 *   Tools/Config DB/Generate Config Id Constants
 *
 * 默认输出:
 *   Assets/Scripts/Generated/ConfigIds.g.cs  (若不存在会自动创建目录)
 *
 * 命名生成规则:
 *   1. 按字段优先级尝试获取“原始名称”：
 *      codeName > internalName > name > resourceName > displayName > editorName > EditorName
 *   2. 若都不存在或为空：使用 <TypeName>_<Id>
 *   3. 规范化：
 *      - 去除前后空白
 *      - 非字母数字字符替换为下划线
 *      - 去掉连续下划线
 *      - 首字符若为数字前缀 "_"
 *      - 转 PascalCase（下划线、空格、短横等作为分词）
 *   4. 若同一类型内出现重复规范化结果，后者追加 "_<Id>"
 *
 * 生成结构 (示例):
 *
 *   namespace Game.Data.Generated {
 *     public static partial class ConfigIds {
 *        public static class ItemDefinition {
 *           public const int Wood = 1;
 *           public const int Stone = 2;
 *        }
 *        public static class GatherableNodeDefinition {
 *           public const int Tree_Oak = 10;
 *        }
 *     }
 *   }
 *
 * 额外：
 *   - 顶层还生成 TotalCount / PerTypeCount / IdToName 反查字典（仅在编辑器下保留或 #if 需要时可裁剪）
 *
 * 可选拓展（留接口）:
 *   - 输出枚举
 *   - 输出 JSON / CSV
 *   - 为重复 Id 抛出异常而不是日志
 *
 * 注意:
 *   - 仅 Editor 下执行
 *   - 生成文件带有“自动生成”注释头，不要手动修改；如需扩展在同命名空间写 partial class。
 */

#if UNITY_EDITOR

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.Data.Core;

namespace Game.Data.EditorTools
{
    public static class ConfigIdCodeGenerator
    {
        private const string OutputDirectory = "Assets/Scripts/Generated";
        private const string OutputFileName = "ConfigIds.g.cs";
        private static readonly string[] NameFieldPriority =
        {
            "codeName","internalName","name","resourceName","displayName","editorName","EditorName"
        };

        [MenuItem("Tools/Core Generator/Generate Config Id Constants")]
        public static void Generate()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var logBuilder = new StringBuilder();

                // 1. 获取所有 Section 类型
                var sectionTypes = RuntimeConfigLoader.GetSectionTypes(forceRescan: true);

                // 2. 查找所有资产
                var allSectionAssets = new List<ScriptableObject>();
                foreach (var t in sectionTypes)
                {
                    var guids = AssetDatabase.FindAssets("t:" + t.Name);
                    foreach (var g in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        var asset = AssetDatabase.LoadAssetAtPath(path, t) as ScriptableObject;
                        if (asset != null)
                            allSectionAssets.Add(asset);
                    }
                }

                // 3. 解析每个 Section 里的 definitions 列表中的 IDefinition
                // 数据结构: Type -> List<(id, rawName)>
                var typeToEntries = new Dictionary<Type, List<DefinitionEntry>>();

                foreach (var section in allSectionAssets)
                {
                    try
                    {
                        ExtractDefinitionsFromSection(section, typeToEntries, logBuilder);
                    }
                    catch (Exception ex)
                    {
                        logBuilder.AppendLine($"[Generator] Section {section.name} 提取异常: {ex.Message}");
                    }
                }

                // 4. 归档并生成代码
                int totalIds = 0;
                foreach (var kv in typeToEntries)
                    totalIds += kv.Value.Count;

                var code = BuildCode(typeToEntries, totalIds, logBuilder);

                // 5. 写入文件
                if (!Directory.Exists(OutputDirectory))
                    Directory.CreateDirectory(OutputDirectory);

                var fullPath = Path.Combine(OutputDirectory, OutputFileName);
                File.WriteAllText(fullPath, code, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(fullPath);

                sw.Stop();
                Debug.Log($"[ConfigIdCodeGenerator] 生成完成: {fullPath}\n定义类型数={typeToEntries.Count} 总常量数={totalIds} 耗时={sw.ElapsedMilliseconds}ms\n日志:\n{logBuilder}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ConfigIdCodeGenerator] 生成失败: " + ex);
            }
        }

        private class DefinitionEntry
        {
            public int Id;
            public string RawName;   // 原始提取名（可能为空）
            public string FinalName; // 规范化后最终常量名
        }

        private static void ExtractDefinitionsFromSection(
            ScriptableObject sectionAsset,
            Dictionary<Type, List<DefinitionEntry>> map,
            StringBuilder log)
        {
            var sectionType = sectionAsset.GetType();

            // 找 protected List<DefinitionItem> definitions 字段
            var defsField = sectionType.GetField("definitions", BindingFlags.NonPublic | BindingFlags.Instance);
            if (defsField == null)
            {
                log.AppendLine($"[Warn] 未找到 definitions 字段: {sectionType.Name}");
                return;
            }

            var listObj = defsField.GetValue(sectionAsset) as IList;
            if (listObj == null) return;

            foreach (var listItem in listObj)
            {
                if (listItem == null) continue;
                // DefinitionItem 内的 definition 字段
                var defField = listItem.GetType().GetField("definition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (defField == null) continue;
                var defObj = defField.GetValue(listItem);
                if (defObj is not IDefinition def) continue;

                var defType = defObj.GetType();
                int id = def.Id;

                if (!map.TryGetValue(defType, out var entries))
                {
                    entries = new List<DefinitionEntry>();
                    map[defType] = entries;
                }

                // 尝试读取名称字段
                string rawName = TryGetNameField(defObj, defType);
                entries.Add(new DefinitionEntry
                {
                    Id = id,
                    RawName = rawName
                });
            }
        }

        private static string TryGetNameField(object defObj, Type defType)
        {
            foreach (var fieldName in NameFieldPriority)
            {
                var f = defType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(string))
                {
                    var val = f.GetValue(defObj) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                var p = defType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(string) && p.CanRead)
                {
                    var val = p.GetValue(defObj) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }
            return null;
        }

        private static string BuildCode(Dictionary<Type, List<DefinitionEntry>> map, int totalIds, StringBuilder log)
        {
            var sb = new StringBuilder(32 * 1024);

            sb.AppendLine("// ------------------------------------------------------------------------------");
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     THIS FILE IS AUTO-GENERATED BY ConfigIdCodeGenerator.");
            sb.AppendLine("//     DO NOT EDIT MANUALLY. Changes will be overwritten.");
            sb.AppendLine("//     生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("// ------------------------------------------------------------------------------");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace Game.Data.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 集中生成的配置 Id 常量访问入口。");
            sb.AppendLine("    /// 按定义类型（类名）分为内部静态类，每个条目为 public const int {Name} = {Id};");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static partial class ConfigIds");
            sb.AppendLine("    {");

            // 为每种类型生成常量
            foreach (var kv in map.OrderBy(k => k.Key.Name))
            {
                var type = kv.Key;
                var entries = kv.Value;

                // 去重 & 生成最终名字
                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in entries)
                {
                    e.FinalName = BuildSafeConstName(e.RawName, type.Name, e.Id);
                    if (!usedNames.Add(e.FinalName))
                    {
                        var old = e.FinalName;
                        e.FinalName = $"{e.FinalName}_{e.Id}";
                        log.AppendLine($"[NameCollision] {type.Name}::{old} 冲突，改为 {e.FinalName}");
                        usedNames.Add(e.FinalName);
                    }
                }

                // 按 Id 排序生成
                var ordered = entries.OrderBy(e => e.Id).ToList();

                sb.AppendLine($"        // === {type.Name} ===");
                sb.AppendLine($"        public static class {type.Name}");
                sb.AppendLine("        {");
                foreach (var e in ordered)
                {
                    sb.AppendLine($"            /// <summary>SrcName: {(string.IsNullOrEmpty(e.RawName) ? "(null)" : EscapeXml(e.RawName))}</summary>");
                    sb.AppendLine($"            public const int {e.FinalName} = {e.Id};");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // 统计信息
            sb.AppendLine("        /// <summary>总定义常量数量（所有类型累计）。</summary>");
            sb.AppendLine($"        public const int TotalCount = {totalIds};");
            sb.AppendLine();

            // 生成一个静态反查字典（仅在 EDITOR 下或需要时使用，可用 #if 包起来）
            sb.AppendLine("#if UNITY_EDITOR || DEVELOPMENT_BUILD");
            sb.AppendLine("        private static Dictionary<Type, Dictionary<int,string>> _idToNameCache;");
            sb.AppendLine("        /// <summary> (Editor/Dev) 反查：DefinitionType + Id -> 常量名 </summary>");
            sb.AppendLine("        public static string? TryGetConstName(Type defType, int id)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (_idToNameCache == null) BuildReverseCache();");
            sb.AppendLine("            if (_idToNameCache.TryGetValue(defType, out var inner) && inner.TryGetValue(id, out var name))");
            sb.AppendLine("                return name;");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static void BuildReverseCache()");
            sb.AppendLine("        {");
            sb.AppendLine("            _idToNameCache = new Dictionary<Type, Dictionary<int,string>>();");
            foreach (var kv in map.OrderBy(k => k.Key.Name))
            {
                var type = kv.Key;
                sb.AppendLine($"            // {type.Name}");
                sb.AppendLine($"            var dict_{type.Name} = new Dictionary<int,string>();");
                foreach (var e in kv.Value)
                {
                    var finalName = BuildSafeConstName(e.RawName, type.Name, e.Id);
                    sb.AppendLine($"            dict_{type.Name}[{e.Id}] = \"{finalName}\";");
                }
                sb.AppendLine($"            _idToNameCache[typeof({type.FullName})] = dict_{type.Name};");
            }
            sb.AppendLine("        }");
            sb.AppendLine("#endif");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string BuildSafeConstName(string rawName, string typeName, int id)
        {
            string baseName = rawName;

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = $"{typeName}_{id}";

            baseName = baseName.Trim();

            // 替换非法字符为分隔符
            var chars = baseName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    chars[i] = '_';
            }
            baseName = new string(chars);

            // 分隔符统一 -> 下划线
            baseName = baseName.Replace('-', '_');

            // 拆分为 tokens (下划线/空格/多下划线)
            var tokens = baseName
                .Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ToPascal)
                .Where(t => t.Length > 0)
                .ToList();

            if (tokens.Count == 0)
                tokens.Add($"{typeName}{id}");

            var merged = string.Join("", tokens);

            // 开头是数字 -> 加前缀
            if (char.IsDigit(merged[0]))
                merged = "_" + merged;

            return merged;
        }

        private static string ToPascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return s.ToUpperInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static string EscapeXml(string s)
        {
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}

#endif