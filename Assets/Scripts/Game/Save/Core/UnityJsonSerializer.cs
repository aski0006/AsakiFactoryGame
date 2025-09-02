using UnityEngine;

namespace Game.Save.Core
{
    public class UnityJsonSerializer : ISaveSerializer
    {
        private readonly bool _pretty;
        public UnityJsonSerializer(bool pretty = false) => _pretty = pretty;

        public string Serialize<T>(T obj) => JsonUtility.ToJson(obj, _pretty);
        public T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);
        public object Deserialize(System.Type type, string json) => JsonUtility.FromJson(json, type);
    }
}
