using UnityEngine;

namespace Game.Singletons.Example
{
    public class AudioManagerExample : MonoBehaviour, SingletonInterfaces.ISyncInitialize, SingletonInterfaces.ISingletonModule
    {
        public void Initialize()
        {
            Debug.Log("[AudioManager] Initialize：加载音频设置 / 建立混音器。");
        }
    }
}
