#if UNITY_EDITOR
using UnityEngine;

namespace Game.ScriptableObjectDB.CSV
{
    /// <summary>
    /// 可选：生成配置资产（若不需要动态配置，可不创建实例）。
    /// </summary>
    [CustomConfig]
    [CreateAssetMenu(fileName = "CsvCodeGenSettings", menuName = "Game/CSV/CodeGen Settings")]
    public class CsvCodeGenSettings : ScriptableObject
    {
        public string outputFolder = "Assets/Generated/CsvBindings";
        public bool verbose = true;
        public bool generateEditorSetters = true;
    }
}
#endif
