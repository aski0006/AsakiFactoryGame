/*
 * SaveManagerExample.cs
 * 示例演示：
 *  1. 三个独立的存档 Section Provider：玩家(Player)、背包(Inventory)、设置(Settings)。
 *  2. 每个 Provider 只保存本模块“必要的纯数据”，不直接序列化 Unity 对象引用。
 *  3. 使用 ICustomSaveSectionKey & IExposeSectionType 保证跨版本重命名安全。
 *  4. 提供一个简单的 OnGUI 面板（可选，用于快速调试：保存/加载/加金币/升级/重置等）。
 *
 * 使用步骤：
 *  1. 在场景中新建空物体 “SaveExampleRoot”，挂本脚本 (或只挂下面任意 Provider)。
 *  2. 确保你的 SaveManager (单例) 已由 SingletonManager 生成并引用有效的 SaveSystemConfig，
 *     且 SaveSystemConfig 中 autoDiscoverProvidersOnSceneLoad = true 或者 Provider 会在 OnEnable 自行注册。
 *  3. 运行游戏，使用左上角的 OnGUI 按钮操作，观察 Console 与持久化目录文件变化。
 *
 * 注意：
 *  - 这里的 Section 类型是内部嵌套类，也可以放到独立文件（只要实现 ISaveSection）。
 *  - 如果未来改为 Newtonsoft 或二进制，只需替换 ISaveSerializer 实现，不需要改 Provider。
 */

using UnityEngine;
// 引入接口
using Random = UnityEngine.Random;

namespace Game.Save.Example
{
    public class SaveManagerExample : MonoBehaviour
    {
        [Header("调试 GUI")]
        public bool showGui = true;
        public KeyCode toggleKey = KeyCode.F10;

        private PlayerSectionProviderExample _player;
        private InventorySectionProviderExample _inventory;
        private SettingsSectionProviderExample _settings;

        private void Awake()
        {
            // 也可以手动创建 Provider，或者在场景中分别挂载
            _player ??= gameObject.AddComponent<PlayerSectionProviderExample>();
            _inventory ??= gameObject.AddComponent<InventorySectionProviderExample>();
            _settings ??= gameObject.AddComponent<SettingsSectionProviderExample>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showGui = !showGui;
            }
        }

        private void OnGUI()
        {
            if (!showGui) return;

            GUILayout.BeginArea(new Rect(12, 12, 340, 560), GUI.skin.box);
            GUILayout.Label("<b><color=#6cf>SaveManager Example</color></b>", Rich());

            if (SaveManager.Instance == null)
            {
                GUILayout.Label("SaveManager 未初始化。请确认已通过 SingletonManager 或场景中放置。");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(6);
            GUILayout.Label($"Loaded: <b>{GetBool(SaveManager.Instance, "_loaded")}</b>  Restored: <b>{GetBool(SaveManager.Instance, "_restored")}</b>", Rich());
            GUILayout.Label($"Sections: <b>{GetInt(SaveManager.Instance, "_sectionCount")}</b>  Version: <b>{GetInt(SaveManager.Instance, "_loadedVersion")}</b>", Rich());

            GUILayout.Space(8);
            GUILayout.Label("<b>玩家</b>", Rich());
            GUILayout.Label($"ID: {_player.PlayerId}");
            GUILayout.Label($"Level: {_player.Level}  Exp: {_player.Exp:F1}");
            GUILayout.Label($"HP: {_player.Hp:F1}/{_player.MaxHp}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("加经验 +5")) { _player.AddExp(5); }
            if (GUILayout.Button("受伤 -10HP")) { _player.TakeDamage(10); }
            if (GUILayout.Button("回血 +20HP")) { _player.Heal(20); }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>背包</b>", Rich());
            GUILayout.Label("物品数量: " + _inventory.TotalItems);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("随机加物品"))
            {
                var id = "item_" + Random.Range(1, 4);
                _inventory.AddItem(id, Random.Range(1, 5));
            }
            if (GUILayout.Button("消耗一个"))
            {
                _inventory.ConsumeOne();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(string.Join(", ", _inventory.DebugItemList()));

            GUILayout.Space(4);
            GUILayout.Label("<b>设置</b>", Rich());
            GUILayout.Label($"Master={_settings.Master:0.00}  BGM={_settings.Bgm:0.00}  SFX={_settings.Sfx:0.00}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("降低BGM"))
                _settings.SetBgm(Mathf.Clamp01(_settings.Bgm - 0.1f));
            if (GUILayout.Button("提高BGM"))
                _settings.SetBgm(Mathf.Clamp01(_settings.Bgm + 0.1f));
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>存档操作</b>", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存 (ManualSave)"))
                SaveManager.Instance.ManualSave();
            if (GUILayout.Button("加载+恢复"))
                SaveManager.Instance.ManualLoad(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("强制 Restore (Dev)"))
                SaveManager.Instance.DevForceRestore();
            if (GUILayout.Button("强制 Save (Dev)"))
                SaveManager.Instance.DevForceSave();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("删除存档 (DevDeleteSave)"))
            {
                if (EditorLikeConfirm("确定删除存档?"))
                    SaveManager.Instance.DevDeleteSave();
            }

            GUILayout.Space(8);
            if (GUILayout.Button("清空 & 重建默认数据"))
            {
                _player.ResetToDefault();
                _inventory.ResetInventory();
                _settings.ResetSettings();
                SaveManager.Instance.ManualSave();
            }

            GUILayout.Label("<i>（F10 显示/隐藏面板）</i>", Rich());
            GUILayout.EndArea();
        }

        // 仅在编辑器内弹窗，运行时直接真
        private bool EditorLikeConfirm(string msg)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.DisplayDialog("确认", msg, "是", "否");
#else
            return true;
#endif
        }

        private GUIStyle Rich()
        {
            var s = new GUIStyle(GUI.skin.label);
            s.richText = true;
            return s;
        }

        private string GetBool(object obj, string field) => GetPrivateField<bool>(obj, field).ToString();
        private string GetInt(object obj, string field) => GetPrivateField<int>(obj, field).ToString();
        private T GetPrivateField<T>(object obj, string name)
        {
            if (obj == null) return default;
            var f = obj.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f == null) return default;
            return (T)f.GetValue(obj);
        }
    }

}