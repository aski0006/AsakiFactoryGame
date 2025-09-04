using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.CSV
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
        /// <summary>
        /// 旧接口：仅写 header（英文列名）+ 数据。
        /// </summary>
        public static void Write(string path, IReadOnlyList<string> header, IEnumerable<Dictionary<string,string>> rows, bool utf8Bom = false)
            => Write(path, header, rows, null, utf8Bom);

        /// <summary>
        /// 新接口：支持 remarks（中文备注）行。
        /// remarks != null && length>0 时：第一行写 remarks，第二行写 header。
        /// 为安全起见若 remarks.Count != header.Count，会进行对齐裁剪/补空。
        /// </summary>
        public static void Write(
            string path,
            IReadOnlyList<string> header,
            IEnumerable<Dictionary<string,string>> rows,
            IReadOnlyList<string> remarks,
            bool utf8Bom = false)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var sb = new StringBuilder(4096);

            // 如果有备注行
            if (remarks != null && remarks.Count > 0)
            {
                int count = header.Count;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(',');
                    string remark = i < remarks.Count ? remarks[i] : string.Empty;
                    // 可选策略：空 remark 自动用 header[i] 填充：
                    if (string.IsNullOrEmpty(remark)) remark = header[i];
                    sb.Append(Escape(remark ?? ""));
                    Debug.Log($"[CSVGen] 备注行 {remark}");
                }
                sb.Append('\n');
            }

            // 英文列名（程序使用）
            for (int i = 0; i < header.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(header[i]));
            }
            sb.Append('\n');

            // 数据
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