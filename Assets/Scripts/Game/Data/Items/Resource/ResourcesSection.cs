// ResourcesSection.cs

using Game.Data.Core;
using Game.ScriptableObjectDB;
using UnityEngine;

namespace Game.Data.Items.Resource
{
    // 让 ConfigDatabaseWindow 能看到（CustomConfig 只是编辑器标签）
    [CustomConfig]
    [CreateAssetMenu(menuName = "Game/Config/Resources Section")]
    public class ResourcesSection : DefinitionSectionBase<ResourceDefinition>
    {
        public override string SectionName => "Resources"; // SectionName 用于按名字取
        
    }
}