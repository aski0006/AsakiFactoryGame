#if UNITY_EDITOR
using Game.CSV;
using Game.Data.Core;
using Game.ScriptableObjectDB.CSV;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
// 引用 ICsvImportableConfig / ICsvExportableConfig

// DefinitionSectionBase

namespace Editor.AutoIDGenerator
{
    /// <summary>
    /// 菜单式 CSV 代码生成器（Unity 不支持 Roslyn Source Generator 时使用）。
    /// 逻辑：
    /// 1. 查找所有打了 [CsvDefinition] 的类型。
    /// 2. 收集字段上 [CsvField] 元数据。
    /// 3. 生成：Definition Binding + (可选) Section Adapter + (可选) Editor Setter partial。
    /// </summary>
    public static class CsvBindingGeneratorMenu
    {
        private const string DefaultOutputDir = "Assets/Generated/CsvBindings";

        [MenuItem("Tools/Core Generator/CSV Generate Bindings")]
        public static void Generate()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settings = LoadSettings();
            var outputDir = string.IsNullOrEmpty(settings?.outputFolder) ? DefaultOutputDir : settings.outputFolder;
            Directory.CreateDirectory(outputDir);

            var asmList = AppDomain.CurrentDomain.GetAssemblies();
            var allTypes = new List<Type>();
            foreach (var a in asmList)
            {
                Type[] t;
                try { t = a.GetTypes(); }
                catch { continue; }
                allTypes.AddRange(t);
            }

            var csvDefTypes = allTypes
                .Where(t => !t.IsAbstract && t.GetCustomAttribute<CsvDefinitionAttribute>() != null)
                .ToList();

            int generatedFiles = 0;
            var logBuilder = new StringBuilder();

            foreach (var defType in csvDefTypes)
            {
                try
                {
                    var defAttr = defType.GetCustomAttribute<CsvDefinitionAttribute>();
                    var fieldInfos = defType
                        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .Where(f => f.GetCustomAttribute<CsvFieldAttribute>() != null)
                        .ToList();

                    if (fieldInfos.Count == 0)
                    {
                        logBuilder.AppendLine($"[CSVGen] 跳过 {defType.Name} (无 CsvField).");
                        continue;
                    }

                    // 生成 Binding
                    var bindingCode = GenerateDefinitionBinding(defType, fieldInfos, defAttr, settings);
                    var bindingPath = Path.Combine(outputDir, $"{defType.Name}_CsvBinding.g.cs");
                    generatedFiles += WriteFileIfChanged(bindingPath, bindingCode);

                    // 生成 Editor Setter partial（可选）
                    if (settings == null || settings.generateEditorSetters)
                    {
                        var setterCode = GenerateEditorSetterPartial(defType, fieldInfos);
                        var setterPath = Path.Combine(outputDir, $"{defType.Name}_CsvSetters.g.cs");
                        generatedFiles += WriteFileIfChanged(setterPath, setterCode);
                    }

                    // Section Adapter
                    if (defAttr.GenerateSectionAdapters)
                    {
                        var sectionTypes = FindSectionTypesForDefinition(allTypes, defType);
                        foreach (var sectionType in sectionTypes)
                        {
                            var adapterCode = GenerateSectionAdapter(defType, sectionType);
                            var adapterPath = Path.Combine(outputDir, $"{sectionType.Name}_CsvAdapter.g.cs");
                            generatedFiles += WriteFileIfChanged(adapterPath, adapterCode);
                        }
                    }

                    logBuilder.AppendLine($"[CSVGen] OK {defType.Name}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CSVGen] 处理 {defType.FullName} 失败: {e}");
                }
            }

            AssetDatabase.Refresh();
            sw.Stop();
            Debug.Log($"[CSVGen] 完成 生成/更新文件数={generatedFiles} 耗时={sw.ElapsedMilliseconds}ms\n{logBuilder}");
        }

