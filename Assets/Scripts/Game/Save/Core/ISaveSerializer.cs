namespace Game.Save.Core
{
    public interface ISaveSerializer
    {
        string Serialize<T>(T obj);
        T Deserialize<T>(string json);
        object Deserialize(System.Type type, string json);
    }
}
