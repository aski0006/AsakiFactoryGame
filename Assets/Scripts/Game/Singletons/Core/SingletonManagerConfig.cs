using Game.ScriptableObjectDB;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Singletons.Core
{

    namespace Game.Singletons
    {
        [CustomConfig]
        [CreateAssetMenu(menuName = "Game/Singletons/Singleton Manager Config", fileName = "SingletonManagerConfig")]
        public class SingletonManagerConfig : ScriptableObject
        {
            [Serializable]
            public class Entry
            {
                [Tooltip("可选：可读的唯一 Key（用于按 key 获取）。")]
                public string key;

                [Tooltip("拖拽一个预制体上的组件（MonoBehaviour）。\n注意应是Project窗口中的预制体实例，而不是场景对象。")]
                public MonoBehaviour prefabComponent;

                [Tooltip("优先级：数值越大越早初始化。")]
                public int priority = 0;

                [Tooltip("若场景中找不到实例时是否自动实例化。")]
                public bool instantiateIfMissing = true;

                [Tooltip("实例化后是否 DontDestroyOnLoad。")]
                public bool dontDestroyOnLoad = true;

                [Tooltip("可选：如果无法找到或实例化则静默跳过，不报错。")]
                public bool optional = false;

                public Type ComponentType => prefabComponent ? prefabComponent.GetType() : null;
            }

            [SerializeField] private List<Entry> entries = new();
            public IReadOnlyList<Entry> Entries => entries;

#if UNITY_EDITOR
            private void OnValidate()
            {
                var keySet = new HashSet<string>(StringComparer.Ordinal);
                var typeSet = new HashSet<Type>();
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    if (!string.IsNullOrEmpty(e.key) && !keySet.Add(e.key))
                    {
                        Debug.LogWarning($"[SingletonManagerConfig] 重复的 key: {e.key}", this);
                    }
                    if (e.prefabComponent)
                    {
                        var t = e.prefabComponent.GetType();
                        if (!typeSet.Add(t))
                        {
                            Debug.LogWarning($"[SingletonManagerConfig] 重复的组件类型: {t.Name}", this);
                        }
                    }
                }
            }
#endif
        }
    }
}
