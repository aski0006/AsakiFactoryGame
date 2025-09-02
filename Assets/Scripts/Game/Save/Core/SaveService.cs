using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Save.Core
{
    /// <summary>
    /// 处理：聚合数据 -> json -> (encrypt + hash) -> 文件 I/O
    /// </summary>
    public class SaveService
    {
        private readonly ISaveSerializer _serializer;
        private readonly ISaveEncryptor _encryptor;
        private readonly IHashProvider _hash;
        private readonly SaveSystemConfig _cfg;

        public SaveService(ISaveSerializer s, ISaveEncryptor e, IHashProvider h, SaveSystemConfig cfg)
        {
            _serializer = s;
            _encryptor = e;
            _hash = h;
            _cfg = cfg;
        }

        private string MainPath => Path.Combine(Application.persistentDataPath, _cfg.fileName);
        private string TempPath => MainPath + ".tmp";
        private string BakPath => MainPath + ".bak";

        #region Sync Methods
        public void Save(SaveRootData data)
        {
            data.lastSaveUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            data.version = SaveRootData.CurrentVersion;

            var json = _serializer.Serialize(data);
            var plain = Encoding.UTF8.GetBytes(json);
            var encrypted = _encryptor.Encrypt(plain);
            var hash = _cfg.enableHash ? _hash.ComputeHash(encrypted) : "NO_HASH";
            var base64 = Convert.ToBase64String(encrypted);

            Directory.CreateDirectory(Path.GetDirectoryName(MainPath)!);
            using (var sw = new StreamWriter(TempPath, false, Encoding.UTF8))
            {
                sw.WriteLine("HASH:" + hash);
                sw.WriteLine("DATA:" + base64);
            }

            if (File.Exists(MainPath))
            {
                if (_cfg.useBackup)
                {
                    try { File.Copy(MainPath, BakPath, true); } catch { }
                }
                File.Delete(MainPath);
            }

            File.Move(TempPath, MainPath);

            if (_cfg.verboseLog)
                Debug.Log($"[SaveService] Saved -> {MainPath}  bytes={encrypted.Length}");
        }
        public SaveRootData Load()
        {
            var path = MainPath;
            if (!File.Exists(path)) return null;
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return null;
                if (!lines[0].StartsWith("HASH:") || !lines[1].StartsWith("DATA:")) return null;
                var storedHash = lines[0].Substring(5);
                var base64 = lines[1].Substring(5);
                var encrypted = Convert.FromBase64String(base64);
                if (_cfg.enableHash)
                {
                    var nowHash = _hash.ComputeHash(encrypted);
                    if (!string.Equals(storedHash, nowHash, StringComparison.Ordinal))
                    {
                        Debug.LogWarning("[SaveService] Hash mismatch, try backup...");
                        return TryBackup();
                    }
                }
                var plain = _encryptor.Decrypt(encrypted);
                var json = Encoding.UTF8.GetString(plain);
                return _serializer.Deserialize<SaveRootData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveService] Load failed: " + e);
                return TryBackup();
            }
        }
        private SaveRootData TryBackup()
        {
            if (!_cfg.useBackup || !File.Exists(BakPath)) return null;
            try
            {
                File.Copy(BakPath, MainPath, true);
                return Load();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveService] Backup restore failed: " + e);
                return null;
            }
        }
        
        #endregion
        
        #region Async Methods
        
        public async Task SaveAsync(SaveRootData data, CancellationToken ct = default)
        {
            data.lastSaveUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            data.version = SaveRootData.CurrentVersion;

            // CPU 密集部分放在线程池
            var pack = await Task.Run(() =>
            {
                var json = _serializer.Serialize(data);
                var plain = Encoding.UTF8.GetBytes(json);
                var encrypted = _encryptor.Encrypt(plain);
                var hash = _cfg.enableHash ? _hash.ComputeHash(encrypted) : "NO_HASH";
                var base64 = Convert.ToBase64String(encrypted);
                return (encryptedLength: encrypted.Length, hash, base64);
            }, ct).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(MainPath)!);

            // 写临时文件
            var sb = new StringBuilder(pack.hash.Length + pack.base64.Length + 16);
            sb.Append("HASH:").AppendLine(pack.hash);
            sb.Append("DATA:").Append(pack.base64);
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
            await File.WriteAllTextAsync(TempPath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
#else
            using (var sw = new StreamWriter(TempPath, false, Encoding.UTF8))
            {
                await sw.WriteAsync(sb.ToString());
            }
#endif
            // 备份与替换仍用同步文件操作（极短）
            if (File.Exists(MainPath))
            {
                if (_cfg.useBackup)
                {
                    try { File.Copy(MainPath, BakPath, true); } catch { }
                }
                File.Delete(MainPath);
            }
            File.Move(TempPath, MainPath);

            if (_cfg.verboseLog)
                Debug.Log($"[SaveService] (Async) Saved -> {MainPath} bytes={pack.encryptedLength}");
        }
        public async Task<SaveRootData> LoadAsync(CancellationToken ct = default)
        {
            if (!File.Exists(MainPath)) return null;
            try
            {
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                var lines = await File.ReadAllLinesAsync(MainPath, ct).ConfigureAwait(false);
#else
                string[] lines;
                using (var sr = new StreamReader(MainPath, Encoding.UTF8))
                {
                    var list = new System.Collections.Generic.List<string>(2);
                    while (!sr.EndOfStream) list.Add(sr.ReadLine());
                    lines = list.ToArray();
                }
#endif
                if (lines.Length < 2 || !lines[0].StartsWith("HASH:") || !lines[1].StartsWith("DATA:"))
                    return null;

                var storedHash = lines[0].Substring(5);
                var base64 = lines[1].Substring(5);
                var encrypted = Convert.FromBase64String(base64);

                if (_cfg.enableHash)
                {
                    var nowHash = _hash.ComputeHash(encrypted);
                    if (!string.Equals(storedHash, nowHash, StringComparison.Ordinal))
                    {
                        Debug.LogWarning("[SaveService] (Async) Hash mismatch, try backup...");
                        return await TryBackupAsync(ct).ConfigureAwait(false);
                    }
                }

                var plain = _encryptor.Decrypt(encrypted);
                var json = Encoding.UTF8.GetString(plain);
                return _serializer.Deserialize<SaveRootData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveService] (Async) Load failed: " + e);
                return await TryBackupAsync(ct).ConfigureAwait(false);
            }
        }
        private async Task<SaveRootData> TryBackupAsync(CancellationToken ct)
        {
            if (!_cfg.useBackup || !File.Exists(BakPath)) return null;
            try
            {
                File.Copy(BakPath, MainPath, true);
                return await LoadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveService] (Async) Backup restore failed: " + e);
                return null;
            }
        }
        #endregion 
        
    }
}