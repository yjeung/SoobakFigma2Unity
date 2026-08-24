using System.IO;
using System.Text;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Resolves the on-disk path for an extracted component prefab.
    ///
    /// Default path is "<output>/<sanitized name>.prefab". If a file already lives there,
    /// we read its FigmaPrefabManifest.RootComponentId and either:
    ///   - Reuse the same path (same componentId — this IS our component, just SmartMerge it).
    ///   - Append "_<componentId8>" suffix so we don't clobber an unrelated prefab that
    ///     happens to share the name (designer-made `Card.prefab` already at that path,
    ///     incoming Figma "Card" component has a different componentId).
    /// </summary>
    internal static class ComponentPrefabNamer
    {
        /// <summary>Resolve where to write (or merge into) the prefab for <paramref name="componentId"/>.</summary>
        public static string Resolve(string componentId, string componentName, string outputDir)
        {
            var sanitized = SanitizeFileName(componentName);
            var candidate = $"{outputDir}/{sanitized}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
            if (existing == null) return candidate;

            var manifest = existing.GetComponent<FigmaPrefabManifest>();
            if (manifest != null && manifest.RootComponentId == componentId)
                return candidate; // same component — re-import target is the same file.

            // Different componentId (or unmanaged designer prefab). Pick a non-clobbering name.
            var shortId = ShortenComponentId(componentId);
            return $"{outputDir}/{sanitized}_{shortId}.prefab";
        }

        /// <summary>
        /// Builds the stable prefab name for a COMPONENT_SET child.
        /// Every variant, including the first one, includes all of its property values:
        /// "Button" + "Style=Primary, State=Default" → "Button_Primary_Default".
        /// </summary>
        public static string BuildVariantPrefabName(string componentSetName, string variantName)
        {
            var baseName = SanitizeFileName(componentSetName);
            var variantValues = SanitizeFileName(variantName);
            return string.IsNullOrEmpty(variantValues)
                ? baseName
                : SanitizeFileName($"{baseName}_{variantValues}");
        }

        // Strip characters Unity / Windows reject in file names AND the Figma variant-
        // syntax characters that read as noise in a Unity Project window.
        //
        // Figma's variant component names look like "style=primary, size=XL, State=Default"
        // (key=value pairs, comma-separated). Saved as-is the prefab file ends up named
        // `style=primary, size=XL, State=Default.prefab` — visually unparseable in the
        // Project view and prone to collisions when only one property differs. We strip
        // any "<key>=" prefix from each comma-segment, leaving the values joined by
        // underscores: e.g. "primary_XL_Default". Designer-typed component names without
        // "=" pass through unchanged so we don't over-mangle conventional names.
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Component";

            // Variant-style flattening: "k=v, k=v" → "v_v".
            if (name.IndexOf('=') >= 0)
            {
                var parts = name.Split(',');
                var values = new System.Collections.Generic.List<string>(parts.Length);
                foreach (var part in parts)
                {
                    var eq = part.IndexOf('=');
                    var v = (eq >= 0 ? part.Substring(eq + 1) : part).Trim();
                    if (!string.IsNullOrEmpty(v)) values.Add(v);
                }
                if (values.Count > 0)
                    name = string.Join("_", values);
            }

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                bool ok = ch != '/' && ch != '\\' && ch != ':' && ch != '*' && ch != '?'
                          && ch != '"' && ch != '<' && ch != '>' && ch != '|'
                          && ch != '=' && ch != ',';
                sb.Append(ok ? ch : '_');
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "Component" : s;
        }

        // Figma componentIds look like "1234:5678". Hex-friendly compaction so the
        // suffix stays short but stable across re-imports.
        private static string ShortenComponentId(string componentId)
        {
            if (string.IsNullOrEmpty(componentId)) return "x";
            var hash = (uint)componentId.GetHashCode();
            return hash.ToString("x8");
        }
    }
}
