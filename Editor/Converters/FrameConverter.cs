using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Layout;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts FRAME and GROUP nodes to GameObjects with RectTransform.
    /// Adds Image component if the frame has fills.
    /// Adds LayoutGroup if the frame uses auto-layout.
    /// Adds Mask if clipsContent is true.
    /// </summary>
    internal sealed class FrameConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node)
        {
            var t = node.NodeType;
            return t == FigmaNodeType.FRAME || t == FigmaNodeType.GROUP ||
                   t == FigmaNodeType.COMPONENT || t == FigmaNodeType.COMPONENT_SET ||
                   t == FigmaNodeType.SECTION;
        }

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = CreateGameObject(node, parent);
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, null);
            var rt = go.GetComponent<RectTransform>();

            // Apply fills
            ApplyFills(go, node, ctx);

            // Apply blend mode
            var image_ = go.GetComponent<Image>();
            if (image_ != null && !string.IsNullOrEmpty(node.BlendMode))
                BlendModeHelper.TryApply(image_, node.BlendMode, ctx.Logger);

            // isMask FRAME: this defines a sibling-style mask in Figma.
            // ConvertChildren reparents subsequent siblings under this GameObject;
            // Unity's Mask component then clips them to this frame's alpha shape.
            // The mask shape sprite is taken from this node's own rasterization,
            // or from a child vector's sprite (typical: bubble_mask FRAME with VECTOR child).
            if (node.IsMask)
            {
                var image = go.GetComponent<Image>();
                if (image == null)
                {
                    image = go.AddComponent<Image>();
                    Sprite shapeSprite = null;
                    if (ctx.NodeSprites.TryGetValue(node.Id, out var ownSprite))
                        shapeSprite = ownSprite;
                    else if (node.Children != null)
                    {
                        foreach (var ch in node.Children)
                        {
                            if (ctx.NodeSprites.TryGetValue(ch.Id, out var childSprite))
                            {
                                shapeSprite = childSprite;
                                break;
                            }
                        }
                    }
                    if (shapeSprite != null)
                        image.sprite = shapeSprite;
                    else
                        image.color = UnityEngine.Color.white;
                }
                var mask = go.AddComponent<Mask>();
                mask.showMaskGraphic = false; // mask shape is invisible; only its alpha clips children
            }
            // Clips content → mask the frame's bounds. If ApplyFills already produced an
            // Image (rasterized bg / solid colour / image fill), pair it with a UGUI Mask
            // so children clip to the visible shape. Otherwise fall back to RectMask2D —
            // adding an empty white Image just to host a Mask leaves the prefab with a
            // bare Image that has m_Sprite: {fileID: 0}, which shows up in the inspector
            // as an unintended placeholder and confuses anyone reading the prefab tree.
            else if (node.ClipsContent)
            {
                var image = go.GetComponent<Image>();
                if (image != null)
                {
                    go.AddComponent<Mask>().showMaskGraphic = image.sprite != null;
                }
                else
                {
                    go.AddComponent<RectMask2D>();
                }
            }

            // Opacity. Skip when ApplyFills picked a rasterized sprite (NodeSprites): that
            // PNG already carries node.opacity baked in, so a CanvasGroup on top would
            // multiply opacity twice and the frame becomes much fainter than in Figma.
            bool spriteIsRasterized = ctx.NodeSprites.ContainsKey(node.Id);
            if (node.Opacity < 1f && !spriteIsRasterized)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            return go;
        }

        private GameObject CreateGameObject(FigmaNode node, GameObject parent)
        {
            var go = new GameObject(node.Name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();

            // Initial size from Figma's bounding box. ConvertChildren applies the actual
            // anchor / position layout afterwards (AutoLayoutMapper or AnchorMapper), but
            // those paths assume the child already has a sane sizeDelta to anchor against.
            // Without this seed value the child is born at (0,0,0,0) in the RectTransform,
            // and any downstream layout that fails or no-ops leaves the GameObject as a
            // zero-sized point — observed on KeyHint inside the primary-XL button variant,
            // which then collapsed every stretch-anchored descendant (highlight, etc.) to
            // zero too.
            rt.sizeDelta = SizeCalculator.GetSize(node);

            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            return go;
        }

        private void ApplyFills(GameObject go, FigmaNode node, ImportContext ctx)
        {
            // Check rasterized sprite FIRST — even if no visible fills, the node
            // may have been rasterized for effects/shadows/masks/etc. We must apply
            // the sprite or the node will appear empty (e.g., Frame 3845 with shadow).
            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                image.type = (sprite.border != UnityEngine.Vector4.zero)
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                image.preserveAspect = false;
                return;
            }

            if (!node.HasVisibleFills)
                return;

            // Solid color optimization — fill color uses only fill's own alpha+opacity.
            // node.Opacity is applied via CanvasGroup at end of Convert (already in place).
            if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
            {
                var (color, fillOpacity) = SolidColorOptimizer.GetTopSolidFill(node);
                if (color != null)
                {
                    var image = go.AddComponent<Image>();
                    image.color = ColorSpaceHelper.Convert(color, fillOpacity);
                    if (node.CornerRadius > 0)
                    {
                        var rounded = RoundedRectSpriteGenerator.GetOrGenerate(
                            node.CornerRadius, ctx.Profile.ImageScale, ctx.Profile.ImageOutputPath, ctx.Logger);
                        if (rounded != null)
                        {
                            image.sprite = rounded;
                            image.type = Image.Type.Sliced;
                        }
                    }
                    return;
                }
            }

            // Image fill (raw, uncropped — only when node itself wasn't rasterized)
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && fill.ImageRef != null)
                    {
                        if (ctx.FillSprites.TryGetValue(fill.ImageRef, out var fillSprite))
                        {
                            var image = go.AddComponent<Image>();
                            image.sprite = fillSprite;
                            image.type = Image.Type.Simple;
                            image.preserveAspect = fill.ScaleMode == "FIT";
                            return;
                        }
                    }
                }
            }
        }
    }
}
