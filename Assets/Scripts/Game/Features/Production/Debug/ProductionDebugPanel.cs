using Game.Core.Debug.FastString;
using System.Collections.Generic;
using Game.Data;
using Game.Data.Definition.Items;
using Game.Data.Definition.Recipes;
using Game.Data.Generated;
using Game.Features.Production.Inventory;
using Game.Features.Production.Machines;
using Game.Features.Production.Processing;
using Game.Save;
using Game.Singletons.Core;
using UnityEngine;

namespace Game.Features.Production.Debug
{
    /// <summary>
    /// 生产调试面板：
    /// - 放置机器
    /// - 显示 / 选择配方
    /// - 注入 / 补充输入
    /// - 显示进度 / 状态 / 循环次数
    /// - 收集输出
    ///
    /// 已重构为使用 MachineProcessService API：
    ///  1) 启动配方： process.TryStart(...)
    ///  2) 补料：     process.TryRefillInputs(...)
    ///  3) 收集输出： process.TryCollectOutputs(...)
    ///  4) 停止：     process.Stop(...)
    ///  不再直接写 machine.ActiveRecipeId / Progress / Phase（除极少数清空缓冲的调试操作）。
    /// </summary>
    public class ProductionDebugPanel : MonoBehaviour
    {
        public MachineRuntimeManager machineManager;
        public ProductionTickService tickService;
        public InventoryComponent playerInventory;

        private Vector2 _scroll;
        private int _selectedMachineId;
        private bool _showRecipeList;
        private readonly List<BaseRecipeDefinition> _tempRecipes = new List<BaseRecipeDefinition>();

        // 示例使用固定配方常量数组（后期可改为 MachineRecipeHelper）
        private readonly int[] _recipeConstIds =
        {
            ConfigIds.BaseRecipeDefinition.WoodToWoodPlank,
            ConfigIds.BaseRecipeDefinition.CopperOreToCopperIngot,
            ConfigIds.BaseRecipeDefinition.PlankIngotToKeyFragment,
            ConfigIds.BaseRecipeDefinition.KeyFragmentToDungeonKey
        };

        private GUIStyle _progressBg;
        private GUIStyle _progressFill;
        private GUIStyle _richLabel;
        private bool _stylesReady;

        private MachineProcessService Process => tickService ? tickService.MachineProcessService : null;

        private void Awake()
        {
            if (!machineManager) machineManager = FindFirstObjectByType<MachineRuntimeManager>();
            if (!tickService) tickService = FindFirstObjectByType<ProductionTickService>();
            if (!playerInventory) playerInventory = FindFirstObjectByType<InventoryComponent>();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _progressBg = new GUIStyle(GUI.skin.box);
            _progressFill = new GUIStyle(GUI.skin.box)
            {
                normal =
                {
                    background = MakeTex(1, 1, new Color(0.3f, 0.8f, 0.3f))
                }
            };
            _richLabel = new GUIStyle(GUI.skin.label) { richText = true };

            _stylesReady = true;
        }

