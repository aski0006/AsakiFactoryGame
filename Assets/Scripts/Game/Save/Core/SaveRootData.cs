using System;
using UnityEngine;
using System.Collections.Generic;

namespace Game.Save.Core
{
    /// <summary>
    /// 顶层聚合存档（取代单一 SaveData）。可扩展字段。
    /// Version 用于整体格式迁移。
    /// </summary>
    [Serializable]
    public class SaveRootData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public long lastSaveUnix;
        
        public List<SectionBlob> sections = new List<SectionBlob>();
    }

   
}
