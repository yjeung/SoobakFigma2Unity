using System;
using System.Collections.Generic;
using System.Threading;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Window
{
    public sealed class SoobakFigma2UnityWindow : EditorWindow
    {
        [MenuItem("Window/SoobakFigma2Unity/Importer")]
        public static void ShowWindow()
        {
            var window = GetWindow<SoobakFigma2UnityWindow>();
            window.titleContent = new GUIContent("SoobakFigma2Unity");
            window.minSize = new Vector2(420, 600);
        }

        private ImportProfile _profile = new ImportProfile();
        private FrameTreeView _treeView = new FrameTreeView();
        private ImportLogger _logger = new ImportLogger();
        private CancellationTokenSource _cts;
        private bool _isFetching;
        private bool _isImporting;
        private Vector2 _mainScroll;
        private Vector2 _logScroll;
        private readonly Dictionary<string, string> _prefabPathByNodeId = new Dictionary<string, string>();

        private string _figmaUrl = "";
        private string _token = "";

        // Tracks paths touched by the most-recent successful import so the user can undo.
        private readonly List<string> _lastImportedPaths = new List<string>();
        private DateTime _lastImportAt = DateTime.MinValue;
        private const double UndoVisibilityMinutes = 30;

        private static readonly string[] ScaleLabels = { "1x", "2x", "3x", "4x" };
        private static readonly float[] ScaleValues = { 1f, 2f, 3f, 4f };
        private int _scaleIndex = 1;

        private static readonly string[] ColorSpaceLabels = { "Auto Detect", "Linear", "Gamma" };

        private void OnEnable()
        {
            _token = SoobakSettings.Token;
            _profile.PrefabOutputPath = SoobakSettings.PrefabPath;
            _profile.ScreenOutputPath = SoobakSettings.ScreenPath;
            _profile.ImageOutputPath = SoobakSettings.ImagePath;
            _profile.ImageScale = SoobakSettings.ImageScale;
            _scaleIndex = System.Array.IndexOf(ScaleValues, _profile.ImageScale);
            if (_scaleIndex < 0) _scaleIndex = 1;
            _logger.OnLogUpdated += Repaint;
        }

        private void OnDisable()
        {
            _logger.OnLogUpdated -= Repaint;
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private bool _showAdvanced;
        private bool _showLog;

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            // ── Primary (always visible) ──
            DrawConnectionSection();
            DrawFrameSelectionSection();
            DrawReimportSection();
            DrawImportButton();
            DrawUndoSection();

            // ── Advanced (collapsed by default) ──
            EditorGUILayout.Space(4);
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true, EditorStyles.foldoutHeader);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                DrawImageSection();
                DrawOutputSection();
                DrawLayoutSection();
                DrawColorSection();
                DrawPrefabSection();
                EditorGUI.indentLevel--;
            }

            // ── Log (collapsed by default) ──
            EditorGUILayout.Space(4);
            _showLog = EditorGUILayout.Foldout(_showLog, $"Log ({_logger.Entries.Count})", true, EditorStyles.foldoutHeader);
            if (_showLog)
                DrawLogSection();

            EditorGUILayout.EndScrollView();
        }

        // ─── Connection ─────────────────────────────────
        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("Figma", EditorStyles.boldLabel);

            _figmaUrl = EditorGUILayout.TextField(
                new GUIContent("File URL", "Paste a Figma file URL (or a node URL)."),
                _figmaUrl);

            EditorGUI.BeginChangeCheck();
            _token = EditorGUILayout.PasswordField(
                new GUIContent("Token", "Personal Access Token. Create one at figma.com > Settings > Account > Personal access tokens."),
                _token);
            if (EditorGUI.EndChangeCheck())
                SoobakSettings.Token = _token; // auto-save on change

            using (new EditorGUI.DisabledScope(_isFetching || _isImporting))
            {
                if (GUILayout.Button(_isFetching ? "Fetching…" : "📥 Fetch from Figma", GUILayout.Height(26)))
                    FetchFromApi();
            }

            EditorGUILayout.Space(8);
        }

        // ─── Frame Selection ────────────────────────────
        private void DrawFrameSelectionSection()
        {
            EditorGUILayout.LabelField("Frame Selection", EditorStyles.boldLabel);
            _treeView.OnGUI(180, GetImportedPrefabPath, OpenImportedPrefab);
            EditorGUILayout.Space(8);
        }

        // ─── Image ──────────────────────────────────────
        private void DrawImageSection()
        {
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _scaleIndex = EditorGUILayout.Popup("Scale", _scaleIndex, ScaleLabels);
            _profile.ImageScale = ScaleValues[_scaleIndex];
            _profile.AutoNineSlice = EditorGUILayout.Toggle("Auto 9-slice detection", _profile.AutoNineSlice);
            _profile.SolidColorOptimization = EditorGUILayout.Toggle(
                new GUIContent("Solid color → no image"), _profile.SolidColorOptimization);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Layout ─────────────────────────────────────
        private void DrawLayoutSection()
        {
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.ConvertAutoLayout = EditorGUILayout.Toggle("Convert Auto Layout → LayoutGroup", _profile.ConvertAutoLayout);
            _profile.FlattenEmptyGroups = EditorGUILayout.Toggle("Flatten empty groups", _profile.FlattenEmptyGroups);
            _profile.ApplyConstraints = EditorGUILayout.Toggle("Apply constraints → Anchors", _profile.ApplyConstraints);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Color ──────────────────────────────────────
        private void DrawColorSection()
        {
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.ColorSpace = (ColorSpaceMode)EditorGUILayout.Popup("Color Space", (int)_profile.ColorSpace, ColorSpaceLabels);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Prefab ─────────────────────────────────────
        private void DrawPrefabSection()
        {
            EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.GeneratePrefabVariants = EditorGUILayout.Toggle("Generate Prefab Variants", _profile.GeneratePrefabVariants);
            _profile.MapComponentInstances = EditorGUILayout.Toggle("Map Instances → Prefab Instances", _profile.MapComponentInstances);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Output ─────────────────────────────────────
        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.PrefabOutputPath = DrawPathField("Prefab Path", _profile.PrefabOutputPath);
            _profile.ScreenOutputPath = DrawPathField("Screen Path", _profile.ScreenOutputPath);
            _profile.ImageOutputPath = DrawPathField("Image Path", _profile.ImageOutputPath);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        private string DrawPathField(string label, string currentPath)
        {
            EditorGUILayout.BeginHorizontal();
            var path = EditorGUILayout.TextField(label, currentPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var selected = EditorUtility.OpenFolderPanel(label, currentPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                        path = "Assets" + selected.Substring(Application.dataPath.Length);
                    else _logger.Warn("Select a folder inside Assets.");
                }
            }
            EditorGUILayout.EndHorizontal();
            return path;
        }

        // ─── Re-import Settings ─────────────────────────
        private void DrawReimportSection()
        {
            EditorGUILayout.LabelField("Re-import", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var prevMode = _profile.MergeMode;
            _profile.MergeMode = (MergeMode)EditorGUILayout.EnumPopup(
                new GUIContent("Merge Mode",
                    "Smart Merge (default): preserves your Unity-side additions (components, scripts, child GameObjects).\n" +
                    "Full Replace: rewrites each prefab from scratch — all user edits are lost."),
                _profile.MergeMode);

            if (_profile.MergeMode == MergeMode.FullReplace && prevMode != MergeMode.FullReplace)
            {
                // Guard against accidental selection of the destructive mode.
                var ok = EditorUtility.DisplayDialog(
                    "Switch to Full Replace?",
                    "Full Replace overwrites each imported prefab, deleting any components, " +
                    "child GameObjects, or Inspector tweaks you've added on the Unity side.\n\n" +
                    "Continue?",
                    "Yes, Full Replace", "Cancel");
                if (!ok)
                    _profile.MergeMode = prevMode;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Import Button ──────────────────────────────
        private void DrawImportButton()
        {
            EditorGUILayout.Space(4);
            var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 32 };

            using (new EditorGUI.DisabledScope(_isFetching || _isImporting))
            {
                var label = _isImporting ? "Importing…" : "Import Selected";
                if (GUILayout.Button(label, style))
                    RunImport();
            }

            if (_isImporting && GUILayout.Button("Cancel"))
                _cts?.Cancel();

            EditorGUILayout.Space(8);
        }

        // ─── Undo Last Import ───────────────────────────
        private void DrawUndoSection()
        {
            bool hasRecent = _lastImportedPaths.Count > 0 &&
                             (DateTime.UtcNow - _lastImportAt).TotalMinutes <= UndoVisibilityMinutes;

            using (new EditorGUI.DisabledScope(!hasRecent || _isImporting || _isFetching))
            {
                var label = hasRecent
                    ? $"⏪ Undo Last Import ({_lastImportedPaths.Count} prefab, {(int)(DateTime.UtcNow - _lastImportAt).TotalMinutes}m ago)"
                    : "⏪ Undo Last Import";
                if (GUILayout.Button(label, GUILayout.Height(24)))
                    UndoLastImport();
            }
            EditorGUILayout.Space(8);
        }

        private void UndoLastImport()
        {
            int restored = 0, failed = 0;
            foreach (var path in _lastImportedPaths)
            {
                var snapshots = MergeBackup.ListSnapshots(path);
                if (snapshots == null || snapshots.Count == 0)
                {
                    _logger.Warn($"{path}: no backup to restore.");
                    failed++;
                    continue;
                }
                // Newest is the one created just before the last import.
                var newest = snapshots[0];
                if (MergeBackup.Restore(newest))
                {
                    restored++;
                    _logger.Info($"Restored: {path}");
                }
                else
                {
                    failed++;
                    _logger.Error($"Failed to restore: {path}");
                }
            }
            AssetDatabase.Refresh();
            _logger.Success($"Undo complete: {restored} restored, {failed} failed.");
            _lastImportedPaths.Clear();
            _lastImportAt = DateTime.MinValue;
            Repaint();
        }

        // ─── Log ────────────────────────────────────────
        private void DrawLogSection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(60))) _logger.Clear();
            EditorGUILayout.EndHorizontal();

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, EditorStyles.helpBox, GUILayout.Height(150));
            foreach (var entry in _logger.Entries)
            {
                var s = new GUIStyle(EditorStyles.label) { wordWrap = true };
                s.normal.textColor = entry.Level switch
                {
                    LogLevel.Error => new UnityEngine.Color(1f, 0.3f, 0.3f),
                    LogLevel.Warning => new UnityEngine.Color(1f, 0.8f, 0.2f),
                    LogLevel.Success => new UnityEngine.Color(0.3f, 0.9f, 0.3f),
                    _ => s.normal.textColor
                };
                EditorGUILayout.LabelField(entry.ToString(), s);
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Actions ────────────────────────────────────

        private void FetchFromApi()
        {
            var urlInfo = FigmaUrlParser.Parse(_figmaUrl);
            if (!urlInfo.IsValid) { _logger.Error("Invalid Figma URL."); return; }
            if (string.IsNullOrEmpty(_token)) { _logger.Error("Enter a Figma Personal Access Token."); return; }

            _isFetching = true;
            _logger.Info($"Fetching file (key: {urlInfo.FileKey})...");

            AsyncHelper.RunAsync(async () =>
            {
                using var api = new FigmaApiClient(_token);
                var file = await api.GetFileAsync(urlInfo.FileKey, depth: 2);
                AsyncHelper.RunOnMainThread(() =>
                {
                    _treeView.BuildFromDocument(file.Document);
                    RefreshImportedPrefabLinks();
                    _logger.Success($"Fetched: {file.Name} ({_treeView.Roots.Count} pages)");
                    _isFetching = false;
                    Repaint();
                });
            }, e => { _logger.Error($"Fetch failed: {e.Message}"); _isFetching = false; Repaint(); });
        }

        private void RunImport()
        {
            var urlInfo = FigmaUrlParser.Parse(_figmaUrl);
            if (!urlInfo.IsValid) { _logger.Error("Invalid Figma URL."); return; }

            var selectedIds = _treeView.GetSelectedNodeIds();
            if (selectedIds.Count == 0) { _logger.Error("No frames selected."); return; }

            SoobakSettings.Token = _token;
            SoobakSettings.PrefabPath = _profile.PrefabOutputPath;
            SoobakSettings.ScreenPath = _profile.ScreenOutputPath;
            SoobakSettings.ImagePath = _profile.ImageOutputPath;
            SoobakSettings.ImageScale = _profile.ImageScale;

            _isImporting = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _logger.Info($"Importing {selectedIds.Count} frame(s)...");
            AsyncHelper.RunAsync(async () =>
            {
                var pipeline = new ImportPipeline(_logger);
                await pipeline.RunFromApiAsync(_profile, _token, urlInfo.FileKey, selectedIds, _cts.Token);
                AsyncHelper.RunOnMainThread(() =>
                {
                    _lastImportedPaths.Clear();
                    foreach (var p in pipeline.SavedPrefabPaths) _lastImportedPaths.Add(p);
                    if (_lastImportedPaths.Count > 0) _lastImportAt = DateTime.UtcNow;
                    RefreshImportedPrefabLinks();
                    _isImporting = false;
                    Repaint();
                });
            }, e => { _logger.Error($"Import failed: {e.Message}"); _isImporting = false; Repaint(); });
        }

        private string GetImportedPrefabPath(string nodeId)
        {
            return !string.IsNullOrEmpty(nodeId) && _prefabPathByNodeId.TryGetValue(nodeId, out var path)
                ? path
                : null;
        }

        private void RefreshImportedPrefabLinks()
        {
            _prefabPathByNodeId.Clear();

            // Prefer the selected frame's screen prefab when the same Figma node also
            // produced an extracted component prefab.
            var folders = new List<string>();
            AddValidFolder(folders, _profile.ScreenOutputPath);
            AddValidFolder(folders, _profile.PrefabOutputPath);
            AddValidFolder(folders, _profile.ComponentOutputPath);
            if (folders.Count == 0)
                return;

            foreach (var folder in folders)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var manifest = prefab != null ? prefab.GetComponent<FigmaPrefabManifest>() : null;
                    var nodeId = manifest != null ? manifest.GetNodeId(prefab.transform) : null;
                    if (!string.IsNullOrEmpty(nodeId) && !_prefabPathByNodeId.ContainsKey(nodeId))
                        _prefabPathByNodeId[nodeId] = path;
                }
            }
        }

        private static void AddValidFolder(List<string> folders, string path)
        {
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                return;

            foreach (var folder in folders)
            {
                if (string.Equals(folder, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            folders.Add(path);
        }

        private void OpenImportedPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                _logger.Warn($"Prefab is not available yet: {path}");
                return;
            }

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            try
            {
                PrefabStageUtility.OpenPrefab(path);
            }
            catch (Exception e)
            {
                _logger.Warn($"Could not open Prefab Mode; opened in Project instead: {e.Message}");
                AssetDatabase.OpenAsset(prefab);
            }
        }
    }
}
