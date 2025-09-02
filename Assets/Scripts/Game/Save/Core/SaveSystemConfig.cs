using Game.ScriptableObjectDB;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Save.Core
{
    [CustomConfig]
    [CreateAssetMenu(menuName = "Game/Save/Save System Config", fileName = "SaveSystemConfig")]
    public class SaveSystemConfig : ScriptableObject
    {
        [Header("路径 & 文件")]
        public string fileName = "save.dat";
        public bool useBackup = true;

        [Header("序列化")]
        public bool prettyJson = false;
        public bool storeTypeAssemblyQualified = false; // true: AssemblyQualifiedName; false: FullName

        [Header("加密 / 哈希")]
        public bool enableEncryption = false;
        public bool enableHash = true;
        public bool useSha256 = true; // false = NOOP Hash
        [Tooltip("占位：若后续实现 AES，可放密钥/盐。")]
        public string encryptionKey = "";

        [Header("自动保存")]
        public bool saveOnQuit = true;
        public bool saveOnPause = true;
        public bool saveOnFocusLost = true;
        public bool autoIntervalEnabled = false;
        public float autoIntervalSeconds = 120f;

        [Header("初始化")]
        public bool loadOnAwake = true;
        public bool restoreAfterSingletonsReady = true; // 等 SingletonManager 初始化完再 Restore
        public bool autoDiscoverProvidersOnSceneLoad = true;
        public bool discoverInactiveGameObjects = false;

        [Header("Provider 过滤")]
        public List<string> excludeTypeNames = new(); // 完整类型名（FullName）
        public List<string> explicitIncludeTypeNames = new(); // 如果非空，只包含这些

        [Header("调试")]
        public bool verboseLog = true;
        public bool logOnEveryAutoSave = true;

        [Header("其他")]
        public bool writeImmediatelyAfterFirstLoad = false; // 首次无存档 -> 创建后立刻写入
    }
}
