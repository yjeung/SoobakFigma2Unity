using System;
using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    /// <summary>
    /// Links Figma INSTANCE nodes to existing Unity Prefabs,
    /// creating proper PrefabInstances with overrides instead of inline copies.
    /// </summary>
    internal sealed class PrefabInstanceLinker
    {
        private readonly ImportLogger _logger;

        public PrefabInstanceLinker(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Try to instantiate a prefab for the given INSTANCE node.
        /// Returns the instantiated GameObject if a matching prefab was found, null otherwise.
        /// </summary>
        public GameObject TryCreatePrefabInstance(FigmaNode instanceNode, GameObject parent, ImportContext ctx, string componentIdOverride = null)
        {
            // For an INSTANCE node the componentId is on the node itself; for a COMPONENT node
            // (a variant of a COMPONENT_SET that we've extracted as its own prefab), the caller
            // passes the COMPONENT's own Id as the override since that's what's keyed in
            // ctx.GeneratedPrefabs.
            var componentId = !string.IsNullOrEmpty(componentIdOverride)
                ? componentIdOverride
                : instanceNode.ComponentId;
            if (string.IsNullOrEmpty(componentId))
                return null;

            // Prefer the exact component-id mapping generated during this import. Components
            // from another selection/import are not present in that map, so fall back to the
            // stable prefab filename derived from Figma's component metadata.
            if (!ctx.GeneratedPrefabs.TryGetValue(componentId, out var prefabPath) &&
                !TryFindPrefabByName(instanceNode, componentId, ctx, out prefabPath))
            {
                return null;
            }

            // Cache a successful name lookup so every later instance of the same component
            // uses the same asset without rescanning the Project window.
            ctx.GeneratedPrefabs[componentId] = prefabPath;

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                _logger.Warn($"{instanceNode.Name}: prefab not found at {prefabPath}");
                return null;
            }

            // Instantiate as prefab instance
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            instance.name = instanceNode.Name;

            if (parent != null)
                instance.transform.SetParent(parent.transform, false);

            // Apply instance overrides from Figma
            ApplyInstanceOverrides(instance, instanceNode, ctx);

            _logger.Info($"{instanceNode.Name}: linked to prefab '{prefabAsset.name}'");
            return instance;
        }

        private bool TryFindPrefabByName(
            FigmaNode instanceNode,
            string componentId,
            ImportContext ctx,
            out string prefabPath)
        {
            prefabPath = null;
            if (ctx.Profile == null || string.IsNullOrWhiteSpace(ctx.Profile.ComponentOutputPath))
                return false;

            var candidates = new List<string>();
            if (ctx.Components.TryGetValue(componentId, out var component))
            {
                if (!string.IsNullOrEmpty(component.ComponentSetId) &&
                    ctx.ComponentSets.TryGetValue(component.ComponentSetId, out var componentSet))
                {
                    AddCandidate(candidates, ComponentPrefabNamer.BuildVariantPrefabName(
                        componentSet.Name,
                        component.Name));
                }

                AddCandidate(candidates, ComponentPrefabNamer.SanitizeFileName(component.Name));
            }

            AddCandidate(candidates, ComponentPrefabNamer.SanitizeFileName(instanceNode.Name));

            var guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { ctx.Profile.ComponentOutputPath });

            // Candidate order matters: the component-set + full property name is more
            // specific than the raw component name, and both are safer than an instance's
            // locally overridden layer name.
            foreach (var candidate in candidates)
            {
                var matches = new List<string>();
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        candidate,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(path);
                    }
                }

                if (matches.Count == 1)
                {
                    prefabPath = matches[0];
                    _logger.Info($"{instanceNode.Name}: matched prefab by name '{candidate}'");
                    return true;
                }

                if (matches.Count > 1)
                {
                    _logger.Warn(
                        $"{instanceNode.Name}: multiple prefabs named '{candidate}' found under " +
                        $"{ctx.Profile.ComponentOutputPath}; component was not linked.");
                    return false;
                }
            }

            return false;
        }

        private static void AddCandidate(List<string> candidates, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            foreach (var existing in candidates)
            {
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            candidates.Add(candidate);
        }

        /// <summary>
        /// Apply Figma instance overrides to the prefab instance.
        /// This handles property changes that differ from the base component.
        /// </summary>
        private void ApplyInstanceOverrides(GameObject instance, FigmaNode instanceNode, ImportContext ctx)
        {
            // Size override
            var rt = instance.GetComponent<RectTransform>();
            if (rt != null && instanceNode.AbsoluteBoundingBox != null)
            {
                var size = new Vector2(instanceNode.AbsoluteBoundingBox.Width, instanceNode.AbsoluteBoundingBox.Height);
                rt.sizeDelta = size;
            }

            // Opacity override
            if (instanceNode.Opacity < 1f)
            {
                var cg = instance.GetComponent<CanvasGroup>();
                if (cg == null) cg = instance.AddComponent<CanvasGroup>();
                cg.alpha = instanceNode.Opacity;
            }

            // Fill color overrides on root — only when the Image is a pure solid colour
            // (no sprite). When the Image carries a baked sprite, that PNG already encodes
            // the correct visual including its colour; assigning the Figma fill colour here
            // would tint the sprite (e.g. an ivory rounded-rect sprite turned brown by the
            // master's brown fill colour landing on top).
            if (instanceNode.HasVisibleFills)
            {
                var image = instance.GetComponent<Image>();
                if (image != null && image.sprite == null && instanceNode.Fills != null)
                {
                    foreach (var fill in instanceNode.Fills)
                    {
                        if (fill.Visible && fill.IsSolid && fill.Color != null)
                        {
                            image.color = ColorSpaceHelper.Convert(fill.Color, instanceNode.Opacity * fill.Opacity);
                            break;
                        }
                    }
                }
            }

            // Recursively apply child overrides by matching names. Pass the INSTANCE's
            // componentProperties down so child componentPropertyReferences can resolve
            // (BOOLEAN → SetActive, TEXT → TMP.text). PrefabUtility.RecordPrefabInstance...
            // is invoked at the end so Unity registers the changes as proper overrides
            // on the PrefabInstance rather than leaving them as un-tracked edits.
            //
            // Pass the prefab-instance ROOT's manifest so the recursion can resolve children
            // by figmaNodeId — Figma freely allows duplicate sibling names (a button with
            // both a left "icon" and a right "icon"), and Transform.Find(name) collapses
            // both onto the first match, mis-applying the second sibling's overrides
            // (e.g. SetActive(false)) onto the first.
            var rootManifest = instance.GetComponent<FigmaPrefabManifest>();
            if (instanceNode.Children != null)
            {
                ApplyChildOverrides(instance, instanceNode, ctx, instanceNode.ComponentProperties, rootManifest);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        }

        private void ApplyChildOverrides(
            GameObject parentGo, FigmaNode parentNode, ImportContext ctx,
            Dictionary<string, FigmaComponentPropertyValue> ownerProperties,
            FigmaPrefabManifest rootManifest)
        {
            if (parentNode.Children == null) return;

            foreach (var childNode in parentNode.Children)
            {
                // Resolve the matching GO via figmaNodeId. Sibling name duplicates (e.g. "icon"
                // left + "icon" right) make a name lookup unsafe — Transform.Find returns the
                // first match for both, so the second sibling's overrides land on the first GO.
                // When the manifest doesn't track this Figma child it usually means we filtered
                // it out at convert time (visible=False, etc.); applying its override to the
                // wrong GO would actively break the instance. Fall back to name lookup ONLY
                // when there's no manifest at all (legacy prefabs).
                //
                // Compound-id strip: a Figma INSTANCE on the screen resolves its children with
                // compound ids like "I9160:147973;7708:32996" (or deeper "I<a>;I<b>;<masterId>"
                // for nested instances). The MASTER's prefab manifest stores the BARE master
                // child id (7708:32996), so a direct lookup of the compound form never matches
                // and every per-instance override silently skips — the symptom is "every
                // PrefabInstance shows the master's default values, all looking identical".
                // Strip everything up to and including the last ';' before lookup. Bare ids
                // pass through unchanged so screen-side variants linked directly to extracted
                // prefabs keep matching.
                Transform childTransform = null;
                if (rootManifest != null)
                {
                    var lookupId = StripCompoundInstanceIdPrefix(childNode.Id);
                    if (!string.IsNullOrEmpty(lookupId))
                        childTransform = rootManifest.FindByNodeId(lookupId);
                    if (childTransform == null) continue; // tracked tree, not in manifest → skip
                }
                else
                {
                    childTransform = parentGo.transform.Find(childNode.Name);
                    if (childTransform == null) continue;
                }

                var childGo = childTransform.gameObject;

                // 1) componentProperty references — Figma's "Show Icon", "Label", etc.
                //    The CHILD references a property on the OWNING INSTANCE; that's why
                //    we pass ownerProperties from the outermost INSTANCE downward.
                ApplyComponentPropertyReferences(childGo, childNode, ownerProperties);

                // 2) Direct text overrides on TEXT nodes (when not driven by a TEXT property)
                if (childNode.NodeType == FigmaNodeType.TEXT)
                {
                    var tmp = childGo.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null && !string.IsNullOrEmpty(childNode.Characters))
                    {
                        tmp.text = childNode.Characters;

                        // Color override
                        if (childNode.Fills != null)
                        {
                            foreach (var fill in childNode.Fills)
                            {
                                if (fill.Visible && fill.IsSolid && fill.Color != null)
                                {
                                    tmp.color = ColorSpaceHelper.Convert(fill.Color, childNode.Opacity * fill.Opacity);
                                    break;
                                }
                            }
                        }
                    }
                }

                // 3) Image/fill overrides — solid-colour overrides only when the child has
                // no sprite (a baked sprite already encodes the colour; tinting it with
                // the Figma fill colour produces washed-out / wrong-tinted visuals like the
                // ivory rounded-rect sprite turning brown). Image-fill overrides still apply
                // because they replace the sprite outright.
                var image = childGo.GetComponent<Image>();
                if (image != null && childNode.HasVisibleFills)
                {
                    foreach (var fill in childNode.Fills)
                    {
                        if (fill.Visible && fill.IsSolid && fill.Color != null)
                        {
                            if (image.sprite == null)
                                image.color = ColorSpaceHelper.Convert(fill.Color, childNode.Opacity * fill.Opacity);
                            break;
                        }

                        if (fill.Visible && fill.IsImage && fill.ImageRef != null &&
                            ctx.FillSprites.TryGetValue(fill.ImageRef, out var sprite))
                        {
                            image.sprite = sprite;
                            break;
                        }
                    }
                }

                // 4) Plain visibility (Figma's hide-this-layer toggle, distinct from a BOOLEAN
                //    component property). A BOOLEAN already set this above; if both apply, the
                //    later "false" wins which matches Figma's "false hides regardless" rule.
                if (!childNode.Visible)
                    childGo.SetActive(false);

                // 5) Recurse — but skip stepping into nested INSTANCE children. Those are
                //    their own PrefabInstances (linked in their own InstanceConverter pass)
                //    and shouldn't have the outer instance's overrides bled into them.
                if (childNode.NodeType != FigmaNodeType.INSTANCE && childNode.Children != null)
                    ApplyChildOverrides(childGo, childNode, ctx, ownerProperties, rootManifest);
            }
        }

        private static void ApplyComponentPropertyReferences(
            GameObject childGo, FigmaNode childNode,
            Dictionary<string, FigmaComponentPropertyValue> ownerProperties)
        {
            if (ownerProperties == null) return;
            var refs = childNode.ComponentPropertyReferences;
            if (refs == null || refs.Count == 0) return;

            foreach (var pair in refs)
            {
                // pair.Key is the local property the child wants overridden ("visible",
                // "characters", "mainComponent"). pair.Value is the "Name#Id" key into
                // the owning INSTANCE's componentProperties dictionary.
                if (!ownerProperties.TryGetValue(pair.Value, out var propValue) || propValue == null)
                    continue;

                switch (pair.Key)
                {
                    case "visible":
                        if (propValue.Type == "BOOLEAN")
                            childGo.SetActive(propValue.AsBool());
                        break;
                    case "characters":
                        if (propValue.Type == "TEXT")
                        {
                            var tmp = childGo.GetComponent<TMPro.TextMeshProUGUI>();
                            var text = propValue.AsString();
                            if (tmp != null && text != null)
                                tmp.text = text;
                        }
                        break;
                    // case "mainComponent": INSTANCE_SWAP — v2; for now the prefab keeps
                    // whatever component the master extraction baked in.
                }
            }
        }

        // Drops the "I<instanceId>;...;" prefix that Figma adds to children of a resolved
        // INSTANCE so the bare master child id can match the prefab manifest entry. Bare
        // ids (no ';') pass through unchanged.
        private static string StripCompoundInstanceIdPrefix(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return nodeId;
            int lastSep = nodeId.LastIndexOf(';');
            return lastSep < 0 ? nodeId : nodeId.Substring(lastSep + 1);
        }
    }
}
