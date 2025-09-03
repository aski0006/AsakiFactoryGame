using Game.Data.Core;
using System;
using UnityEngine;

namespace Game.Data.Items.Resource
{
    [Serializable]
    public class ResourceDefinition : IDefinition
    {
        [SerializeField] private int id; // 使用枚举作为 ID
        public int Id => id; // 实现 IDefinition.Id
        public string resourceName; // 资源名称
        public Sprite icon;
    }
}
