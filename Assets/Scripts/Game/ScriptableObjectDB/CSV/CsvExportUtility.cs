using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.ScriptableObjectDB.CSV
{
    /// <summary>
    /// 简易 CSV 写出：支持逗号、引号、换行转义。
    /// 规则：
    /// - 如果字段包含 逗号 / 引号 / 换行，则用双引号包裹；
    /// - 内部的双引号替换为 ""。
    /// - 行尾不追加多余分隔。
    /// </summary>
    public static class CsvExportUtility
    {
        public static void Write(string path, IReadOnlyList<string> header, IEnumerable<Dictionary<string,string>> rows, bool utf8Bom = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var sb = new StringBuilder(4096);

            // 写表头
            for (int i = 0; i < header.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(header[i]));
            }
            sb.Append('\n');

            // 写每一行
            foreach (var row in rows)
            {
                for (int i = 0; i < header.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    string col = "";
                    if (row != null && row.TryGetValue(header[i], out var v) && v != null)
                        col = v;
                    sb.Append(Escape(col));
                }
                sb.Append('\n');
            }

            var encoding = utf8Bom ? new UTF8Encoding(true) : new UTF8Encoding(false);
            File.WriteAllText(path, sb.ToString(), encoding);
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            bool needQuote =
                text.Contains(',') ||
                text.Contains('"') ||
                text.Contains('\n') ||
                text.Contains('\r');

            if (!needQuote)
                return text;

            var t = text.Replace("\"", "\"\"");
            return $"\"{t}\"";
        }
    }
}
