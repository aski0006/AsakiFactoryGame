namespace Game.Save.Core
{
    /// <summary>
    /// 标记接口：一个可序列化的“存档分段”数据（Snapshot）。
    /// 只包含纯数据，不包含 Unity 对象引用。
    /// </summary>
    public interface ISaveSection{ }
    
}
