using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.CSV
{
    public class CsvImportHelper : ICsvImportHelper
    {
        private readonly string _contextName;

        public CsvImportHelper(string contextName)
        {
            _contextName = contextName;
        }

        public bool TryParseInt(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        public bool TryParseFloat(string s, out float v) => float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);

        public bool TryParseBool(string s, out bool v)
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (bool.TryParse(s, out v)) return true;
            if (s == "0") { v = false; return true; }
            if (s == "1") { v = true; return true; }
            if (string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase)) { v = true; return true; }
            if (string.Equals(s, "no", StringComparison.OrdinalIgnoreCase)) { v = false; return true; }
            return false;
        }

        public bool TryParseEnum<T>(string s, out T v) where T : struct
            => Enum.TryParse(s, true, out v);

        public string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

        #region Unity Type Parsers

        private static readonly char[] _vectorSplit = { ',', ';', '|', ' ', '\t' };

        private static bool ParseFloatList(string s, int minCount, int maxCount, out float[] values)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // 去掉可能的 () [] {}
            s = s.Trim();
            if ((s.StartsWith("(") && s.EndsWith(")")) ||
                (s.StartsWith("[") && s.EndsWith("]")) ||
                (s.StartsWith("{") && s.EndsWith("}")))
            {
                s = s.Substring(1, s.Length - 2);
            }

            var parts = s.Split(_vectorSplit, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < minCount || parts.Length > maxCount) return false;

            float[] nums = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i].TrimEnd('f', 'F');
                if (!float.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out nums[i]))
                    return false;
            }

            values = nums;
            return true;
        }

        public bool TryParseVector2(string s, out Vector2 v)
        {
            v = default;
            if (!ParseFloatList(s, 2, 2, out var arr)) return false;
            v = new Vector2(arr[0], arr[1]);
            return true;
        }

        public bool TryParseVector3(string s, out Vector3 v)
        {
            v = default;
            if (!ParseFloatList(s, 3, 3, out var arr)) return false;
            v = new Vector3(arr[0], arr[1], arr[2]);
            return true;
        }

        public bool TryParseVector4(string s, out Vector4 v)
        {
            v = default;
            if (!ParseFloatList(s, 4, 4, out var arr)) return false;
            v = new Vector4(arr[0], arr[1], arr[2], arr[3]);
            return true;
        }

        public bool TryParseVector2Int(string s, out Vector2Int v)
        {
            v = default;
            if (!ParseFloatList(s, 2, 2, out var arr)) return false;
            v = new Vector2Int(Mathf.RoundToInt(arr[0]), Mathf.RoundToInt(arr[1]));
            return true;
        }

        public bool TryParseVector3Int(string s, out Vector3Int v)
        {
            v = default;
            if (!ParseFloatList(s, 3, 3, out var arr)) return false;
            v = new Vector3Int(Mathf.RoundToInt(arr[0]), Mathf.RoundToInt(arr[1]), Mathf.RoundToInt(arr[2]));
            return true;
        }

        public bool TryParseQuaternion(string s, out Quaternion q)
        {
            q = default;
            if (!ParseFloatList(s, 3, 4, out var arr)) return false;

            if (arr.Length == 3)
            {
                // 视为欧拉角（度）
                q = Quaternion.Euler(arr[0], arr[1], arr[2]);
            }
            else
            {
                // x,y,z,w
                q = new Quaternion(arr[0], arr[1], arr[2], arr[3]);
            }
            return true;
        }

        public bool TryParseColor(string s, out Color c)
        {
            c = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            // Hex: #RGB #RGBA #RRGGBB #RRGGBBAA / 0x...
            if (s.StartsWith("#") || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = s.StartsWith("#") ? s.Substring(1) :
                             s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;

                if (hex.Length is 3 or 4 or 6 or 8)
                {
                    // Expand shorthand (#RGB / #RGBA)
                    if (hex.Length == 3 || hex.Length == 4)
                    {
                        var expanded = "";
                        foreach (char ch in hex)
                            expanded += new string(ch, 2);
                        hex = expanded;
                    }

                    byte r = 255, g = 255, b = 255, a = 255;
                    if (hex.Length == 6)
                    {
                        if (!TryByte(hex[0..2], out r) ||
                            !TryByte(hex[2..4], out g) ||
                            !TryByte(hex[4..6], out b))
                            return false;
                    }
                    else if (hex.Length == 8)
                    {
                        if (!TryByte(hex[0..2], out r) ||
                            !TryByte(hex[2..4], out g) ||
                            !TryByte(hex[4..6], out b) ||
                            !TryByte(hex[6..8], out a))
                            return false;
                    }
                    c = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                    return true;
                }
                return false;
            }

            // 数组形式: r,g,b,(a)  允许 0-1 或 0-255, 若出现 >1 则整体按 0-255 处理
            if (!ParseFloatList(s, 3, 4, out var arr)) return false;

            bool treatAs255 = arr.Any(x => x > 1.00001f);
            float rF = arr[0], gF = arr[1], bF = arr[2];
            float aF = arr.Length == 4 ? arr[3] : 1f;

            if (treatAs255)
            {
                rF /= 255f; gF /= 255f; bF /= 255f; aF /= 255f;
            }

            c = new Color(rF, gF, bF, aF);
            return true;

            static bool TryByte(string hex2, out byte b) => byte.TryParse(hex2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        #endregion

        #region Asset / Object Loading

        public T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public T LoadByPath<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public Sprite LoadSpriteByGuid(string guid, string spriteName = null)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return LoadSpriteByPath(path, spriteName);
        }

        public Sprite LoadSpriteByPath(string path, string spriteName = null)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (string.IsNullOrEmpty(spriteName))
            {
                // 尝试直接当作单 Sprite
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s) return s;
                // 多 Sprite 图集
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                return all.OfType<Sprite>().FirstOrDefault();
            }
            else
            {
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                return all.OfType<Sprite>().FirstOrDefault(x => x.name == spriteName);
            }
        }

        public GameObject LoadPrefabByGuid(string guid, string prefabName = null)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return LoadPrefabByPath(path, prefabName);
        }

        public GameObject LoadPrefabByPath(string path, string prefabName = null)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (string.IsNullOrEmpty(prefabName))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab)
                {
                    prefab.name = "UnNamePrefab";
                    return prefab;
                }
            }
            // 若需要按 prefabName 精确匹配，可扩展这里逻辑
            return null;
        }

        public Transform LoadTransformByScenePath(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath)) return null;
            // GameObject.Find 支持类似 "Root/Child/Sub"
            var go = GameObject.Find(scenePath);
            return go ? go.transform : null;
        }

        public Transform LoadTransformFromPrefab(string prefabGuid, string childPath = null)
        {
            var prefab = LoadByGuid<GameObject>(prefabGuid);
            if (!prefab) return null;
            if (string.IsNullOrEmpty(childPath))
                return prefab.transform;

            return FindChildByRelativePath(prefab.transform, childPath);
        }

        private static Transform FindChildByRelativePath(Transform root, string relativePath)
        {
            if (!root || string.IsNullOrEmpty(relativePath)) return null;
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            foreach (var p in parts)
            {
                bool found = false;
                for (int i = 0; i < current.childCount; i++)
                {
                    var c = current.GetChild(i);
                    if (string.Equals(c.name, p, StringComparison.Ordinal))
                    {
                        current = c;
                        found = true;
                        break;
                    }
                }
                if (!found) return null;
            }
            return current;
        }

        #endregion

        #region Logging

        public void LogWarning(int line, string msg)
        {
            Debug.LogWarning($"[CSV:{_contextName}] 行 {line}: {msg}");
        }

        public void LogError(int line, string msg)
        {
            Debug.LogError($"[CSV:{_contextName}] 行 {line}: {msg}");
        }

        #endregion
    }
}