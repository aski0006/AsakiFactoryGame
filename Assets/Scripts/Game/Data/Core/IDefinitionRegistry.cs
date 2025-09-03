using System;

namespace Game.Data.Core
{
    /// <summary>
    /// 所有“定义注册表”统一实现，允许通过 DefinitionType 做动态检索。
    /// </summary>
    public interface IDefinitionRegistry
    {
        Type DefinitionType { get; }
        bool TryGetRaw(int id, out IDefinition def);
    }
}
