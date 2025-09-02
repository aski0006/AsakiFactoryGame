using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.ProjectTools
{
    /// <summary>
    /// 持久化 ProjectStructurePrinterWindow 选项的 ScriptableObject。
    /// 如果想扩展字段，只需在 SettingsData 中新增 public 字段。
    /// </summary>
    public class ProjectStructurePrinterSettings : ScriptableObject
    {
        private const string DefaultAssetDir = "Assets/Editor/ProjectTools";
        private const string DefaultAssetPath = DefaultAssetDir + "/ProjectStructurePrinterSettings.asset";

        [System.Serializable]
        public class SettingsData
        {
            [Header("遍历")]
            public int maxDepth = 20;
            public bool includeFiles = true;
            public bool includeHidden = false;
            public bool showFileSize = false;
            public bool useUnicodeTree = true;
            public bool sortFoldersFirst = true;
            public bool alphabetical = true;
            public bool incremental = false;
            public bool collapseSingleChild = false;
            public long minFileSizeBytes = 0;

            [Header("过滤")]
            public string fileExtensionsFilter = "";
            public string ignoreNamePatterns = "obj,bin,temp,.DS_Store,Thumbs.db";
            public string ignorePathContains = "Packages~,__backup__,~$";
            public bool ignoreMeta = true;

            [Header("输出")]
            public bool trimTrailingSpaces = true;
        }

        [SerializeField] private SettingsData data = new SettingsData();
        public SettingsData Data => data;

        #region API

        public static ProjectStructurePrinterSettings LoadOrCreate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ProjectStructurePrinterSettings>(DefaultAssetPath);
            if (asset == null)
            {
                if (!Directory.Exists(DefaultAssetDir))
                {
                    Directory.CreateDirectory(DefaultAssetDir);
                }
                asset = ScriptableObject.CreateInstance<ProjectStructurePrinterSettings>();
                AssetDatabase.CreateAsset(asset, DefaultAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return asset;
        }

        public static string GetDefaultPath() => DefaultAssetPath;

        public void ResetToDefault()
        {
            data = new SettingsData();
            EditorUtility.SetDirty(this);
        }

        #endregion
    }
}