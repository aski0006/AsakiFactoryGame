using Game.Features.Production.Inventory;
using UnityEngine;

namespace Game.Features.Production.Debug
{
    /// <summary>
    /// 单独的背包调试（可选）。
    /// </summary>
    public class InventoryDebugPanel : MonoBehaviour
    {
        public InventoryComponent inventory;
        private Vector2 _scroll;

        private void Awake()
        {
            if (!inventory) inventory =FindFirstObjectByType<InventoryComponent>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 260, 10, 250, 400), "Inventory Debug", GUI.skin.window);
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (inventory == null)
            {
                GUILayout.Label("No InventoryComponent found.");
            }
            else
            {
                var slots = inventory.Slots;
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    GUILayout.Label($"{i:D2}: {(s.IsEmpty ? "(empty)" : s.ToString())}");
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
