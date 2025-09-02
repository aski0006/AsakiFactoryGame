namespace Game.Save.Core
{
    public interface IDirtySaveSectionProvider : ISaveSectionProvider
    {
        bool Dirty { get; }
        void ClearDirty();
    }
}
