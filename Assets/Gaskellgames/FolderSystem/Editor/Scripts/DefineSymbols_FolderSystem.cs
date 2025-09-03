#if UNITY_EDITOR
using UnityEditor;

namespace Gaskellgames.FolderSystem.EditorOnly
{
    /// <summary>
    /// Code created by Gaskellgames
    /// </summary>
    
    [InitializeOnLoad]
    public class DefineSymbols_FolderSystem
    {
        #region Variables
        
        private static readonly string[] ExtraScriptingDefineSymbols = new string[] { "GASKELLGAMES_FOLDERSYSTEM" };
        
        private static readonly string link_GgCore = "<a href=\"https://assetstore.unity.com/packages/tools/utilities/ggcore-gaskellgames-304325\">GgCore</a>";
        private static readonly string error = $"{link_GgCore} not detected: The Folder System package from Gaskellgames requires {link_GgCore}. Please add the package from the package manager, or claim it for FREE from the Unity Asset Store using the same account that has the Folder System asset licence.";
        
        #endregion
        
        //----------------------------------------------------------------------------------------------------
        
        #region Constructors

        static DefineSymbols_FolderSystem()
        {
#if GASKELLGAMES
            Gaskellgames.EditorOnly.DefineSymbols_GgCore.AddExtraScriptingDefineSymbols(ExtraScriptingDefineSymbols);
#else
            UnityEngine.Debug.LogError(error);
#endif
        }
        
        #endregion
        
    } // class end
}

#endif