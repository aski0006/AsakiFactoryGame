#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 简单一键校验（可扩展成窗口）。
    /// </summary>
    public static class SectionValidationUtility
    {
        [MenuItem("Tools/Config DB/Validate All Sections")]
        public static void ValidateAll()
        {
            var types = RuntimeConfigLoader.GetSectionTypes(true);
            int issues = 0;
            foreach (var t in types)
            {
                var guids = AssetDatabase.FindAssets("t:" + t.Name);
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var obj = AssetDatabase.LoadAssetAtPath(path, t) as ScriptableObject;
                    if (obj is IContextSection s)
                    {
                        // 调用 Build 到临时容器，检查是否抛错
                        var data = new GameRuntimeData();
                        try { s.Build(data); }
                        catch (System.Exception ex)
                        {
                            issues++;
                            Debug.LogError($"[Validate] {t.Name} -> {obj.name} 构建异常: {ex.Message}");
                        }
                    }
                }
            }
            Debug.Log($"[Validate] 完成。Issues={issues}");
        }
    }
}
#endif
