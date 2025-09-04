#if UNITY_EDITOR
using Game.ScriptableObjectDB;
using UnityEngine;

namespace Game.CSV
{
    [CustomConfig]
    [CreateAssetMenu(fileName = "CsvCodeGenSettings", menuName = "Game/CSV/CodeGen Settings")]
    public class CsvCodeGenSettings : ScriptableObject
    {
        public string outputFolder = "Assets/Generated/CsvBindings";
        public bool verbose = true;
        public bool generateEditorSetters = true;

        [Header("增量生成")]
        public bool incremental = true;
        public bool deleteStaleFiles = true;

        [Header("字典序列化")]
        public char defaultKeyValueSeparator = ':';   // 允许在将来修改
    }
}
#endif
