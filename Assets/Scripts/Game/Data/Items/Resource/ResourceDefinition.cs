using Game.Data.Core;
using System;
using UnityEngine;

namespace Game.Data.Items.Resource
{
    [Serializable]
    public class ResourceDefinition : IDefinition
    {
        [SerializeField] private int id; // 使用枚举作为 ID
        [SerializeField] private string codeName; // 资源代码名称
        public int Id => id; // 实现 IDefinition.Id
        public string CodeName => codeName;
        
        public Sprite icon;
    }
}
