#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Game.CSV
{
    /// <summary>
    /// 资产解析接口（生成代码在导入时调用）。
    /// 可扩展 Addressables / 自定义加载。
    /// </summary>
    public interface IAssetCsvResolver
    {
        Sprite LoadSpriteByGuid(string guid);
        Object LoadByGuid(string guid, System.Type type);
    }

    /// <summary>
    /// Editor 直接用 AssetDatabase。
    /// </summary>
    public sealed class AssetCsvResolver : IAssetCsvResolver
    {
        public static readonly AssetCsvResolver Instance = new();

        public Sprite LoadSpriteByGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite) return sprite;
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var a in all)
                if (a is Sprite sp) return sp;
            return null;
        }

        public Object LoadByGuid(string guid, System.Type type)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath(path, type);
        }
    }
}
#endif
