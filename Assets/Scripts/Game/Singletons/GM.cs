using Game.Singletons.Game.Singletons;

namespace Game.Singletons
{
    public class GM
    {
        public static T Get<T>() where T : class => SingletonManager.Instance.Get<T>();
        public static bool TryGet<T>(out T val) where T : class => SingletonManager.Instance.TryGet(out val);
    }
}
