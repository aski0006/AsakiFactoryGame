using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Save.Core
{
    /// <summary>
    /// 运行时收集所有 ISaveSectionProvider 的入口。可由你的 SingletonManager 来提供。
    /// 简易实现用一个静态列表。
    /// </summary>
    public interface ISaveSectionRegistry
    {
        IReadOnlyList<ISaveSectionProvider> Providers { get; }
        void Register(ISaveSectionProvider provider);
        void Unregister(ISaveSectionProvider provider);
    }

    public class SimpleSaveSectionRegistry : ISaveSectionRegistry
    {
        private readonly List<ISaveSectionProvider> _list = new();
        public IReadOnlyList<ISaveSectionProvider> Providers => _list;
        public void Register(ISaveSectionProvider provider)
        {
            if (provider == null || _list.Contains(provider)) return;
            _list.Add(provider);
        }
        public void Unregister(ISaveSectionProvider provider)
        {
            if (provider == null) return;
            _list.Remove(provider);
        }
    }

    public class AutoSaveSectionRegistry : ISaveSectionRegistry
    {
        private readonly List<ISaveSectionProvider> _list = new List<ISaveSectionProvider>();
        public IReadOnlyList<ISaveSectionProvider> Providers => _list;

        [Obsolete("Use RegisterAllFromAssemblies instead")]
        public void Register(ISaveSectionProvider provider)
        {
            if (provider == null || _list.Contains(provider)) return;
            _list.Add(provider);
        }

        /// <summary>
        /// 自动注册所有 ISaveSectionProvider 实现
        /// </summary>
        public void RegisterAllFromAssemblies()
        {
            // 自动注册, 从程序集中扫描所有 ISaveSectionProvider 实现
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(ISaveSectionProvider).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract);

            foreach (var type in types)
            {
                if (Activator.CreateInstance(type) is ISaveSectionProvider instance)
                {
                    Register(instance);
                }
            }
        }

        public void Unregister(ISaveSectionProvider provider)
        {
            if (provider == null) return;
            _list.Remove(provider);
        }
    }
}
