namespace Game.Save.Core
{
    public interface ISaveEncryptor
    {
        byte[] Encrypt(byte[] raw);
        byte[] Decrypt(byte[] encrypted);
    }

    public sealed class NoOpEncryptor : ISaveEncryptor
    {
        public static readonly NoOpEncryptor Instance = new NoOpEncryptor();
        private NoOpEncryptor() {}
        public byte[] Encrypt(byte[] raw)=>raw;
        public byte[] Decrypt(byte[] encrypted)=>encrypted;
    }
}
