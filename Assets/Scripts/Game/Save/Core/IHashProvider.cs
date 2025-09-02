namespace Game.Save.Core
{
    public interface IHashProvider
    {
        string ComputeHash(byte[] data);
    }

    public sealed class Sha256HashProvider : IHashProvider
    {
        public static readonly Sha256HashProvider Instance = new Sha256HashProvider();
        private Sha256HashProvider(){}
        public string ComputeHash(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new System.Text.StringBuilder(hash.Length*2);
            foreach(var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    public sealed class NoOpHashProvider : IHashProvider
    {
        public static readonly NoOpHashProvider Instance = new();
        private NoOpHashProvider(){}
        public string ComputeHash(byte[] data)=>"NO_HASH";
    }
}
