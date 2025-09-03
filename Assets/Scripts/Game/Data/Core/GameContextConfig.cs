using Game.ScriptableObjectDB;
using UnityEngine;

namespace Game.Data.Core
{
    [CustomConfig]
    public class GameContextConfig : ScriptableObject
    {
        [Header("Config Load Options")]
        public bool verbose = true;
        public bool forceRescanOnPlay = false;
        public bool logSummaryAfterBuild = true;
    }
}
