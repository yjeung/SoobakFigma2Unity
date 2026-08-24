using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Registry that maps FigmaNode types to their converters.
    /// </summary>
    internal sealed class NodeConverterRegistry
    {
        private readonly List<INodeConverter> _converters;

        public NodeConverterRegistry()
        {
            _converters = new List<INodeConverter>
            {
                new TextConverter(),
                new VectorConverter(),
                new RectangleConverter(),
                new InstanceConverter(),
                new FrameConverter(), // Must be last: it's the catch-all for FRAME/GROUP/COMPONENT/etc.
            };
        }

        public INodeConverter GetConverter(FigmaNode node)
        {
            foreach (var converter in _converters)
            {
                if (converter.CanConvert(node))
                    return converter;
            }
            return null;
        }

        /// <summary>
        /// Check if a node type should be skipped entirely (not converted).
        /// </summary>
        public static bool ShouldSkip(FigmaNode node)
        {
            if (!node.Visible)
                return true;

            var t = node.NodeType;
            return t == FigmaNodeType.SLICE ||
                   t == FigmaNodeType.STICKY ||
                   t == FigmaNodeType.CONNECTOR ||
                   t == FigmaNodeType.WASHI_TAPE ||
                   t == FigmaNodeType.SHAPE_WITH_TEXT ||
                   t == FigmaNodeType.UNKNOWN;
        }

        /// <summary>
        /// Check if this node type needs rasterization via Figma Images API.
        /// </summary>
        public static bool NeedsRasterization(FigmaNode node)
        {
            var t = node.NodeType;

            // Vector types always need rasterization
            if (t == FigmaNodeType.VECTOR ||
                t == FigmaNodeType.ELLIPSE ||
                t == FigmaNodeType.STAR ||
                t == FigmaNodeType.LINE ||
                t == FigmaNodeType.REGULAR_POLYGON ||
                t == FigmaNodeType.BOOLEAN_OPERATION)
                return true;

            // Containers with visible children should NEVER be rasterized as a whole.
            // Figma's /v1/images endpoint renders the full visual of the requested node —
            // for a FRAME / GROUP / COMPONENT / INSTANCE / SECTION that means every visible
            // child gets baked into the PNG. We then ALSO convert each child into a separate
            // GameObject, so the screen ends up with the entire frame painted as a flat
            // image AND the child structure layered on top — every text/icon/button rendered
            // twice, the prefab structure becomes meaningless because everything is hidden
            // behind the rasterized layer.
            //
            // The cost of this guard: a frame with a gradient or image background loses that
            // background (we can't separate it from the children in a single PNG). For UI
            // designs the children carry the actual content — backgrounds are usually a
            // solid colour anyway and SolidColorOptimizer handles those without raster.
            bool isContainer = t == FigmaNodeType.FRAME
                || t == FigmaNodeType.GROUP
                || t == FigmaNodeType.COMPONENT
                || t == FigmaNodeType.COMPONENT_SET
                || t == FigmaNodeType.INSTANCE
                || t == FigmaNodeType.SECTION
                || t == FigmaNodeType.TRANSFORM_GROUP;
            if (isContainer && node.HasChildren)
                return false;

            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (!fill.Visible || fill.Opacity <= 0f)
                        continue;

                    // Gradient fills need rasterization
                    if (fill.IsGradient)
                        return true;

                    // Image fills with FILL scaleMode (crop) need rasterization
                    // as the node itself to capture the correct crop
                    if (fill.IsImage && fill.ScaleMode == "FILL")
                        return true;
                }
            }

            // Nodes with visible effects (shadow, blur):
            // - For containers (FRAME/GROUP/COMPONENT/INSTANCE), do NOT rasterize the
            //   whole container, otherwise text and structured children get baked into
            //   the image. Effects on containers are visual approximations only —
            //   apply Unity Shadow/Outline components to descendants if needed.
            // - For visual-only types (RECTANGLE), still rasterize to capture the
            //   effect since they have no internal structure to preserve.
            if (node.Effects != null && t == FigmaNodeType.RECTANGLE)
            {
                foreach (var effect in node.Effects)
                {
                    if (effect.Visible)
                        return true;
                }
            }

            // Non-uniform corner radius on a filled frame needs rasterization
            if (node.RectangleCornerRadii != null && node.RectangleCornerRadii.Length == 4 &&
                node.HasVisibleFills)
            {
                var r = node.RectangleCornerRadii;
                if (r[0] != r[1] || r[1] != r[2] || r[2] != r[3])
                    return true;
            }

            // Uniform corner radius + visible fills on RECTANGLE → rasterize.
            // Unity Image cannot render rounded corners natively without a sprite asset,
            // so we bake the rounded shape into a PNG. NineSliceDetector then sets sprite
            // borders so the rounded corners scale correctly with RT size.
            // (Without this rule, solid-filled rounded rectangles like button backgrounds
            //  end up with no Image at all because SolidColorOptimizer rejects them.)
            if (t == FigmaNodeType.RECTANGLE && node.HasVisibleFills && node.CornerRadius > 0)
                return true;

            // Visible stroke (border) → rasterize. Unity Image cannot draw strokes
            // natively. Without this rule, nodes with strokes but no other rasterization
            // trigger (gradient/effect/cornerRadius/etc.) lose their borders entirely
            // (SolidColorOptimizer also rejects them, leaving no Image at all).
            if (node.StrokeWeight > 0 && node.Strokes != null)
            {
                foreach (var stroke in node.Strokes)
                {
                    if (stroke.Visible && stroke.Opacity > 0f)
                        return true;
                }
            }

            // Nodes with visible image fills → rasterize to capture correct crop/stretch
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage)
                        return true;
                }
            }

            // isMask FRAME nodes need their own rasterization to get an exact-size
            // mask shape sprite (matches the FRAME's RectTransform with clipsContent
            // applied). Using a child VECTOR's sprite would cause aspect-ratio
            // distortion since vector bbox often differs from the FRAME bbox.
            // (Vector-type isMask is already handled by the vector-types rule above.)
            if (node.IsMask &&
                (node.NodeType == FigmaNodeType.FRAME ||
                 node.NodeType == FigmaNodeType.GROUP ||
                 node.NodeType == FigmaNodeType.RECTANGLE))
                return true;

            // NOTE: We do NOT rasterize parents containing isMask children.
            // Instead, ImportPipeline.ConvertChildren restructures the hierarchy:
            // the isMask sibling becomes a Unity Mask wrapper, and subsequent siblings
            // are reparented under it. This preserves Figma structure for runtime
            // sprite swapping (e.g., speechbubble emoji variants).

            return false;
        }

        /// <summary>
        /// Check if a node is a mask shape container (has isMask child).
        /// Used by FrameConverter to apply Unity Mask component.
        /// </summary>
        public static bool IsMaskContainer(FigmaNode node)
        {
            if (node.Children == null) return false;
            foreach (var child in node.Children)
                if (child.IsMask) return true;
            return false;
        }
    }
}
