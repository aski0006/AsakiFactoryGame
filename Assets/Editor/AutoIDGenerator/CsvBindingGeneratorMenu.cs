#if UNITY_EDITOR
using Game.CSV;
using Game.Data.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.AutoIDGenerator
{
    public static class CsvBindingGeneratorMenu
    {
        private const string DefaultOutputDir = "Assets/Scripts/Generated/CsvBindings";
        private const string ManifestFileName = "CsvGenManifest.xml";

        [MenuItem("Tools/Core Generator/CSV Generate Bindings")]
        public static void Generate()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settings = LoadSettings();
            var outputDir = string.IsNullOrEmpty(settings?.outputFolder) ? DefaultOutputDir : settings.outputFolder;
            Directory.CreateDirectory(outputDir);

            // Manifest 读入
            var manifestPath = Path.Combine(outputDir, ManifestFileName);
            var manifest = CsvGenManifest.Load(manifestPath);
            var newManifest = new CsvGenManifest();

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

            int generatedOrUpdated = 0;
            int skipped = 0;
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

                    // Binding
                    var bindingCode = GenerateDefinitionBinding(defType, fieldInfos, defAttr, settings);
                    var bindingPath = Path.Combine(outputDir, $"{defType.Name}_CsvBinding.g.cs");
                    if (WriteFileIfChangedIncremental(bindingPath, bindingCode, manifest, newManifest, settings))
                        generatedOrUpdated++;
                    else
                        skipped++;

                    // Editor Setter partial
                    if (settings == null || settings.generateEditorSetters)
                    {
                        var setterCode = GenerateEditorSetterPartial(defType, fieldInfos);
                        var setterPath = Path.Combine(outputDir, $"{defType.Name}_CsvSetters.g.cs");
                        if (WriteFileIfChangedIncremental(setterPath, setterCode, manifest, newManifest, settings))
                            generatedOrUpdated++;
                        else
                            skipped++;
                    }

                    // Section Adapter
                    if (defAttr.GenerateSectionAdapters)
                    {
                        var sectionTypes = FindSectionTypesForDefinition(allTypes, defType);
                        foreach (var sectionType in sectionTypes)
                        {
                            var adapterCode = GenerateSectionAdapter(defType, sectionType);
                            var adapterPath = Path.Combine(outputDir, $"{sectionType.Name}_CsvAdapter.g.cs");
                            if (WriteFileIfChangedIncremental(adapterPath, adapterCode, manifest, newManifest, settings))
                                generatedOrUpdated++;
                            else
                                skipped++;
                        }
                    }

                    logBuilder.AppendLine($"[CSVGen] OK {defType.Name}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CSVGen] 处理 {defType.FullName} 失败: {e}");
                }
            }

            // 删除陈旧文件
            if (settings == null || settings.deleteStaleFiles)
            {
                var newSet = new HashSet<string>(newManifest.Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
                foreach (var old in manifest.Files)
                {
                    if (!newSet.Contains(old.FilePath))
                    {
                        try
                        {
                            if (File.Exists(old.FilePath))
                            {
                                File.Delete(old.FilePath);
                                logBuilder.AppendLine($"[CSVGen] 删除陈旧文件 {old.FilePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CSVGen] 删除文件失败 {old.FilePath}: {ex.Message}");
                        }
                    }
                }
            }

            // 保存新 manifest
            newManifest.Save(manifestPath);

            AssetDatabase.Refresh();
            sw.Stop();
            Debug.Log($"[CSVGen] 完成。生成/更新={generatedOrUpdated} 跳过(未变化)={skipped} 耗时={sw.ElapsedMilliseconds}ms\n{logBuilder}");
        }

        private static CsvCodeGenSettings LoadSettings()
        {
            var guids = AssetDatabase.FindAssets("t:CsvCodeGenSettings");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<CsvCodeGenSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        #region Incremental Helpers

        private static bool WriteFileIfChangedIncremental(string path, string content, CsvGenManifest oldManifest, CsvGenManifest newManifest, CsvCodeGenSettings settings)
        {
            var hash = CsvGenManifest.ComputeHash(content);
            var oldEntry = oldManifest.Find(path);
            if (oldEntry != null && oldEntry.Hash == hash && (settings == null || settings.incremental))
            {
                // 保留旧 entry
                newManifest.Files.Add(new CsvGenManifestEntry { FilePath = path, Hash = hash, TypeFullName = oldEntry.TypeFullName });
                return false; // 未写入
            }

            var oldExists = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            if (oldExists != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }

            newManifest.Files.Add(new CsvGenManifestEntry
            {
                FilePath = path,
                Hash = hash,
                TypeFullName = "" // 可选：记录 defType.FullName
            });
            return true;
        }

        #endregion

        #region Reflection Helpers

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

        #endregion

        #region Binding Generation

        private static string GenerateDefinitionBinding(
            Type defType,
            List<FieldInfo> fields,
            CsvDefinitionAttribute defAttr,
            CsvCodeGenSettings settings)
        {
            var ns = string.IsNullOrEmpty(defType.Namespace) ? "GlobalNamespace" : defType.Namespace;
            var headerCols = new List<string>();
            var remarkCols = new List<string>();

            foreach (var f in fields)
            {
                var meta = f.GetCustomAttribute<CsvFieldAttribute>();
                var col = GetColumnName(f, meta);
                if (headerCols.Contains(col))
                    Debug.LogWarning($"[CSVGen] 重复列名 {col} in {defType.Name}");
                headerCols.Add(col);
                remarkCols.Add(meta.Remark ?? "");
            }

            char kvSep = settings != null ? settings.defaultKeyValueSeparator : ':'; // 字典 key-value 分隔符

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using Game.CSV;");
            sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"internal static class CsvBinding_{defType.Name}");
            sb.AppendLine("{");

            // === Primitive Parse Helper (added) ===
            sb.AppendLine("    // 基础类型/枚举解析辅助（由导入代码调用）");
            sb.AppendLine("    private static bool TryParsePrimitive<T>(string s, out T v)");
            sb.AppendLine("    {");
            sb.AppendLine("        v = default!;"); // C# 8 nullable 安全
            sb.AppendLine("        if (string.IsNullOrWhiteSpace(s)) return false;");
            sb.AppendLine("        var t = typeof(T);");
            sb.AppendLine("        try {");
            sb.AppendLine("            if (t == typeof(string)) { v = (T)(object)s; return true; }");
            sb.AppendLine("            if (t == typeof(int)) { if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iv)) { v = (T)(object)iv; return true; } return false; }");
            sb.AppendLine("            if (t == typeof(float)) { if (float.TryParse(s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var fv)) { v = (T)(object)fv; return true; } return false; }");
            sb.AppendLine("            if (t == typeof(double)) { if (double.TryParse(s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var dv)) { v = (T)(object)dv; return true; } return false; }");
            sb.AppendLine("            if (t == typeof(long)) { if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lv)) { v = (T)(object)lv; return true; } return false; }");
            sb.AppendLine("            if (t == typeof(bool)) {");
            sb.AppendLine("                if (bool.TryParse(s, out var bv)) { v = (T)(object)bv; return true; }");
            sb.AppendLine("                if (s == \"1\") { v = (T)(object)true; return true; }");
            sb.AppendLine("                if (s == \"0\") { v = (T)(object)false; return true; }");
            sb.AppendLine("                if (string.Equals(s, \"yes\", System.StringComparison.OrdinalIgnoreCase)) { v = (T)(object)true; return true; }");
            sb.AppendLine("                if (string.Equals(s, \"no\", System.StringComparison.OrdinalIgnoreCase)) { v = (T)(object)false; return true; }");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (t.IsEnum) { if (System.Enum.TryParse(t, s, true, out var ev)) { v = (T)ev; return true; } return false; }");
            sb.AppendLine("        } catch { return false; }");
            sb.AppendLine("        return false;");
            sb.AppendLine("    }");

            // Header
            sb.AppendLine("    internal static readonly string[] Header = new[]{");
            for (int i = 0; i < headerCols.Count; i++)
            {
                sb.Append("        \"").Append(EscapeString(headerCols[i])).Append("\"");
                if (i < headerCols.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("    };");

            // Remarks
            sb.AppendLine("    internal static readonly string[] Remarks = new[]{");
            for (int i = 0; i < remarkCols.Count; i++)
            {
                sb.Append("        \"").Append(EscapeString(remarkCols[i])).Append("\"");
                if (i < remarkCols.Count - 1) sb.Append(",");
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
                sb.AppendLine(GenerateExportLine(defType, f, meta, defAttr, kvSep));
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
                sb.AppendLine(GenerateImportLines(defType, f, meta, defAttr, kvSep));
            }
            sb.AppendLine("    }");
            sb.AppendLine("#endif");

            sb.AppendLine("}"); // class
            sb.AppendLine("}"); // ns
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string GetColumnName(FieldInfo fi, CsvFieldAttribute meta)
        {
            if (!string.IsNullOrEmpty(meta.Column))
                return meta.Column;
            if (fi.Name.Length > 0 && char.IsUpper(fi.Name[0]))
                return char.ToLower(fi.Name[0]) + fi.Name.Substring(1);
            return fi.Name;
        }

        private static bool IsListLike(Type t, out Type elemType)
        {
            elemType = null;
            if (t.IsArray)
            {
                elemType = t.GetElementType();
                return true;
            }
            if (t.IsGenericType)
            {
                var td = t.GetGenericTypeDefinition();
                if (td == typeof(List<>) || td == typeof(IReadOnlyList<>))
                {
                    elemType = t.GetGenericArguments()[0];
                    return true;
                }
            }
            // 接口实现 IReadOnlyList<>
            var iface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
            if (iface != null)
            {
                elemType = iface.GetGenericArguments()[0];
                return true;
            }
            return false;
        }

        private static bool IsDictionaryLike(Type t, out Type keyType, out Type valueType)
        {
            keyType = null;
            valueType = null;
            if (t.IsGenericType)
            {
                var td = t.GetGenericTypeDefinition();
                if (td == typeof(Dictionary<,>) || td == typeof(IReadOnlyDictionary<,>))
                {
                    var args = t.GetGenericArguments();
                    keyType = args[0];
                    valueType = args[1];
                    return true;
                }
            }
            // 接口实现
            var iface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
            if (iface != null)
            {
                var args = iface.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }
            return false;
        }

        private static string GenerateExportLine(Type defType, FieldInfo fi, CsvFieldAttribute meta, CsvDefinitionAttribute defAttr, char kvSep)
        {
            string col = GetColumnName(fi, meta);
            string propGuess = GuessPublicPropertyName(fi);
            var t = fi.FieldType;

            if (t == typeof(string))
                return $"        d[\"{col}\"] = {InstanceExpr(propGuess, fi)} ?? \"\";";
            if (t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t.IsEnum)
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
                return @"#if UNITY_EDITOR
        if (" + InstanceExpr(propGuess, fi) + @" != null) {
            var p = AssetDatabase.GetAssetPath(" + InstanceExpr(propGuess, fi) + @");
            d[""" + col + @"""] = string.IsNullOrEmpty(p) ? """" : AssetDatabase.AssetPathToGUID(p);
        } else d[""" + col + @"""] = """";
#else
        d[""" + col + @"""] = """";
#endif";
            }

            if (IsDictionaryLike(t, out var kType, out var vType))
            {
                var entrySep = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                // 排序（如键可排序）保证稳定输出
                string orderLine = kType.GetInterface(nameof(IComparable)) != null || kType.IsPrimitive || kType.IsEnum
                    ? "var __ordered = dict.Keys.OrderBy(_k => _k).ToList();"
                    : "var __ordered = dict.Keys.ToList();";

                return
                    $@"        {{
            var dict = {InstanceExpr(propGuess, fi)} as System.Collections.Generic.IReadOnlyDictionary<{kType.FullName},{vType.FullName}>;
            if (dict == null || dict.Count == 0) d[""{col}""] = """";
            else {{
                {orderLine}
                System.Text.StringBuilder __sb = new System.Text.StringBuilder();
                for (int __i=0; __i<__ordered.Count; __i++)
                {{
                    var __k = __ordered[__i];
                    var __v = dict[__k];
                    string __ks, __vs;
                    if (!CsvTypeConverterRegistry.TrySerialize(typeof({kType.FullName}), __k, out __ks)) __ks = __k != null ? __k.ToString() : """";
                    if (!CsvTypeConverterRegistry.TrySerialize(typeof({vType.FullName}), __v, out __vs)) __vs = __v != null ? __v.ToString() : """";
                    if (__i>0) __sb.Append('{entrySep}');
                    __sb.Append(__ks).Append('{kvSep}').Append(__vs);
                }}
                d[""{col}""] = __sb.ToString();
            }}
        }}";
            }

            if (IsListLike(t, out var elemType))
            {
                var sepChar = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                return
                    $@"        {{
            var __list = {InstanceExpr(propGuess, fi)} as System.Collections.Generic.IReadOnlyList<{elemType.FullName}>;
            if (__list == null || __list.Count == 0) d[""{col}""] = """";
            else {{
                System.Text.StringBuilder __sb = new System.Text.StringBuilder();
                for (int __i = 0; __i < __list.Count; __i++)
                {{
                    var __e = __list[__i];
                    string __s;
                    if (CsvTypeConverterRegistry.TrySerialize(typeof({elemType.FullName}), __e, out __s))
                    {{
                        if (__i > 0) __sb.Append('{sepChar}');
                        __sb.Append(__s);
                    }}
                    else
                    {{
                        if (__i > 0) __sb.Append('{sepChar}');
                        __sb.Append(__e.ToString());
                    }}
                }}
                d[""{col}""] = __sb.ToString();
            }}
        }}";
            }

            // 单值自定义类型
            return
                $@"        {{
            string __custom;
            if (CsvTypeConverterRegistry.TrySerialize(typeof({t.FullName}), {InstanceExpr(propGuess, fi)}, out __custom))
                d[""{col}""] = __custom ?? """";
            else
                d[""{col}""] = {InstanceExpr(propGuess, fi)} != null ? {InstanceExpr(propGuess, fi)}.ToString() : """";
        }}";
        }

        private static string InstanceExpr(string propGuess, FieldInfo fi) => $"def.{propGuess}";

        private static string GuessPublicPropertyName(FieldInfo fi)
        {
            var n = fi.Name;
            if (n.Length == 0) return n;
            if (char.IsLower(n[0]))
                return char.ToUpper(n[0]) + n.Substring(1);
            return n;
        }

        private static string GenerateImportLines(Type defType, FieldInfo fi, CsvFieldAttribute meta, CsvDefinitionAttribute defAttr, char kvSep)
        {
            var col = GetColumnName(fi, meta);
            var setterName = $"__CsvSet_{fi.Name}";
            var fieldType = fi.FieldType;
            var sb = new StringBuilder();
            string localVar = $"v_{fi.Name}";

            sb.AppendLine($"        if (row.TryGetValue(\"{col}\", out var {localVar}))");
            sb.AppendLine("        {");
            sb.AppendLine($"            if ({localVar} == null) {localVar} = string.Empty;");

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
            else if (IsDictionaryLike(fieldType, out var kType, out var vType))
            {
                var sep = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                sb.AppendLine(
                    $@"            if (string.IsNullOrWhiteSpace({localVar})) {{
                target.{setterName}(new System.Collections.Generic.Dictionary<{kType.FullName},{vType.FullName}>());
            }} else {{
                var _parts = {localVar}.Split(new[]{{'{sep}'}}, StringSplitOptions.RemoveEmptyEntries);
                var _dict = new System.Collections.Generic.Dictionary<{kType.FullName},{vType.FullName}>(_parts.Length);
                for (int __i=0; __i<_parts.Length; __i++)
                {{
                    var __token = _parts[__i].Trim();
                    int __sepIdx = __token.IndexOf('{kvSep}');
                    if (__sepIdx <= 0 || __sepIdx >= __token.Length-1) continue;
                    var __ks = __token.Substring(0, __sepIdx).Trim();
                    var __vs = __token.Substring(__sepIdx+1).Trim();
                    object __kObj, __vObj;
                    // 解析键
                    if (!CsvTypeConverterRegistry.TryDeserialize(typeof({kType.FullName}), __ks, out __kObj))
                    {{
                        __kObj = TryParsePrimitive<{kType.FullName}>(__ks, out var __kPrim) ? (object)__kPrim :
                                 (typeof({kType.FullName}) == typeof(string) ? (object)__ks : null);
                    }}
                    // 解析值
                    if (!CsvTypeConverterRegistry.TryDeserialize(typeof({vType.FullName}), __vs, out __vObj))
                    {{
                        __vObj = TryParsePrimitive<{vType.FullName}>(__vs, out var __vPrim) ? (object)__vPrim :
                                 (typeof({vType.FullName}) == typeof(string) ? (object)__vs : null);
                    }}
                    if (__kObj is {kType.FullName} __kC && __vObj is {vType.FullName} __vC)
                    {{
                        if(!_dict.ContainsKey(__kC))
                            _dict.Add(__kC, __vC);
                    }}
                }}
                target.{setterName}(_dict);
            }}");
            }
            else if (IsListLike(fieldType, out var elemType))
            {
                var sep = string.IsNullOrEmpty(meta.CustomArraySeparator) ? defAttr.ArraySeparator : meta.CustomArraySeparator[0];
                sb.AppendLine(
                    $@"            if (string.IsNullOrWhiteSpace({localVar})) {{
                target.{setterName}(System.Array.Empty<{elemType.FullName}>());
            }} else {{
                var _parts = {localVar}.Split(new[]{{'{sep}'}}, StringSplitOptions.RemoveEmptyEntries);
                var _list = new System.Collections.Generic.List<{elemType.FullName}>(_parts.Length);
                for (int __i = 0; __i < _parts.Length; __i++)
                {{
                    var __token = _parts[__i].Trim();
                    if (CsvTypeConverterRegistry.TryDeserialize(typeof({elemType.FullName}), __token, out var __obj))
                    {{
                        _list.Add( ({elemType.FullName})__obj );
                    }}
                    else
                    {{
                        if (TryParsePrimitive<{elemType.FullName}>(__token, out var __prim))
                            _list.Add(__prim);
                        else if (typeof({elemType.FullName}) == typeof(string))
                            _list.Add( ( {elemType.FullName} )(object)__token );
                        // 否则忽略
                    }}
                }}
                target.{setterName}(_list.ToArray());
            }}");
            }
            else
            {
                sb.AppendLine(
                    $@"            if (CsvTypeConverterRegistry.TryDeserialize(typeof({fieldType.FullName}), {localVar}, out var __boxed))
            {{
                target.{setterName}( ({fieldType.FullName})__boxed );
            }}
            else if (TryParsePrimitive<{fieldType.FullName}>({localVar}, out var __prim))
            {{
                target.{setterName}(__prim);
            }}
            else if (typeof({fieldType.FullName}) == typeof(string) && !string.IsNullOrEmpty({localVar}))
            {{
                target.{setterName}(({fieldType.FullName})(object){localVar});
            }}");
            }

            sb.AppendLine("        }");
            // 附加 TryParsePrimitive<T> 静态泛型方法只生成一次可放最下面，这里简化放 Section Adapter 内或 Binding 末尾
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
            sb.AppendLine("using Game.CSV;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("#endif");
            sb.AppendLine($"namespace {ns} {{");
            sb.AppendLine($"public partial class {sectionType.Name} : ICsvImportableConfig, ICsvExportableConfig, ICsvRemarkProvider");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<string> GetExpectedColumns() => CsvBinding_" + defType.Name + ".Header;");
            sb.AppendLine("    public IReadOnlyList<string> GetCsvHeader() => CsvBinding_" + defType.Name + ".Header;");
            sb.AppendLine("    public IReadOnlyList<string> GetCsvRemarks() => CsvBinding_" + defType.Name + ".Remarks;");
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

            sb.AppendLine("    public void ExportCsv(string path, bool utf8Bom = true)");
            sb.AppendLine("    {");
            sb.AppendLine("        var header = CsvBinding_" + defType.Name + ".Header;");
            sb.AppendLine("        var remarks = CsvBinding_" + defType.Name + ".Remarks;");
            sb.AppendLine("        var rows = ExportCsvRows();");
            sb.AppendLine("        CsvExportUtility.Write(path, header, rows, remarks, utf8Bom);");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("        Debug.Log($\"[CSVGen] 导出完成: {path}\");");
            sb.AppendLine("#endif");
            sb.AppendLine("    }");

            // 工具：TryParsePrimitive<T>
            sb.AppendLine(@"    private static bool TryParsePrimitive<T>(string s, out T v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var target = typeof(T);
        try
        {
            if (target == typeof(int))
            {
                if (int.TryParse(s, out var iv)) { v = (T)(object)iv; return true; }
                return false;
            }
            if (target == typeof(float))
            {
                if (float.TryParse(s, out var fv)) { v = (T)(object)fv; return true; }
                return false;
            }
            if (target == typeof(double))
            {
                if (double.TryParse(s, out var dv)) { v = (T)(object)dv; return true; }
                return false;
            }
            if (target == typeof(long))
            {
                if (long.TryParse(s, out var lv)) { v = (T)(object)lv; return true; }
                return false;
            }
            if (target == typeof(bool))
            {
                if (bool.TryParse(s, out var bv)) { v = (T)(object)bv; return true; }
                if (s == ""1"") { v = (T)(object)true; return true; }
                if (s == ""0"") { v = (T)(object)false; return true; }
                return false;
            }
            if (target.IsEnum)
            {
                if (Enum.TryParse(target, s, true, out var ev)) { v = (T)ev; return true; }
                return false;
            }
        }
        catch { return false; }
        return false;
    }");

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
            return t.FullName?.Replace("+", ".") ?? t.Name;
        }

        #endregion
    }
}
#endif
