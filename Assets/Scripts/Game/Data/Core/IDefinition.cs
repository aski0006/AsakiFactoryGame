using UnityEngine;

namespace Game.Data.Core
{
    /// <summary>
    /// 基础定义接口：所有可注册条目需提供唯一 Id，并支持一个编辑器辅助描述。
    /// 注意：实现类若使用 private 字段需加 [SerializeField]；建议描述字段命名 editorDescription 或 EditorDescription。
    /// </summary>
    public interface IDefinition
    {
        int Id { get; }
    }
}
