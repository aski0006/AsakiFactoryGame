namespace Game.Data.Core
{
    /// <summary>
    /// 可被 GameContext 构建的配置分区（数据库集合）。
    /// </summary>
    public interface IContextSection : IContext
    {
        string SectionName { get; }
        void Build(GameRuntimeData data);
    }
}
