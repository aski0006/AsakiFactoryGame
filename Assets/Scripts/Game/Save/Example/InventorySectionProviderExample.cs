using Game.Save.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Save.Example
{
    public class InventorySectionProviderExample : MonoBehaviour,
                                                   ISaveSectionProvider,
                                                   ICustomSaveSectionKey,
                                                   IExposeSectionType
    {
        [Serializable]
        public class InventorySection : ISaveSection
        {
            public List<ItemData> items = new();
        }

        [Serializable]
        public class ItemData
        {
            public string id;
            public int count;
            public int durability;
        }

        private readonly List<ItemData> _runtimeItems = new();
        public string Key => "Inventory";
        public bool Ready => true;

        public int TotalItems => _runtimeItems.Count;

        public ISaveSection Capture()
        {
            // 直接复制
            var sec = new InventorySection();
            foreach (var it in _runtimeItems)
            {
                sec.items.Add(new ItemData
                {
                    id = it.id,
                    count = it.count,
                    durability = it.durability
                });
            }
            return sec;
        }

        public void Restore(ISaveSection section)
        {
            var sec = section as InventorySection;
            _runtimeItems.Clear();
            if (sec == null) return;
            foreach (var it in sec.items)
            {
                _runtimeItems.Add(new ItemData
                {
                    id = it.id,
                    count = it.count,
                    durability = it.durability
                });
            }
        }

        public void AddItem(string id, int count = 1)
        {
            var found = _runtimeItems.Find(i => i.id == id);
            if (found == null)
            {
                _runtimeItems.Add(new ItemData { id = id, count = count, durability = 100 });
            }
            else
            {
                found.count += count;
                found.durability = Mathf.Max(found.durability, 100); // 简单示例
            }
        }

        public void ConsumeOne()
        {
            if (_runtimeItems.Count == 0) return;
            var i = _runtimeItems[0];
            i.count--;
            if (i.count <= 0) _runtimeItems.RemoveAt(0);
        }

        public IEnumerable<string> DebugItemList()
        {
            foreach (var it in _runtimeItems)
                yield return $"{it.id}x{it.count}";
        }

        public void ResetInventory() => _runtimeItems.Clear();

        private void OnEnable() => SaveManager.Instance?.RegisterProvider(this);
        private void OnDisable() => SaveManager.Instance?.UnregisterProvider(this);
        public Type SectionType => typeof(InventorySection);
    }
}
