using System;

namespace Game.Save.Core
{
    [Serializable]
    public class SectionBlob
    {
        public string key;
        public string type;    // AssemblyQualifiedName 或 FullName（依据配置）
        public string json;    // 子对象自己的 JSON（再次用 JsonUtility 解析）
    }
}
