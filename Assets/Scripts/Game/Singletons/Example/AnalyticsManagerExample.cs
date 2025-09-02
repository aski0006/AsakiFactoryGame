using Game.Singletons.Core;
using System.Collections;
using UnityEngine;

namespace Game.Singletons.Example
{
    public class AnalyticsManagerExample: MonoBehaviour, SingletonInterfaces.IPreInitialize, SingletonInterfaces.IAsyncInitialize, SingletonInterfaces.ISingletonModule
    {
        public void PreInitialize()
        {
            Debug.Log("[AnalyticsManager] PreInitialize：准备缓存目录。");
        }

#if USE_UNITASK
        public async Cysharp.Threading.Tasks.UniTask InitializeAsync()
        {
            Debug.Log("[AnalyticsManager] AsyncInitialize 开始...");
            await Cysharp.Threading.Tasks.UniTask.Delay(800);
            Debug.Log("[AnalyticsManager] AsyncInitialize 完成。");
        }
#else
        public IEnumerator InitializeAsync()
        {
            Debug.Log("[AnalyticsManager] AsyncInitialize 协程开始...");
            yield return new WaitForSeconds(0.8f);
            Debug.Log("[AnalyticsManager] AsyncInitialize 协程完成。");
        }
#endif
    }
}
