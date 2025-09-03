using Game.Core.Debug.FastString;
using Game.Data.Core;
using Game.Singletons;
using System.Diagnostics;
using UnityEngine;
using static Game.Singletons.Core.SingletonInterfaces;

namespace Game.Data
{
    /// <summary>统一上下文：通过 SingletonManager 初始化。</summary>
    public class GameContext : MonoBehaviour, IPreInitialize, ISyncInitialize, IOnRegistered
    {
        [SerializeField] private GameContextConfig config;

        public static GameContext Instance { get; private set; }
        public GameRuntimeData Data { get; private set; }

        private bool _built;

        public void OnRegistered(SingletonManager manager)
        {
            Instance = this;
        }

        public void PreInitialize() { /* 预留 */ }

        public void Initialize()
        {
            if (_built) return;
            BuildAll();
            _built = true;
        }

        private void BuildAll()
        {
            Data = new GameRuntimeData();
            var sw = Stopwatch.StartNew();

            var sectionTypes = RuntimeConfigLoader.GetSectionTypes(config.forceRescanOnPlay);
            if (config.verbose)
            {
                using var fs = FastString.Acquire()
                    .Tag("GameContext")
                    .T(" 开始构建")
                    .T(" 配置数=").I(sectionTypes.Count)
                    .Log();
            }

            int sectionAssetCount = 0;
            foreach (var t in sectionTypes)
            {
                var assets = RuntimeConfigLoader.LoadAllSectionsOfType(t, config.verbose);
                foreach (var s in assets)
                {
                    try
                    {
                        s.Build(Data);
                        sectionAssetCount++;
                    }
                    catch (System.Exception ex)
                    {
                        using var fs = FastString.Acquire()
                            .Tag("GameContext")
                            .T(" 构建异常 SectionType=").T(t.Name)
                            .T(" AssetName=").T(s.SectionName)
                            .T(" Exception=").T(ex.ToString())
                            .Log();
                    }
                }
            }

            sw.Stop();
            if (config.verbose)
            {
                using var fs = FastString.Acquire()
                    .Tag("GameContext")
                    .T(" 构建完成")
                    .T(" 耗时=").L(sw.ElapsedMilliseconds)
                    .T(" 配置数=").I(sectionTypes.Count)
                    .T(" 资产数=").I(sectionAssetCount)
                    .Log();
            }
                

            if (config.logSummaryAfterBuild)
                Data.LogSummary();
        }

        #region Helper API

        public bool TryGetRegistry<TRegistry>(out TRegistry reg) where TRegistry : class
            => Data.TryGet(out reg);

        public TRegistry GetRegistry<TRegistry>() where TRegistry : class
            => Data.Get<TRegistry>();

        public bool TryGetDefinition<TDef>(int id, out TDef def) where TDef : class, IDefinition
            => Data.TryGetDefinition(id, out def);

        public TDef GetDefinition<TDef>(int id) where TDef : class, IDefinition
            => Data.GetDefinition<TDef>(id);

        #endregion
    }
}