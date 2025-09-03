using UnityEngine;

namespace Game.ScriptableObjectDB.CSV
{
    /// <summary>
    /// 导入辅助：提供类型转换与资产加载。
    /// 统一放这里，避免在各 Section 内部重复写 GUID -> Asset 的逻辑。
    /// </summary>
    public interface ICsvImportHelper
    {
        bool TryParseInt(string s, out int v);
        bool TryParseFloat(string s, out float v);
        bool TryParseBool(string s, out bool v);
        bool TryParseEnum<T>(string s, out T v) where T : struct;
        string Normalize(string s);

        // 资源加载
        T LoadByGuid<T>(string guid) where T : Object;
        T LoadByPath<T>(string path) where T : Object;

        // Sprite 特化：支持单 Sprite 或图集中子 Sprite
        Sprite LoadSpriteByGuid(string guid, string spriteName = null);
        Sprite LoadSpriteByPath(string path, string spriteName = null);

        GameObject LoadPrefabByGuid(string guid, string prefabName = null);
        
        GameObject LoadPrefabByPath(string path, string prefabName = null);
        
        // 日志
        void LogWarning(int line, string msg);
        void LogError(int line, string msg);
    }
}
