using System;

namespace Game.Features.Production.Machines
{
    /// <summary>
    /// 机器状态脏标记广播：调用 Mark() -> 所有订阅者（存档 Provider）置脏。
    /// </summary>
    public static class MachineRuntimeDirty
    {
        public static event Action OnDirty;
        public static void Mark() => OnDirty?.Invoke();
    }
}
