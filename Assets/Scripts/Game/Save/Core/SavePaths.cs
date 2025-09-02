using UnityEngine;
using System.IO;

namespace Game.Save.Core
{
    public static class SavePaths
    {
        public const string FileName = "save.dat";
        public static string Dir => Application.persistentDataPath;
        public static string Main => Path.Combine(Dir, FileName);
        public static string Temp => Main + ".tmp";
        public static string Bak  => Main + ".bak";
    }
}
