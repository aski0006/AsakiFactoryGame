using System.Collections.Generic;
using Game.Data;
using Game.Data.Definition.Machines;
using Game.Data.Definition.Recipes;

namespace Game.Features.Production.Machines
{
    public static class MachineRecipeHelper
    {
        private static readonly Dictionary<MachineType, List<BaseRecipeDefinition>> _cache = new();
        private static List<BaseRecipeDefinition> _all;
        private static bool _collected;

        public static IReadOnlyList<BaseRecipeDefinition> GetRecipesFor(MachineType type)
        {
            if (!_collected) CollectAll();
            if (_cache.TryGetValue(type, out var list))
                return list;

            list = new List<BaseRecipeDefinition>(16);
            foreach (var r in _all)
            {
                if (r != null && r.MachineType == type)
                    list.Add(r);
            }
            _cache[type] = list;
            return list;
        }

        public static void ClearCache()
        {
            _cache.Clear();
            _all = null;
            _collected = false;
        }

        private static void CollectAll()
        {
            _all = new List<BaseRecipeDefinition>(32);
            var runtime = GameContext.Instance.Data; // 确保 GameContext 暴露 RuntimeData
            foreach (var r in runtime.EnumerateDefinitions<BaseRecipeDefinition>())
            {
                if (r != null) _all.Add(r);
            }
#if UNITY_EDITOR
            UnityEngine.Debug.Log($"[MachineRecipeHelper] Collected {_all.Count} recipes.");
#endif
            _collected = true;
        }
    }
}
