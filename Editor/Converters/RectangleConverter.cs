using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts RECTANGLE nodes to Image components — but only when there is
    /// actual visual content. Empty rectangles (no fill, no sprite) get just
    /// a RectTransform so they don't render as blank white blocks that occlude
    /// content beneath them.
    /// </summary>
    internal sealed class RectangleConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node) => node.NodeType == FigmaNodeType.RECTANGLE;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = new GameObject(node.Name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, null);

            // Decide visual content (don't add Image until we have something to show)
            Sprite chosenSprite = null;
            UnityEngine.Color? chosenColor = null;
            bool spriteIsRasterized = false;

            // 1) Rasterized sprite (vector op, image-fill cropped, etc.)
            if (ctx.NodeSprites.TryGetValue(node.Id, out var nodeSprite))
            {
                chosenSprite = nodeSprite;
                spriteIsRasterized = true;
            }
            // 2) Solid color optimization — color uses only fill's own alpha+opacity.
            //    node.Opacity is applied via CanvasGroup below for consistency.
            else if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
            {
                var (color, fillOpacity) = SolidColorOptimizer.GetTopSolidFill(node);
                if (color != null)
                    chosenColor = ColorSpaceHelper.Convert(color, fillOpacity);
            }
            // 3) Raw image fill (uncropped — fallback when node wasn't rasterized)
            else if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && fill.ImageRef != null &&
                        ctx.FillSprites.TryGetValue(fill.ImageRef, out var fillSprite))
                    {
                        chosenSprite = fillSprite;
                        break;
                    }
                }
            }

            // Only add Image when there is actual content to render
            if (chosenSprite != null || chosenColor != null)
            {
                var image = go.AddComponent<Image>();
                if (chosenSprite != null)
                {
                    image.sprite = chosenSprite;
                    image.type = (chosenSprite.border != UnityEngine.Vector4.zero)
                        ? Image.Type.Sliced
                        : Image.Type.Simple;
                    image.color = UnityEngine.Color.white;
                }
                else
                {
                    image.color = chosenColor.Value;
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
                }

                BlendModeHelper.TryApply(image, node.BlendMode, ctx.Logger);
            }

            // Node opacity → CanvasGroup.
            // SKIP when the displayed sprite was rasterized by Figma's /v1/images API:
            // that PNG already has node.opacity baked into its alpha channel (verified
            // empirically — an opacity=0.4 node with a fill.opacity=0.2 lands as alpha
            // 20/255 ≈ 0.08 in the exported PNG, exactly 0.4 × 0.2). Applying the
            // CanvasGroup on top would multiply opacity twice and wash the node out.
            if (node.Opacity < 1f && !spriteIsRasterized)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            if (node.CornerRadius > 0)
                ctx.Logger.Info($"{node.Name}: cornerRadius={node.CornerRadius}px (9-slice candidate)");

            // isMask RECTANGLE: in Figma the mask shape itself is invisible — it only
            // defines the clipping alpha for subsequent siblings. Add Unity Mask with
            // showMaskGraphic=false so the rectangle is hidden but its alpha clips
            // children that ConvertChildren reparents under it.
            if (node.IsMask)
            {
                var maskImage = go.GetComponent<Image>();
                if (maskImage == null)
                {
                    // No image was added (e.g., not rasterized) — use solid white as alpha source
                    maskImage = go.AddComponent<Image>();
                    maskImage.color = UnityEngine.Color.white;
                }
                if (go.GetComponent<Mask>() == null)
                    go.AddComponent<Mask>().showMaskGraphic = false;
            }

            return go;
        }
    }
}