        private Texture2D MakeTex(int w, int h, Color c)
        {
            var tex = new Texture2D(w, h);
            var cols = new Color[w * h];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.BeginArea(new Rect(10, 10, 560, Screen.height - 20), "Production Debug", GUI.skin.window);
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (!ValidateRefs())
            {
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            DrawSpawnArea();
            GUILayout.Space(6);
            DrawMachineList();
            GUILayout.Space(6);
            DrawSelectedMachine();
            GUILayout.Space(10);
            DrawInventoryPreview();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool ValidateRefs()
        {
            bool ok = true;
            if (!machineManager)
            {
                GUILayout.Label("<color=red>[Missing] MachineRuntimeManager</color>", _richLabel);
                ok = false;
            }
            if (!playerInventory)
            {
                GUILayout.Label("<color=red>[Missing] InventoryComponent</color>", _richLabel);
                ok = false;
            }
            if (!tickService || Process == null)
            {
                GUILayout.Label("<color=yellow>[Warn] ProductionTickService / ProcessService 未找到 (进度不更新)</color>", _richLabel);
            }
            return ok;
        }

        #region Sections

        private void DrawSpawnArea()
        {
            GUILayout.Label("=== Spawn Machines ===");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn Furnace"))
            {
                var m = machineManager.SpawnMachine(ConfigIds.BaseMachineDefinition.Furnace);
                _selectedMachineId = m.InstanceId;
            }
            if (GUILayout.Button("Spawn Assembler"))
            {
                var m = machineManager.SpawnMachine(ConfigIds.BaseMachineDefinition.Assembler);
                _selectedMachineId = m.InstanceId;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawMachineList()
        {
            GUILayout.Label("=== Machines ===");
            if (machineManager.Machines.Count == 0)
            {
                GUILayout.Label("(No machines)");
                return;
            }

            foreach (var m in machineManager.Machines)
            {
                if (GUILayout.Button($"{m.InstanceId} {m.Definition.CodeName}  Phase={m.Phase}  Prog={m.Progress:0.0}  Cycles={m.CompletedCycles}"))
                {
                    _selectedMachineId = m.InstanceId;
                }
            }
        }

        private void DrawSelectedMachine()
        {
            if (_selectedMachineId == 0) return;
            var m = machineManager.GetByInstanceId(_selectedMachineId);
            if (m == null)
            {
                GUILayout.Label("Selected machine not found.");
                return;
            }

            GUILayout.Space(4);
            GUILayout.Label($"=== Selected Machine #{m.InstanceId} ({m.Definition.DisplayName}) ===");

            if (m.ActiveRecipeId != null)
            {
                var recipe = GameContext.Instance.GetDefinition<BaseRecipeDefinition>(m.ActiveRecipeId.Value);
                GUILayout.Label($"Current Recipe: {recipe.DisplayName}");
                GUILayout.Label(MachineInventoryBridge.FormatRecipeIO(recipe));

                DrawProgressBar(m, recipe);

                GUILayout.BeginHorizontal();
                GUI.enabled = Process != null;
                if (GUILayout.Button("Refill Inputs"))
                {
                    Process?.TryRefillInputs(m, playerInventory);
                }
                GUI.enabled = true;

                if (GUILayout.Button("Clear Buffers"))
                {
                    m.InputBuffer.Clear();
                    m.OutputBuffer.Clear();
                    m.Progress = 0f;
                    if (m.ActiveRecipeId != null)
                    {
                        m.Phase = MachinePhase.PendingInput;
                    }
                }

                GUI.enabled = Process != null;
                if (GUILayout.Button("Stop Recipe"))
                {
                    Process?.Stop(m);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("No active recipe.");
            }

            if (GUILayout.Button(_showRecipeList ? "Hide Recipes" : "Show Recipes"))
                _showRecipeList = !_showRecipeList;

            if (_showRecipeList)
                DrawRecipeSelection(m);

            DrawIOBuffers(m);
        }

        private void DrawProgressBar(MachineRuntimeState m, BaseRecipeDefinition recipe)
        {
            float pct = Mathf.Clamp01(recipe.TimeSeconds <= 0f ? 0f : m.Progress / recipe.TimeSeconds);
            Rect outer = GUILayoutUtility.GetRect(200, 22);
            GUI.Box(outer, GUIContent.none, _progressBg);
            if (pct > 0f)
            {
                var fill = new Rect(outer.x, outer.y, outer.width * pct, outer.height);
                GUI.Box(fill, GUIContent.none, _progressFill);
            }
            GUI.Label(outer, $"Progress {pct * 100f:0.#}% ({m.Progress:0.0}/{recipe.TimeSeconds:0.0}s)  Cycles={m.CompletedCycles}");

            switch (m.Phase)
            {
                case MachinePhase.BlockedOutput:
                    GUILayout.Label("<color=orange>输出缓冲已满，等待清空。</color>", _richLabel);
                    break;
                case MachinePhase.BlockedInput:
                    GUILayout.Label("<color=yellow>缺少输入或尚未注入。</color>", _richLabel);
                    break;
                case MachinePhase.Processing:
                    float remain = Mathf.Max(0, recipe.TimeSeconds - m.Progress);
                    GUILayout.Label($"ETA: {remain:0.0}s", _richLabel);
                    break;
                case MachinePhase.PendingInput:
                    GUILayout.Label("<color=grey>等待输入装载或即将开始。</color>", _richLabel);
                    break;
                case MachinePhase.OutputReady:
                    GUILayout.Label("<color=lime>产出已就绪。</color>", _richLabel);
                    break;
            }
        }

        private void DrawRecipeSelection(MachineRuntimeState m)
        {
            GUILayout.Space(4);
            GUILayout.Label("=== Recipes ===");
            _tempRecipes.Clear();

            foreach (var rid in _recipeConstIds)
            {
                var r = GameContext.Instance.GetDefinition<BaseRecipeDefinition>(rid);
                if (r.MachineType != m.Definition.MachineType) continue;
                _tempRecipes.Add(r);
            }

            if (_tempRecipes.Count == 0)
            {
                GUILayout.Label("(No recipes for this machine type)");
                return;
            }

            foreach (var r in _tempRecipes)
            {
                bool hasAll = MachineInventoryBridge.InventoryHasAllInputs(playerInventory, r);
                string io = MachineInventoryBridge.FormatRecipeIO(r);
                string color = hasAll ? "lime" : "grey";

                GUILayout.BeginVertical("box");
                GUILayout.Label($"<b>{r.DisplayName}</b>  <color={color}>[{r.TimeSeconds:0.0}s]</color>", _richLabel);
                GUILayout.Label(io, _richLabel);

                GUILayout.BeginHorizontal();
                GUI.enabled = Process != null;
                if (GUILayout.Button("Set"))
                {
                    Process?.TryStart(m, r.Id, resetProgress: true);
                }

                GUI.enabled = Process != null;
                if (GUILayout.Button("Set+Load"))
                {
                    if (Process?.TryStart(m, r.Id, resetProgress: true) == true)
                    {
                        // 立即尝试补齐输入
                        Process?.TryRefillInputs(m, playerInventory);
                    }
                }

                GUI.enabled = Process != null && m.ActiveRecipeId == r.Id;
                if (GUILayout.Button("Refill"))
                {
                    Process?.TryRefillInputs(m, playerInventory);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
        }

        private void DrawIOBuffers(MachineRuntimeState m)
        {
            var ctx = GameContext.Instance;

            GUILayout.Space(6);
            GUILayout.Label("Input Buffer:");
            if (m.InputBuffer.Count == 0) GUILayout.Label(" (empty)");
            else
            {
                foreach (var slot in m.InputBuffer)
                {
                    var idef = ctx.GetDefinition<BaseItemDefinition>(slot.itemId);
                    GUILayout.Label($" - {idef.DisplayName} x{slot.count}");
                }
            }

            GUILayout.Label("Output Buffer:");
            if (m.OutputBuffer.Count == 0) GUILayout.Label(" (empty)");
            else
            {
                foreach (var slot in m.OutputBuffer)
                {
                    var idef = ctx.GetDefinition<BaseItemDefinition>(slot.itemId);
                    GUILayout.Label($" - {idef.DisplayName} x{slot.count}");
                }

                GUI.enabled = Process != null;
                if (GUILayout.Button("Take All Outputs → Inventory"))
                {
                    Process?.TryCollectOutputs(m, playerInventory);
                }
                GUI.enabled = true;
            }
        }

        private void DrawInventoryPreview()
        {
            GUILayout.Label("=== Player Inventory (first 15 slots) ===");
            var slots = playerInventory.Slots;
            int shown = Mathf.Min(15, slots.Length);
            for (int i = 0; i < shown; i++)
            {
                var s = slots[i];
                GUILayout.Label($"{i:D2}: {(s.IsEmpty ? "(empty)" : s.ToString())}");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Give Wood x10"))
            {
                playerInventory.AddItem(ConfigIds.BaseItemDefinition.Wood, 10);
                FastString.Acquire().Tag("ProductionDebugPanel").T("Inventory updated -> Give ").I(10).T(" Wood").Log();
            }
            if (GUILayout.Button("Give CopperOre x10"))
            {
                playerInventory.AddItem(ConfigIds.BaseItemDefinition.CopperOre, 10);
                FastString.Acquire().Tag("ProductionDebugPanel").T("Inventory updated -> Give ").I(10).T(" CopperOre").Log();
            }
            if (GUILayout.Button("Save"))
            {
                GM.Get<SaveManager>().ManualSave();
                FastString.Acquire().Tag("ProductionDebugPanel").T("Saved").Log();
            }
            GUILayout.EndHorizontal();
        }

        #endregion
    }
}