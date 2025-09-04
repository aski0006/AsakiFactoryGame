#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace Editor.AutoIDGenerator
{
    [Serializable]
    public class CsvGenManifestEntry
    {
        [XmlAttribute] public string FilePath;
        [XmlAttribute] public string Hash;
        [XmlAttribute] public string TypeFullName;   // Definition 或 Section 类型（可为空）
    }

    [Serializable, XmlRoot("CsvGenManifest")]
    public class CsvGenManifest
    {
        [XmlArray("Files")]
        [XmlArrayItem("File")]
        public List<CsvGenManifestEntry> Files = new();

        public static string ComputeHash(string content)
        {
            using var sha1 = SHA1.Create();
            var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static CsvGenManifest Load(string path)
        {
            if (!File.Exists(path)) return new CsvGenManifest();
            try
            {
                var xml = new XmlSerializer(typeof(CsvGenManifest));
                using var fs = File.OpenRead(path);
                return (CsvGenManifest)xml.Deserialize(fs);
            }
            catch
            {
                return new CsvGenManifest();
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var xml = new XmlSerializer(typeof(CsvGenManifest));
            using var fs = File.Create(path);
            xml.Serialize(fs, this);
        }

        public CsvGenManifestEntry Find(string filePath)
        {
            for (int i = 0; i < Files.Count; i++)
            {
                if (string.Equals(Files[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    return Files[i];
            }
            return null;
        }
    }
}
#endif