        private static CsvCodeGenSettings LoadSettings()
        {
            var guids = AssetDatabase.FindAssets("t:CsvCodeGenSettings");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<CsvCodeGenSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static int WriteFileIfChanged(string path, string content)
        {
            if (File.Exists(path))
            {
                var old = File.ReadAllText(path, Encoding.UTF8);
                if (old == content)
                    return 0;
            }
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return 1;
        }

        private static IEnumerable<Type> FindSectionTypesForDefinition(List<Type> allTypes, Type defType)
        {
            return allTypes.Where(t =>
                !t.IsAbstract &&
                t.IsSubclassOf(typeof(ScriptableObject)) &&
                InheritsDefinitionSectionBaseOf(t, defType));
        }

        private static bool InheritsDefinitionSectionBaseOf(Type candidate, Type defType)
        {
            var cur = candidate;
            while (cur != null && cur != typeof(object))
            {
                if (cur.IsGenericType &&
                    cur.GetGenericTypeDefinition() == typeof(DefinitionSectionBase<>))
                {
                    var arg = cur.GetGenericArguments()[0];
                    if (arg == defType)
                        return true;
                }
                cur = cur.BaseType;
            }
            return false;
        }

        #region Binding Generation

        private static string GenerateDefinitionBinding(
            Type defType,
            List<FieldInfo> fields,
            CsvDefinitionAttribute defAttr,
            CsvCodeGenSettings settings)
        {
            var ns = string.IsNullOrEmpty(defType.Namespace) ? "GlobalNamespace" : defType.Namespace;
            var headerCols = new List<string>();
            foreach (var f in fields)
            {
                var meta = f.GetCustomAttribute<CsvFieldAttribute>();
                var col = GetColumnName(f, meta);
                if (headerCols.Contains(col))
                    Debug.LogWarning($"[CSVGen] 重复列名 {col} in {defType.Name}");
                headerCols.Add(col);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using System.Linq;");
#if UNITY_EDITOR
#endif
            sb.AppendLine("using Game.CSV;");
            sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"internal static class CsvBinding_{defType.Name}");
            sb.AppendLine("{");

            // Header
            sb.AppendLine("    internal static readonly string[] Header = new[]{");
            for (int i = 0; i < headerCols.Count; i++)
            {
                sb.Append("        \"").Append(headerCols[i]).Append("\"");
                if (i < headerCols.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("    };");

            // Export
            sb.AppendLine($"    internal static Dictionary<string,string> Export({defType.Name} def)");
            sb.AppendLine("    {");
            sb.AppendLine("        var d = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);");
            foreach (var f in fields)
            {
                var meta = f.GetCustomAttribute<CsvFieldAttribute>();
                if (meta.IgnoreExport) continue;
                sb.AppendLine(GenerateExportLine(defType, f, meta, defAttr));
            }
            sb.AppendLine("        return d;");
            sb.AppendLine("    }");

            // Import
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine($"    internal static void ImportInto({defType.Name} target, Dictionary<string,string> row, IAssetCsvResolver resolver, int lineIndex)");
            sb.AppendLine("    {");
            foreach (var f in fields)
            {
                var meta = f.GetCustomAttribute<CsvFieldAttribute>();
                if (meta.IgnoreImport) continue;
                sb.AppendLine(GenerateImportLines(defType, f, meta, defAttr));
            }
            sb.AppendLine("    }");
            sb.AppendLine("#endif");

            sb.AppendLine("}"); // class
            sb.AppendLine("}"); // ns
            return sb.ToString();
        }

        private static string GetColumnName(FieldInfo fi, CsvFieldAttribute meta)
        {
            if (!string.IsNullOrEmpty(meta.Column))
                return meta.Column;
            if (fi.Name.Length > 0 && char.IsUpper(fi.Name[0]))
                return char.ToLower(fi.Name[0]) + fi.Name.Substring(1);
            return fi.Name;
        }

        private static string GenerateExportLine(Type defType, FieldInfo fi, CsvFieldAttribute meta, CsvDefinitionAttribute defAttr)
        {
            string col = GetColumnName(fi, meta);
            string propGuess = GuessPublicPropertyName(fi);
            var t = fi.FieldType;
            if (t == typeof(string))
                return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)} ?? \"\";";
            if (t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) ||
                t.IsEnum)
                return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)}.ToString();";
            if (t == typeof(bool))
                return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)} ? \"true\" : \"false\";";

            if (t == typeof(string[]))
            {
                var sep = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)} != null ? string.Join(\"{sep}\", {InstanceExpr(propGuess, fi)}) : \"\";";
            }

            if (t == typeof(Sprite) && meta.AssetMode == AssetRefMode.Guid)
            {
                return
@"#if UNITY_EDITOR
        if (" + InstanceExpr(propGuess, fi) + @" != null) {
            var p = AssetDatabase.GetAssetPath(" + InstanceExpr(propGuess, fi) + @");
            d[""" + col + @"""] = string.IsNullOrEmpty(p) ? """" : AssetDatabase.AssetPathToGUID(p);
        } else d[""" + col + @"""] = """";
#else
        d[""" + col + @"""] = """";
#endif";
            }

            // Fallback
            return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)} != null ? {InstanceExpr(propGuess, fi)}.ToString() : \"\";";
        }

        private static string InstanceExpr(string propGuess, FieldInfo fi)
        {
            // 优先尝试公开属性(只读) - 若无则反射时也可以内部 setter；导出只读属性足够
            return $"def.{propGuess}";
        }

        private static string GuessPublicPropertyName(FieldInfo fi)
        {
            // 简单假设：private int id; 对应 public int Id => id;
            var n = fi.Name;
            if (n.Length == 0) return n;
            if (char.IsLower(n[0]))
                return char.ToUpper(n[0]) + n.Substring(1);
            return n;
        }

        private static string GenerateImportLines(Type defType, FieldInfo fi, CsvFieldAttribute meta, CsvDefinitionAttribute defAttr)
        {
            var col = GetColumnName(fi, meta);
            var setterName = $"__CsvSet_{fi.Name}";
            var fieldType = fi.FieldType;
            var sb = new StringBuilder();

            string localVar = $"v_{fi.Name}";
            sb.AppendLine($"        if (row.TryGetValue(\"{col}\", out var {localVar}))");
            sb.AppendLine("        {");
            if (fieldType == typeof(string))
            {
                if (meta.AllowEmptyOverwrite)
                    sb.AppendLine($"            target.{setterName}({localVar});");
                else
                    sb.AppendLine($"            if(!string.IsNullOrEmpty({localVar})) target.{setterName}({localVar});");
            }
            else if (fieldType == typeof(int))
            {
                sb.AppendLine($"            if (int.TryParse({localVar}, out var parsed)) target.{setterName}(parsed);");
            }
            else if (fieldType == typeof(float))
            {
                sb.AppendLine($"            if (float.TryParse({localVar}, out var parsed)) target.{setterName}(parsed);");
            }
            else if (fieldType == typeof(bool))
            {
                sb.AppendLine($"            target.{setterName}(({localVar}.Equals(\"1\") || {localVar}.Equals(\"true\", StringComparison.OrdinalIgnoreCase) || {localVar}.Equals(\"yes\", StringComparison.OrdinalIgnoreCase)));");
            }
            else if (fieldType.IsEnum)
            {
                sb.AppendLine($"            if (System.Enum.TryParse(typeof({fieldType.FullName}), {localVar}, true, out var enumObj)) target.{setterName}(({fieldType.FullName})enumObj);");
            }
            else if (fieldType == typeof(string[]))
            {
                var sep = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                sb.AppendLine(
$@"            var arr = string.IsNullOrWhiteSpace({localVar})
                ? System.Array.Empty<string>()
                : {localVar}.Split(new[]{{'{sep}'}}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct().ToArray();
            target.{setterName}(arr);");
            }
            else if (fieldType == typeof(Sprite) && meta.AssetMode == AssetRefMode.Guid)
            {
                sb.AppendLine(
@"            if (!string.IsNullOrWhiteSpace(" + localVar + @"))
            {
                var sp = resolver.LoadSpriteByGuid(" + localVar + @".Trim());
                target." + setterName + @"(sp);
            }
            else target." + setterName + "(null);");
            }
            else
            {
                sb.AppendLine($"            // Unsupported type {fieldType.FullName} (忽略)");
            }

            sb.AppendLine("        }");
            return sb.ToString();
        }

        #endregion

        #region Section Adapter

        private static string GenerateSectionAdapter(Type defType, Type sectionType)
        {
            var ns = string.IsNullOrEmpty(sectionType.Namespace) ? "GlobalNamespace" : sectionType.Namespace;
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Game.ScriptableObjectDB.CSV;");
            sb.AppendLine("using Game.CSV;");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("#endif");
            sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"public partial class {sectionType.Name} : ICsvImportableConfig, ICsvExportableConfig");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<string> GetExpectedColumns() => CsvBinding_" + defType.Name + ".Header;");
            sb.AppendLine("    public IReadOnlyList<string> GetCsvHeader() => CsvBinding_" + defType.Name + ".Header;");
            sb.AppendLine("    public void PrepareForCsvImport(bool clearExisting)");
            sb.AppendLine("    {");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        if (clearExisting) definitions.Clear();");
            sb.AppendLine("#endif");
            sb.AppendLine("    }");

            sb.AppendLine("    public bool ImportCsvRow(Dictionary<string,string> row, ICsvImportHelper helper, int lineIndex)");
            sb.AppendLine("    {");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!row.TryGetValue(\"id\", out var idStr) || string.IsNullOrWhiteSpace(idStr) || !int.TryParse(idStr,out var idVal))");
            sb.AppendLine("                return false;");
            sb.AppendLine("            " + defType.Name + " target = null;");
            sb.AppendLine("            for (int i = 0; i < definitions.Count; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                var d = definitions[i].definition;");
            sb.AppendLine("                if (d != null && d.Id == idVal) { target = d; break; }");
            sb.AppendLine("            }");
            sb.AppendLine("            if (target == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                target = new " + defType.Name + "();");
            sb.AppendLine("                definitions.Add(new DefinitionItem{ definition = target, description = \"\"});");
            sb.AppendLine("            }");
            sb.AppendLine("            CsvBinding_" + defType.Name + ".ImportInto(target, row, AssetCsvResolver.Instance, lineIndex);");
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception e)");
            sb.AppendLine("        {");
            sb.AppendLine("            helper.LogError(lineIndex, e.Message);");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine("#else");
            sb.AppendLine("        return false;");
            sb.AppendLine("#endif");
            sb.AppendLine("    }");

            sb.AppendLine("    public void FinalizeCsvImport(int success, int error)");
            sb.AppendLine("    {");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        definitions.Sort((a,b)=>");
            sb.AppendLine("        {");
            sb.AppendLine("            if (a?.definition == null || b?.definition == null) return 0;");
            sb.AppendLine("            return a.definition.Id.CompareTo(b.definition.Id);");
            sb.AppendLine("        });");
            sb.AppendLine("        EditorUtility.SetDirty(this);");
            sb.AppendLine("#endif");
            sb.AppendLine("    }");

            sb.AppendLine("    public IEnumerable<Dictionary<string,string>> ExportCsvRows()");
            sb.AppendLine("    {");
            sb.AppendLine("        foreach (var di in definitions)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (di?.definition == null) continue;");
            sb.AppendLine("            yield return CsvBinding_" + defType.Name + ".Export(di.definition);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine("}"); // class
            sb.AppendLine("}"); // ns
            return sb.ToString();
        }

        #endregion

        #region Editor Setter Partial

        private static string GenerateEditorSetterPartial(Type defType, List<FieldInfo> fields)
        {
            var ns = string.IsNullOrEmpty(defType.Namespace) ? "GlobalNamespace" : defType.Namespace;
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"public partial class {defType.Name} ");
            sb.AppendLine("{");
            foreach (var f in fields)
            {
                sb.AppendLine($"    internal void __CsvSet_{f.Name}({GetFriendlyTypeName(f.FieldType)} v) => {f.Name} = v;");
            }
            sb.AppendLine("}");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            return sb.ToString();
        }

        private static string GetFriendlyTypeName(Type t)
        {
            if (!t.IsGenericType) return t.FullName?.Replace("+", ".") ?? t.Name;
            // 不做复杂处理（本项目字段类型简单）
            return t.FullName?.Replace("+", ".") ?? t.Name;
        }

        #endregion
    }
}
#endif