using System;
using System.Collections.Generic;

namespace Game.Data.Core
{
    /// <summary>
    /// 所有“定义注册表”统一实现，允许通过 DefinitionType 做动态检索。
    /// 约定：
    /// - TryGetRaw(id, out IDefinition) 访问单条
    /// - TryGetAllRaw(out IReadOnlyLis) 批量列举（尽量返回内部缓存，避免分配）
    /// </summary>
    public interface IDefinitionRegistry
    {
        Type DefinitionType { get; }

        bool TryGetRaw(int id, out IDefinition def);

        /// <summary>
        /// 获取该注册表内所有定义（只读列表），若没有则返回 false。
        /// 实现方应该尽量缓存列表以避免重复分配。
        /// </summary>
        bool TryGetAllRaw(out IReadOnlyList<IDefinition> defs);
    }
}
