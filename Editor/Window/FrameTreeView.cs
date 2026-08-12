using System;
using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Window
{
    /// <summary>
    /// Data structure for the frame selection tree displayed in the Editor Window.
    /// </summary>
    internal sealed class FrameTreeItem
    {
        public string NodeId;
        public string Name;
        public string Type;
        public bool Selected;
        public bool Expanded = true;
        public List<FrameTreeItem> Children = new List<FrameTreeItem>();
        public int Depth;
    }

    internal sealed class FrameTreeView
    {
        private List<FrameTreeItem> _roots = new List<FrameTreeItem>();
        private Vector2 _scrollPosition;
        private string _searchQuery = string.Empty;

        public IReadOnlyList<FrameTreeItem> Roots => _roots;

        public void Clear()
        {
            _roots.Clear();
            _searchQuery = string.Empty;
        }

        /// <summary>
        /// Build tree from Figma document node (pages and their children).
        /// </summary>
        public void BuildFromDocument(FigmaNode document)
        {
            _roots.Clear();

            if (document?.Children == null)
                return;

            // Document children are pages (CANVAS)
            foreach (var page in document.Children)
            {
                if (page.NodeType != FigmaNodeType.CANVAS)
                    continue;

                var pageItem = new FrameTreeItem
                {
                    NodeId = page.Id,
                    Name = page.Name,
                    Type = "PAGE",
                    Selected = false,
                    Depth = 0
                };

                if (page.Children != null)
                {
                    foreach (var frame in page.Children)
                    {
                        // Show top-level frames, components, component sets
                        var t = frame.NodeType;
                        if (t == FigmaNodeType.FRAME || t == FigmaNodeType.COMPONENT ||
                            t == FigmaNodeType.COMPONENT_SET || t == FigmaNodeType.SECTION)
                        {
                            pageItem.Children.Add(new FrameTreeItem
                            {
                                NodeId = frame.Id,
                                Name = frame.Name,
                                Type = frame.Type,
                                Selected = false,
                                Depth = 1
                            });
                        }
                    }
                }

                _roots.Add(pageItem);
            }
        }

        /// <summary>
        /// Draw the tree with checkboxes in the Editor Window.
        /// </summary>
        public void OnGUI(float height)
        {
            DrawSearchField();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition,
                GUILayout.Height(height));

            foreach (var root in _roots)
            {
                if (MatchesSearch(root))
                    DrawItem(root);
            }

            if (_roots.Count > 0 && !HasSearchResults())
                EditorGUILayout.HelpBox($"No frames found for \"{_searchQuery}\".", MessageType.Info);

            GUILayout.EndScrollView();
        }

        private void DrawSearchField()
        {
            GUILayout.BeginHorizontal();
            _searchQuery = EditorGUILayout.TextField(
                new GUIContent("Search", "Filter pages and frames by name."),
                _searchQuery);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_searchQuery)))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(48f)))
                {
                    _searchQuery = string.Empty;
                    GUI.FocusControl(null);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawItem(FrameTreeItem item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(item.Depth * 20f);

            if (item.Children.Count > 0)
            {
                item.Expanded = UnityEditor.EditorGUILayout.Foldout(item.Expanded, "", true);
            }
            else
            {
                GUILayout.Space(16f);
            }

            bool wasSelected = item.Selected;
            item.Selected = GUILayout.Toggle(item.Selected, "", GUILayout.Width(16));

            // If page selection changed, propagate to children
            if (item.Selected != wasSelected && item.Children.Count > 0)
            {
                foreach (var child in item.Children)
                    child.Selected = item.Selected;
            }

            var label = item.Type == "PAGE"
                ? $"Page: {item.Name}"
                : $"{item.Name} [{item.Type}]";
            GUILayout.Label(label);

            GUILayout.EndHorizontal();

            if (item.Expanded || HasSearchQuery)
            {
                foreach (var child in item.Children)
                {
                    if (ShouldDrawChild(item, child))
                        DrawItem(child);
                }
            }
        }

        private bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);

        private bool MatchesName(FrameTreeItem item)
        {
            return !string.IsNullOrEmpty(item.Name) &&
                   item.Name.IndexOf(_searchQuery.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchesSearch(FrameTreeItem item)
        {
            if (!HasSearchQuery || MatchesName(item))
                return true;

            foreach (var child in item.Children)
            {
                if (MatchesSearch(child))
                    return true;
            }

            return false;
        }

        private bool ShouldDrawChild(FrameTreeItem parent, FrameTreeItem child)
        {
            return !HasSearchQuery || MatchesName(parent) || MatchesSearch(child);
        }

        private bool HasSearchResults()
        {
            if (!HasSearchQuery)
                return true;

            foreach (var root in _roots)
            {
                if (MatchesSearch(root))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get the Figma node IDs of all selected frames (not pages).
        /// </summary>
        public List<string> GetSelectedNodeIds()
        {
            var result = new List<string>();
            foreach (var root in _roots)
            {
                foreach (var child in root.Children)
                {
                    if (child.Selected)
                        result.Add(child.NodeId);
                }
            }
            return result;
        }
    }
}
