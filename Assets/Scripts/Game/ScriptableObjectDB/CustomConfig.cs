using System;

namespace Game.ScriptableObjectDB
{
    /// <summary>
    /// 标记可被集中配置数据库窗口扫描显示的 ScriptableObject。
    /// 用法: [CustomConfig] public class MyConfig : ScriptableObject { ... }
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomConfig : Attribute {}
}
