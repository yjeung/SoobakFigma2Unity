using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts vector-type nodes (VECTOR, ELLIPSE, STAR, LINE, REGULAR_POLYGON,
    /// BOOLEAN_OPERATION) to Image components using rasterized images from Figma API.
    /// </summary>
    internal sealed class VectorConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node)
        {
            var t = node.NodeType;
            return t == FigmaNodeType.VECTOR ||
                   t == FigmaNodeType.ELLIPSE ||
                   t == FigmaNodeType.STAR ||
                   t == FigmaNodeType.LINE ||
                   t == FigmaNodeType.REGULAR_POLYGON ||
                   t == FigmaNodeType.BOOLEAN_OPERATION;
        }

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = new GameObject(node.Name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, null);

            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
            {
                ctx.Logger.Warn($"{node.Name}: vector node has no rasterized image");
            }

            // Preserve mask/boolean metadata
            if (node.IsMask || node.NodeType == FigmaNodeType.BOOLEAN_OPERATION)
            {
                var maskInfo = go.AddComponent<FigmaMaskInfo>();
                maskInfo.IsMask = node.IsMask;

                if (node.NodeType == FigmaNodeType.BOOLEAN_OPERATION)
                {
                    maskInfo.IsBooleanOperation = true;
                    maskInfo.BooleanOperation = node.Type;

                    if (node.Children != null)
                    {
                        var ids = new string[node.Children.Count];
                        for (int i = 0; i < node.Children.Count; i++)
                            ids[i] = node.Children[i].Id;
                        maskInfo.ChildNodeIds = ids;
                    }

                    ctx.Logger.Info($"{node.Name}: boolean operation '{node.Type}' rasterized (metadata preserved)");
                }
            }

            // isMask vector: add Unity Mask so reparented siblings are clipped to this shape.
            // ImportPipeline.ConvertChildren handles the sibling reparenting.
            if (node.IsMask)
            {
                var maskImg = go.GetComponent<Image>();
                if (maskImg == null)
                {
                    maskImg = go.AddComponent<Image>();
                    maskImg.color = UnityEngine.Color.white;
                }
                var mask = go.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }

            // Blend mode
            var img = go.GetComponent<Image>();
            if (img != null && !string.IsNullOrEmpty(node.BlendMode))
                BlendModeHelper.TryApply(img, node.BlendMode, ctx.Logger);

            // Opacity. Skip when the PNG is already carrying node.opacity in its alpha
            // channel (Figma's /v1/images bakes node.opacity into the exported alpha).
            bool spriteIsRasterized = ctx.NodeSprites.ContainsKey(node.Id);
            if (node.Opacity < 1f && !spriteIsRasterized)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            return go;
        }
    }
}
