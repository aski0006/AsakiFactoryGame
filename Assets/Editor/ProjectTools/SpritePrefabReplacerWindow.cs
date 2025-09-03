/*
 * Sprite / Prefab 快速替换工具
 *
 * 修正说明 (2025-09-03):
 *  - 修复上一版粘贴截断导致的编译错误：
 *      * CopyMatchedComponents for(...) 循环被破坏
 *      * UpdateSceneReferences() 丢失
 *      * 引用 visited / changed / allComponents / ClearProgressBar 等符号缺失
 *  - 新增 Transform / Component GetHierarchyPath() 扩展
 *
 * 功能概述:
 *  1. Sprite 批量替换：基于当前 Selection（GameObject），支持 SpriteRenderer / UI.Image，递归，可 Dry Run
 *  2. Prefab 批量替换：替换场景对象为新 Prefab，保留 Transform / 可选属性（名称、Layer、Tag、Static、Active）
 *  3. 可选复制匹配类型组件数据 (EditorUtility.CopySerialized)
 *  4. 可选更新其他组件对旧实例（及其组件）的引用
 *  5. 支持 Prefab Variant（复制时包含其覆盖值）
 *  6. 日志使用 FastStringBuilder
 *
 * 注意:
 *  - Prefab 替换未整合 Undo（复杂度较高），请在版本管理下使用
 *  - 引用更新会遍历所有已加载场景组件，可能较慢
 *  - Dry Run 建议先预览
 */

using Core.Debug.FastString;
using Game.Core.Debug.FastString;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 若无 UGUI 可移除

namespace Editor.ProjectTools
{
    internal class SpritePrefabReplacerWindow : EditorWindow
    {
        private enum Tab
        {
            SpriteReplace = 0,
            PrefabReplace = 1,
        }

        private Tab _tab;

        #region Sprite Replace Data

        [Serializable]
        private class SpriteReplaceEntry
        {
            public Sprite oldSprite;
            public Sprite newSprite;
            public bool enabled = true;
            public string status;
        }

        private readonly List<SpriteReplaceEntry> _spriteEntries = new List<SpriteReplaceEntry>();
        private bool _spriteRecursive = true;
        private bool _spriteDryRun = true;
        private int _spriteFoundCount;
        private int _spriteWillReplaceCount;
        private Vector2 _spriteScroll;

        #endregion

        #region Prefab Replace Data

        private GameObject _prefabNew;
        private readonly List<GameObject> _prefabTargets = new List<GameObject>();
        private bool _prefabDryRun = true;
        private bool _prefabKeepName = true;
        private bool _prefabKeepLayer = true;
        private bool _prefabKeepTag = true;
        private bool _prefabKeepStatic = true;
        private bool _prefabKeepActive = true;
        private bool _prefabCopyMatchedComponents = true;
        private bool _prefabUpdateReferences = true;
        private bool _prefabLogEach;

        private int _prefabWillReplaceCount;
        private Vector2 _prefabScroll;

        #endregion

        private GUIStyle _headerStyle;
        private GUIStyle _miniStyle;

        [MenuItem("Tools/Project Tools/Sprite & Prefab Replacer")]
        private static void Open()
        {
            var win = GetWindow<SpritePrefabReplacerWindow>("Sprite & Prefab Replacer");
            win.minSize = new Vector2(820, 520);
            win.Show();
        }

        private void OnGUI()
        {
            InitStyles();
            DrawTabs();
            EditorGUILayout.Space(6);

            switch (_tab)
            {
                case Tab.SpriteReplace:
                    DrawSpriteReplaceGUI();
                    break;
                case Tab.PrefabReplace:
                    DrawPrefabReplaceGUI();
                    break;
            }
        }

        private void InitStyles()
        {
            _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _miniStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
        }

