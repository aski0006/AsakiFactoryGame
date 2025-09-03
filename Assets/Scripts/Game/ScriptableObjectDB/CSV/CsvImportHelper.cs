using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.ScriptableObjectDB.CSV
{
    public class CsvImportHelper : ICsvImportHelper
    {
        private readonly string _contextName;

        public CsvImportHelper(string contextName)
        {
            _contextName = contextName;
        }

        public bool TryParseInt(string s, out int v) => int.TryParse(s, out v);
        public bool TryParseFloat(string s, out float v) => float.TryParse(s, out v);

        public bool TryParseBool(string s, out bool v)
        {
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
            return null;
        }

        public void LogWarning(int line, string msg)
        {
            Debug.LogWarning($"[CSV:{_contextName}] 行 {line}: {msg}");
        }

        public void LogError(int line, string msg)
        {
            Debug.LogError($"[CSV:{_contextName}] 行 {line}: {msg}");
        }
    }
}