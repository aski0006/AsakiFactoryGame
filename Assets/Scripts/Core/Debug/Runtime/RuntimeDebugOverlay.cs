/*
 * RuntimeDebugOverlay
 *
 * 显示：FPS（瞬时/平均/最低）、内存(Allocated/Reserved/Mono)、活动 GameObject 数、场景数、分辨率、时间、GC 次数
 * 依赖：Define RUNTIME_DEBUG_OVERLAY 启用编译 (默认不启用)
 * 切换显示：F1 (可修改 HotKey)
 * 拖拽：按住标题区域左键拖动
 * 更新频率：
 *   - 文本刷新：每 frame
 *   - 活动对象统计：每 ActiveRefreshInterval 秒（避免过度开销）
 * 使用 FastStringBuilder 构建字符串，降低 GC
 *
 * 安全：
 *   - DontDestroyOnLoad
 *   - 若重复存在会保留首个，其余自动销毁
 *
 * 可通过代码：RuntimeDebugOverlay.EnsureExists();
 */


using Core.Debug.FastString;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.Debug.Runtime
{
    [DefaultExecutionOrder(-5000)]
    public class RuntimeDebugOverlay : MonoBehaviour
    {
        public static RuntimeDebugOverlay Instance { get; private set; }

        [Header("General")]
        [Tooltip("启动时是否显示")]
        public bool startVisible = true;

        [Tooltip("统计活动对象刷新间隔 (秒)")]
        public float ActiveRefreshInterval = 2f;

        [Tooltip("FPS 平滑采样数")]
        public int fpsSamples = 60;

        [Tooltip("文本缩放")]
        public float guiScale = 1f;

        [Tooltip("窗口初始位置")]
        public Vector2 startPosition = new(10, 10);

        [Tooltip("热键：切换显示 (KeyCode)")]
        public KeyCode toggleKey = KeyCode.F1;

        [Header("颜色")]
        public Color panelColor = new(.05f, .05f, .05f, .78f);
        public Color textColor = Color.white;
        public Color titleColor = new(.9f, .85f, .2f, 1f);
        public Color warnColor = new(1f, .6f, .2f, 1f);

        private bool _visible;
        private Rect _windowRect;
        private float _accumTime;
        private int _accumCount;
        private float _fpsCurrent;
        private float _fpsMin = float.MaxValue;
        private readonly Queue<float> _fpsQueue = new();
        private float _fpsAvg;

        private float _nextActiveScanTime;
        private int _activeGOCount;
        private int _activeComponentCount;

        private int _gcCountStart;
        private int _gcCountTotal;
        private int _gcLastCollectionCount;
        private float _gcCheckTimer;

        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

        private bool _dragging;
        private Vector2 _dragOffset;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[RuntimeDebugOverlay]");
            Instance = go.AddComponent<RuntimeDebugOverlay>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _visible = startVisible;
            _windowRect = new Rect(startPosition.x, startPosition.y, 380, 220);
            _gcCountStart = GC.CollectionCount(0);
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                _visible = !_visible;
            }

            UpdateFPS();
            UpdateGCInfo();
            if (Time.unscaledTime >= _nextActiveScanTime)
            {
                _nextActiveScanTime = Time.unscaledTime + ActiveRefreshInterval;
                ScanActiveObjects();
            }
        }

        private void UpdateFPS()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            _fpsCurrent = 1f / dt;
            _accumTime += dt;
            _accumCount++;

            if (_fpsCurrent < _fpsMin)
                _fpsMin = _fpsCurrent;

            _fpsQueue.Enqueue(_fpsCurrent);
            while (_fpsQueue.Count > Mathf.Max(5, fpsSamples))
                _fpsQueue.Dequeue();

            _fpsAvg = _fpsQueue.Count > 0 ? _fpsQueue.Average() : _fpsCurrent;
        }

        private void UpdateGCInfo()
        {
            _gcCheckTimer += Time.unscaledDeltaTime;
            if (_gcCheckTimer >= 0.5f)
            {
                _gcCheckTimer = 0f;
                // 只看第 0 代足够反映频繁分配
                int c0 = GC.CollectionCount(0);
                _gcCountTotal = c0 - _gcCountStart;
                _gcLastCollectionCount = c0;
            }
        }

        private void ScanActiveObjects()
        {
            int goCount = 0;
            int compCount = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    CountRecursive(roots[r], ref goCount, ref compCount);
                }
            }
            _activeGOCount = goCount;
            _activeComponentCount = compCount;
        }

        private static void CountRecursive(GameObject go, ref int goCount, ref int compCount)
        {
            if (!go.activeInHierarchy) return;
            goCount++;
            var comps = go.GetComponents<Component>();
            compCount += comps.Length;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                CountRecursive(t.GetChild(i).gameObject, ref goCount, ref compCount);
            }
        }

        private void InitStylesIfNeeded()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = true,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false
                };
            }
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;
            InitStylesIfNeeded();

            if (guiScale != 1f)
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
            }

            var prevColor = GUI.color;
            GUI.color = panelColor;
            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, GUIContent.none);
            GUI.color = prevColor;
        }

        private void DrawWindow(int id)
        {
            var titleRect = new Rect(0, 0, _windowRect.width, 22);
            HandleDrag(titleRect);

            using var fs = Game.Core.Debug.FastString.FastString.Acquire(512);

            BuildHeader(fs);
            BuildFPSLine(fs);
            BuildMemoryLines(fs);
            BuildActiveObjectsLine(fs);
            BuildMisc(fs);

            _labelStyle.normal.textColor = textColor;

            GUILayout.Space(4);
            GUILayout.Label(fs.ToStringAndRelease(), _labelStyle);

            var w = GUILayoutUtility.GetLastRect();
            _windowRect.height = Mathf.Clamp(w.yMax + 6, 120, 1000);

            DrawTitleBar(titleRect);

            // 右下角可缩放（简单：拖拽增加宽度高度）
            HandleResize();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));
        }

        private void BuildHeader(FastStringBuilder fs)
        {
            fs.Bold("<color=#FFD94A>Runtime Debug Overlay</color>").NL();
        }

        private void BuildFPSLine(FastStringBuilder fs)
        {
            fs.T("FPS: ");
            Color fpsColor = _fpsCurrent < 30 ? Color.red :
                             _fpsCurrent < 50 ? new Color(1f, .7f, .2f) :
                             Color.green;
            fs.Color(_fpsCurrent.ToString("F1"), fpsColor).SP();
            fs.T("[Avg=").F(_fpsAvg, 1).T(" Min=").F(Mathf.Approximately(_fpsMin, float.MaxValue) ? 0 : _fpsMin, 1).T("]").NL();
        }

        private void BuildMemoryLines(FastStringBuilder fs)
        {
            long mono = GC.GetTotalMemory(false);
#if UNITY_2018_4_OR_NEWER
            long alloc = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            long unused = UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong();
#else
            long alloc = 0;
            long reserved = 0;
            long unused = 0;
#endif
            fs.T("Mem: A=").T(FormatMB(alloc)).SP()
              .T("R=").T(FormatMB(reserved)).SP()
              .T("U=").T(FormatMB(unused)).SP()
              .T("Mono=").T(FormatMB(mono)).NL();
        }

        private void BuildActiveObjectsLine(FastStringBuilder fs)
        {
            fs.T("Active: GO=").I(_activeGOCount).SP()
              .T("Comp=").I(_activeComponentCount).SP()
              .T("Scenes=").I(SceneManager.sceneCount).NL();
        }

        private void BuildMisc(FastStringBuilder fs)
        {
            fs.T("Time: t=").F(Time.time, 1).SP()
              .T("Realtime=").F(Time.realtimeSinceStartup, 1).SP()
              .T("GC0=").I(_gcCountTotal).NL();

            fs.T("Screen: ").I(Screen.width).C('x').I(Screen.height).SP()
              .T("DPI=").F(Screen.dpi, 0).SP()
              .T("Scale=").F(guiScale, 2).NL();

#if UNITY_EDITOR
            fs.Color("Editor", new Color(.6f, .9f, 1f)).SP();
#else
            fs.Color("Player", new Color(.6f, 1f, .6f)).SP();
#endif
#if DEVELOPMENT_BUILD
            fs.Color("DevBuild", new Color(.9f, .5f, 1f)).SP();
#endif
#if ENABLE_IL2CPP
            fs.Color("IL2CPP", new Color(.9f, .7f, .3f)).SP();
#else
            fs.Color("Mono", new Color(.3f, .9f, .9f)).SP();
#endif
            fs.NL();
            fs.T("Toggle Key: ").T(toggleKey.ToString()).SP()
              .T("Drag: Title bar  Resize: Bottom-Right").NL();
        }

        private string FormatMB(long bytes)
        {
            return (bytes / (1024f * 1024f)).ToString("F1") + "MB";
        }

        private void DrawTitleBar(Rect r)
        {
            var prev = GUI.color;
            GUI.color = Color.clear;
            GUI.Box(r, GUIContent.none);
            GUI.color = prev;

            var titleRect = new Rect(r.x + 6, r.y + 2, r.width - 12, r.height - 4);
            _titleStyle.normal.textColor = titleColor;
            GUI.Label(titleRect, "<b>Runtime Debug Overlay</b>", _titleStyle);
        }

        private void HandleDrag(Rect titleRect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && titleRect.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragOffset = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _windowRect.position += e.delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                e.Use();
            }
        }

        private void HandleResize()
        {
            var e = Event.current;
            var gripRect = new Rect(_windowRect.width - 14, _windowRect.height - 14, 14, 14);
            EditorGUIUtility.AddCursorRect(gripRect, MouseCursor.ResizeUpLeft);

            if (e.type == EventType.MouseDown && gripRect.Contains(e.mousePosition))
            {
                _resizing = true;
                _resizeStartMouse = e.mousePosition;
                _resizeStartSize = new Vector2(_windowRect.width, _windowRect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _resizing)
            {
                var delta = e.mousePosition - _resizeStartMouse;
                _windowRect.width = Mathf.Clamp(_resizeStartSize.x + delta.x, 200, 1000);
                _windowRect.height = Mathf.Clamp(_resizeStartSize.y + delta.y, 120, 1000);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _resizing)
            {
                _resizing = false;
                e.Use();
            }

            // 画一个简单的三角
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, .3f);
            GUI.Label(new Rect(gripRect.x + 2, gripRect.y + 2, 12, 12), "◢");
            GUI.color = prev;
        }

        private bool _resizing;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
    }
}