        private void DrawTabs()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Sprite 替换", "Prefab 替换" });
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh Selection", GUILayout.Width(140)))
                {
                    if (_tab == Tab.SpriteReplace)
                        CollectSpritesFromSelection();
                    else
                        CollectPrefabsFromSelection();
                }
            }
        }

        #region Sprite Replace GUI

        private void DrawSpriteReplaceGUI()
        {
            EditorGUILayout.LabelField("Sprite 批量替换", _headerStyle);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _spriteRecursive = EditorGUILayout.ToggleLeft("递归子层级", _spriteRecursive, GUILayout.Width(100));
                    _spriteDryRun = EditorGUILayout.ToggleLeft("Dry Run 预览", _spriteDryRun, GUILayout.Width(120));
                    if (GUILayout.Button("从当前 Selection 收集 Sprite", GUILayout.Width(220)))
                    {
                        CollectSpritesFromSelection();
                    }

                    if (GUILayout.Button("添加条目", GUILayout.Width(90)))
                    {
                        _spriteEntries.Add(new SpriteReplaceEntry());
                    }

                    if (GUILayout.Button("清空", GUILayout.Width(70)))
                    {
                        _spriteEntries.Clear();
                    }
                }

                EditorGUILayout.HelpBox("步骤:\n1. 收集或手动添加旧 Sprite\n2. 设置新 Sprite\n3. 执行（先 Dry Run）", MessageType.Info);
            }

            _spriteScroll = EditorGUILayout.BeginScrollView(_spriteScroll);
            for (int i = 0; i < _spriteEntries.Count; i++)
            {
                var e = _spriteEntries[i];
                using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                {
                    e.enabled = EditorGUILayout.Toggle(e.enabled, GUILayout.Width(20));
                    e.oldSprite = (Sprite)EditorGUILayout.ObjectField(e.oldSprite, typeof(Sprite), false, GUILayout.Width(170));
                    GUILayout.Label("=>", GUILayout.Width(24));
                    e.newSprite = (Sprite)EditorGUILayout.ObjectField(e.newSprite, typeof(Sprite), false, GUILayout.Width(170));
                    GUILayout.Label(string.IsNullOrEmpty(e.status) ? "" : e.status, _miniStyle, GUILayout.Width(120));
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        _spriteEntries.RemoveAt(i);
                        GUI.FocusControl(null);
                        break;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("执行 Sprite 替换", GUILayout.Height(28)))
                {
                    ExecuteSpriteReplace();
                }

                if (GUILayout.Button("清空状态", GUILayout.Height(28)))
                {
                    foreach (var e in _spriteEntries) e.status = "";
                    _spriteFoundCount = _spriteWillReplaceCount = 0;
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.HelpBox($"找到对象: {_spriteFoundCount}  |  将替换: {_spriteWillReplaceCount}  (DryRun={_spriteDryRun})", MessageType.None);
        }

        private void CollectSpritesFromSelection()
        {
            var selected = Selection.gameObjects;
            var set = new HashSet<Sprite>();
            foreach (var go in selected)
            {
                if (_spriteRecursive)
                {
                    foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                        if (sr.sprite != null)
                            set.Add(sr.sprite);

                    foreach (var img in go.GetComponentsInChildren<Image>(true))
                        if (img.sprite != null)
                            set.Add(img.sprite);
                }
                else
                {
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr?.sprite != null) set.Add(sr.sprite);
                    var img = go.GetComponent<Image>();
                    if (img?.sprite != null) set.Add(img.sprite);
                }
            }

            foreach (var s in set)
            {
                if (_spriteEntries.All(en => en.oldSprite != s))
                {
                    _spriteEntries.Add(new SpriteReplaceEntry
                    {
                        oldSprite = s,
                        newSprite = null,
                        enabled = true
                    });
                }
            }
        }

        private void ExecuteSpriteReplace()
        {
            _spriteFoundCount = 0;
            _spriteWillReplaceCount = 0;

            var map = new Dictionary<Sprite, Sprite>();
            foreach (var e in _spriteEntries)
            {
                if (!e.enabled || e.oldSprite == null)
                {
                    e.status = "Skip";
                    continue;
                }
                if (e.newSprite == null)
                {
                    e.status = "NewMissing";
                    continue;
                }
                if (e.oldSprite == e.newSprite)
                {
                    e.status = "Same";
                    continue;
                }
                map[e.oldSprite] = e.newSprite;
                e.status = "Pending";
            }

            if (map.Count == 0)
            {
                Debug.LogWarning("[SpritePrefabReplacer] 没有可替换的映射。");
                return;
            }

            var targets = Selection.gameObjects;
            if (targets == null || targets.Length == 0)
            {
                Debug.LogWarning("[SpritePrefabReplacer] 没有选中的 GameObject。");
                return;
            }

            using var fs = FastString.Acquire().Tag().T("SpriteReplace Start").NL();

            foreach (var root in targets)
            {
                IEnumerable<Transform> transforms = _spriteRecursive
                    ? root.GetComponentsInChildren<Transform>(true)
                    : new[] { root.transform };

                foreach (var t in transforms)
                {
                    var sr = t.GetComponent<SpriteRenderer>();
                    if (sr && sr.sprite != null && map.TryGetValue(sr.sprite, out var newSprite))
                    {
                        _spriteFoundCount++;
                        if (_spriteDryRun)
                        {
                            _spriteWillReplaceCount++;
                            fs.T("DRY: ").Obj(t.GetHierarchyPath()).T("  ").Obj(sr.sprite.name).T(" -> ").Obj(newSprite.name).NL();
                        }
                        else
                        {
                            Undo.RecordObject(sr, "Sprite Replace");
                            var oldName = sr.sprite.name;
                            sr.sprite = newSprite;
                            EditorUtility.SetDirty(sr);
                            _spriteWillReplaceCount++;
                            fs.T("DO:  ").Obj(t.GetHierarchyPath()).T("  ").Obj(oldName).T(" -> ").Obj(newSprite.name).NL();
                        }
                    }

                    var img = t.GetComponent<Image>();
                    if (img && img.sprite != null && map.TryGetValue(img.sprite, out var newSprite2))
                    {
                        _spriteFoundCount++;
                        if (_spriteDryRun)
                        {
                            _spriteWillReplaceCount++;
                            fs.T("DRY(UI): ").Obj(t.GetHierarchyPath()).T("  ").Obj(img.sprite.name).T(" -> ").Obj(newSprite2.name).NL();
                        }
                        else
                        {
                            Undo.RecordObject(img, "Sprite Replace");
                            var oldName = img.sprite.name;
                            img.sprite = newSprite2;
                            EditorUtility.SetDirty(img);
                            _spriteWillReplaceCount++;
                            fs.T("DO(UI):  ").Obj(t.GetHierarchyPath()).T("  ").Obj(oldName).T(" -> ").Obj(newSprite2.name).NL();
                        }
                    }
                }
            }

            foreach (var e in _spriteEntries)
            {
                if (e.status == "Pending")
                    e.status = _spriteDryRun ? "Previewed" : "Done";
            }

            fs.NL().T("Summary: Found=").I(_spriteFoundCount).SP()
              .T("Replace=").I(_spriteWillReplaceCount).SP()
              .T("Dry=").B(_spriteDryRun)
              .Log();

            if (!_spriteDryRun)
            {
                AssetDatabase.SaveAssets();
            }
        }

        #endregion

        #region Prefab Replace GUI

        private void DrawPrefabReplaceGUI()
        {
            EditorGUILayout.LabelField("Prefab 批量替换", _headerStyle);
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                _prefabNew = (GameObject)EditorGUILayout.ObjectField(new GUIContent("新 Prefab", "目标替换的 Prefab 资源"), _prefabNew, typeof(GameObject), false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _prefabDryRun = EditorGUILayout.ToggleLeft("Dry Run 预览", _prefabDryRun, GUILayout.Width(120));
                    _prefabKeepName = EditorGUILayout.ToggleLeft("保留名称", _prefabKeepName, GUILayout.Width(90));
                    _prefabKeepLayer = EditorGUILayout.ToggleLeft("保留 Layer", _prefabKeepLayer, GUILayout.Width(100));
                    _prefabKeepTag = EditorGUILayout.ToggleLeft("保留 Tag", _prefabKeepTag, GUILayout.Width(90));
                    _prefabKeepStatic = EditorGUILayout.ToggleLeft("保留 Static 标记", _prefabKeepStatic, GUILayout.Width(120));
                    _prefabKeepActive = EditorGUILayout.ToggleLeft("保留 Active", _prefabKeepActive, GUILayout.Width(100));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _prefabCopyMatchedComponents = EditorGUILayout.ToggleLeft("复制匹配组件数据", _prefabCopyMatchedComponents, GUILayout.Width(140));
                    _prefabUpdateReferences = EditorGUILayout.ToggleLeft("更新场景引用(慢)", _prefabUpdateReferences, GUILayout.Width(160));
                    _prefabLogEach = EditorGUILayout.ToggleLeft("逐项日志", _prefabLogEach, GUILayout.Width(100));
                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("从当前 Selection 收集待替换对象", GUILayout.Width(240)))
                    {
                        CollectPrefabsFromSelection();
                    }
                    if (GUILayout.Button("清空列表", GUILayout.Width(100)))
                    {
                        _prefabTargets.Clear();
                    }
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.HelpBox("选择场景中的对象 -> 设定新 Prefab -> Dry Run -> 实际执行。\nPrefab Variant 保留其覆盖值（复制组件时）。",
                    MessageType.Info);
            }

            _prefabScroll = EditorGUILayout.BeginScrollView(_prefabScroll);
            for (int i = 0; i < _prefabTargets.Count; i++)
            {
                var go = _prefabTargets[i];
                if (go == null) continue;
                using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                {
                    EditorGUILayout.ObjectField(go, typeof(GameObject), true);
                    GUILayout.Label(go.GetHierarchyPath(), _miniStyle);
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        _prefabTargets.RemoveAt(i);
                        GUI.FocusControl(null);
                        break;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("执行 Prefab 替换", GUILayout.Height(30)))
                {
                    ExecutePrefabReplace();
                }
                if (GUILayout.Button("刷新 Selection", GUILayout.Height(30), GUILayout.Width(140)))
                {
                    CollectPrefabsFromSelection();
                }
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.HelpBox($"将替换对象数量: {_prefabWillReplaceCount}  (DryRun={_prefabDryRun})", MessageType.None);
        }

        private void CollectPrefabsFromSelection()
        {
            _prefabTargets.Clear();
            foreach (var go in Selection.gameObjects)
            {
                if (go.scene.IsValid())
                    _prefabTargets.Add(go);
            }
            _prefabWillReplaceCount = _prefabTargets.Count;
        }

        private void ExecutePrefabReplace()
        {
            if (_prefabNew == null)
            {
                Debug.LogWarning("[SpritePrefabReplacer] 未指定新的 Prefab。");
                return;
            }
            if (_prefabTargets.Count == 0)
            {
                Debug.LogWarning("[SpritePrefabReplacer] 没有待替换对象。");
                return;
            }

            using var fs = FastString.Acquire().Tag().T("PrefabReplace Start").NL()
                .T("Targets=").I(_prefabTargets.Count).SP()
                .T("Dry=").B(_prefabDryRun).NL();

            var mappingOldToNew = new Dictionary<GameObject, GameObject>();
            int done = 0;

            if (!_prefabDryRun)
            {
                int idx = 0;
                foreach (var oldObj in _prefabTargets)
                {
                    idx++;
                    if (oldObj == null) continue;

                    var parent = oldObj.transform.parent;
                    int sibling = oldObj.transform.GetSiblingIndex();

                    GameObject newInstance = (GameObject)PrefabUtility.InstantiatePrefab(_prefabNew, oldObj.scene);
                    if (newInstance == null)
                    {
                        fs.ErrTag().T("InstantiatePrefab 失败: ").Obj(_prefabNew.name).NL();
                        continue;
                    }

                    newInstance.transform.SetParent(parent, false);
                    newInstance.transform.SetSiblingIndex(sibling);

                    CopyTransform(oldObj.transform, newInstance.transform);

                    if (_prefabKeepName) newInstance.name = oldObj.name;
                    if (_prefabKeepLayer) newInstance.layer = oldObj.layer;
                    if (_prefabKeepTag) newInstance.tag = oldObj.tag;
                    if (_prefabKeepStatic) GameObjectUtility.SetStaticEditorFlags(newInstance,
                        GameObjectUtility.GetStaticEditorFlags(oldObj));
                    if (_prefabKeepActive) newInstance.SetActive(oldObj.activeSelf);

                    if (_prefabCopyMatchedComponents)
                        CopyMatchedComponents(oldObj, newInstance, _prefabLogEach, fs);

                    mappingOldToNew[oldObj] = newInstance;

                    if (_prefabLogEach)
                        fs.T("Replaced: ").Obj(oldObj.GetHierarchyPath()).T(" -> ").Obj(newInstance.name).NL();

                    oldObj.SetActive(false); // 暂时隐藏旧对象
                    done++;

                    EditorUtility.DisplayProgressBar("Prefab Replace",
                        $"Processing {idx}/{_prefabTargets.Count}", (float)idx / _prefabTargets.Count);
                }

                EditorUtility.ClearProgressBar();

                if (_prefabUpdateReferences && mappingOldToNew.Count > 0)
                {
                    UpdateSceneReferences(mappingOldToNew, fs);
                }

                foreach (var kv in mappingOldToNew)
                {
                    if (kv.Key != null)
                        UnityEngine.Object.DestroyImmediate(kv.Key);
                }

                AssetDatabase.SaveAssets();
            }
            else
            {
                foreach (var oldObj in _prefabTargets)
                {
                    fs.T("DRY Replace: ").Obj(oldObj?.GetHierarchyPath()).T(" -> ").Obj(_prefabNew.name).NL();
                }
                done = _prefabTargets.Count;
            }

            _prefabWillReplaceCount = done;

            fs.NL().T("Summary: Done=").I(done).SP()
              .T("Dry=").B(_prefabDryRun).SP()
              .T("CopyComp=").B(_prefabCopyMatchedComponents).SP()
              .T("UpdRef=").B(_prefabUpdateReferences)
              .Log();
        }

        private static void CopyTransform(Transform src, Transform dst)
        {
            dst.localPosition = src.localPosition;
            dst.localRotation = src.localRotation;
            dst.localScale = src.localScale;
        }

        private static void CopyMatchedComponents(GameObject src, GameObject dst, bool logEach, FastStringBuilder fs)
        {
            var srcComps = src.GetComponents<Component>();
            var dstComps = dst.GetComponents<Component>();

            foreach (Component sc in srcComps)
            {
                if (sc == null || sc is Transform) continue;

                Component targetComp = null;
                foreach (Component dc in dstComps)
                {
                    if (dc == null || dc is Transform) continue;
                    if (dc.GetType() == sc.GetType())
                    {
                        targetComp = dc;
                        break;
                    }
                }

                if (targetComp == null)
                {
                    if (logEach) fs.T("SkipComp(NoMatchType): ").Obj(sc.GetType().Name).NL();
                    continue;
                }

                try
                {
                    EditorUtility.CopySerialized(sc, targetComp);
                    if (logEach) fs.T("CopyComp: ").Obj(sc.GetType().Name).NL();
                }
                catch (Exception ex)
                {
                    fs.WarnTag().T("CopyCompFail: ").Obj(sc.GetType().Name).T(" => ").Obj(ex.Message).NL();
                }
            }
        }

        private static void UpdateSceneReferences(Dictionary<GameObject, GameObject> remap, FastStringBuilder fs)
        {
            var allRoots = GetAllSceneRoots();
            var allComponents = new List<Component>(1024);
            foreach (var r in allRoots)
            {
                r.GetComponentsInChildren(true, allComponents);
            }

            int changed = 0;
            int visited = 0;

            fs.T("Updating References ...").NL();
            try
            {
                for (int i = 0; i < allComponents.Count; i++)
                {
                    var comp = allComponents[i];
                    if (comp == null) continue;

                    var so = new SerializedObject(comp);
                    var prop = so.GetIterator();
                    bool enterChildren = true;
                    bool modified = false;

                    while (prop.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var objRef = prop.objectReferenceValue;

                            if (objRef is GameObject goRef && remap.TryGetValue(goRef, out var newGo))
                            {
                                prop.objectReferenceValue = newGo;
                                modified = true;
                                changed++;
                            }
                            else if (objRef is Component compRef &&
                                     compRef != null &&
                                     remap.TryGetValue(compRef.gameObject, out var remapGo))
                            {
                                var newComp = remapGo.GetComponent(compRef.GetType());
                                if (newComp != null)
                                {
                                    prop.objectReferenceValue = newComp;
                                    modified = true;
                                    changed++;
                                }
                            }
                        }
                    }

                    if (modified)
                    {
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(comp);
                    }

                    visited++;
                    if ((i & 0x3F) == 0)
                    {
                        EditorUtility.DisplayProgressBar("Update References",
                            $"Component {i}/{allComponents.Count}",
                            (float)i / allComponents.Count);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            fs.T("Reference Updated: Visited=").I(visited).SP().T("Changed=").I(changed).NL();
        }

        private static List<GameObject> GetAllSceneRoots()
        {
            var list = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                list.AddRange(scene.GetRootGameObjects());
            }
            return list;
        }

        #endregion
    }

    internal static class SpritePrefabReplacerExtensions
    {
        public static string GetHierarchyPath(this GameObject go)
        {
            if (go == null) return "<null>";
            return go.transform.GetHierarchyPath();
        }

        public static string GetHierarchyPath(this Transform t)
        {
            if (t == null) return "<null>";
            using var fs = FastString.Acquire(128);
            var stack = new Stack<string>();
            var cur = t;
            while (cur != null)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }
            bool first = true;
            while (stack.Count > 0)
            {
                if (!first) fs.C('/');
                fs.T(stack.Pop());
                first = false;
            }
            return fs.ToStringAndRelease();
        }

        public static string GetHierarchyPath(this Component comp)
        {
            if (comp == null) return "<null>";
            return comp.transform.GetHierarchyPath();
        }
    }
}