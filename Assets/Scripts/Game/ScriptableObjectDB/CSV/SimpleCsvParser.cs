using System.Collections.Generic;
using System.Text;

namespace Game.ScriptableObjectDB.CSV
{
    /// <summary>
    /// 简单 CSV 解析（支持双引号包裹、内部逗号、双引号转义 "" -> "）。
    /// 不支持多行字段（如需要可扩展）。
    /// </summary>
    public static class SimpleCsvParser
    {
        public static List<string> ParseLine(string line, char delimiter = ',')
        {
            var result = new List<string>();
            if (line == null)
            {
                result.Add("");
                return result;
            }

            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // 看下一个是不是双引号（转义）
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == delimiter)
                    {
                        result.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            result.Add(sb.ToString());
            return result;
        }
    }
}
