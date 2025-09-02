using Game.Singletons.Core.Game.Singletons;
using System.Collections;

namespace Game.Singletons.Core
{
    public class SingletonInterfaces
    {
        /// <summary>标记接口（非必须，仅用于分类）。</summary>
        public interface ISingletonModule { }

        /// <summary>优先级阶段 1：在其它初始化之前执行，可做数据预读取。</summary>
        public interface IPreInitialize
        {
            void PreInitialize();
        }

        /// <summary>优先级阶段 2：同步初始化（轻量逻辑、建立引用）。</summary>
        public interface ISyncInitialize
        {
            void Initialize();
        }

        /// <summary>优先级阶段 3：异步初始化（加载资源、网络请求等）。</summary>
        public interface IAsyncInitialize
        {
#if USE_UNITASK
        Cysharp.Threading.Tasks.UniTask InitializeAsync();
#else
            /// <summary>协程版异步初始化：返回 IEnumerator。</summary>
            IEnumerator InitializeAsync();
#endif
        }

        /// <summary>当刚被注册进入管理器时回调（可获取其它已注册实例）。</summary>
        public interface IOnRegistered
        {
            void OnRegistered(SingletonManager manager);
        }
    }
}
