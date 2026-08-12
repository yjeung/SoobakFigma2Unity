using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using TMPro;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts TEXT nodes to TextMeshProUGUI components.
    /// </summary>
    internal sealed class TextConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node) => node.NodeType == FigmaNodeType.TEXT;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = new GameObject(node.Name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, null);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = node.Characters ?? "";

            // Font style
            var style = node.Style;
            if (style != null)
            {
                tmp.fontSize = style.FontSize;

                // Font style (bold/italic)
                FontStyles fontStyles = FontStyles.Normal;
                if (style.FontWeight >= 700) fontStyles |= FontStyles.Bold;
                if (style.Italic) fontStyles |= FontStyles.Italic;
                tmp.fontStyle = fontStyles;

                // Letter spacing (Figma is in pixels, TMP is in em units)
                if (style.FontSize > 0)
                    tmp.characterSpacing = (style.LetterSpacing / style.FontSize) * 100f;

                // Line spacing (Figma lineHeightPx vs TMP line spacing)
                if (style.LineHeightPx > 0 && style.FontSize > 0)
                {
                    // TMP lineSpacing is percentage offset from default
                    float defaultLineHeight = style.FontSize * 1.2f;
                    tmp.lineSpacing = ((style.LineHeightPx / defaultLineHeight) - 1f) * 100f;
                }

                // Paragraph spacing
                tmp.paragraphSpacing = style.ParagraphSpacing;

                // Horizontal alignment
                tmp.horizontalAlignment = style.TextAlignHorizontal switch
                {
                    "LEFT" => HorizontalAlignmentOptions.Left,
                    "CENTER" => HorizontalAlignmentOptions.Center,
                    "RIGHT" => HorizontalAlignmentOptions.Right,
                    "JUSTIFIED" => HorizontalAlignmentOptions.Justified,
                    _ => HorizontalAlignmentOptions.Left
                };

                // Vertical alignment
                tmp.verticalAlignment = style.TextAlignVertical switch
                {
                    "TOP" => VerticalAlignmentOptions.Top,
                    "CENTER" => VerticalAlignmentOptions.Middle,
                    "BOTTOM" => VerticalAlignmentOptions.Bottom,
                    _ => VerticalAlignmentOptions.Top
                };

                // Auto-resize
                tmp.enableAutoSizing = false;
                tmp.overflowMode = style.TextAutoResize switch
                {
                    "TRUNCATE" => TextOverflowModes.Ellipsis,
                    _ => TextOverflowModes.Overflow
                };

                // Log font info for manual mapping
                if (!string.IsNullOrEmpty(style.FontFamily))
                {
                    ctx.Logger.Info($"{node.Name}: font={style.FontFamily}, weight={style.FontWeight}");
                }
            }

            // Text color from fills — use only fill's own alpha+opacity here.
            // node.Opacity is applied via CanvasGroup below for consistency with
            // FrameConverter/VectorConverter (avoids double-multiplication).
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsSolid && fill.Color != null)
                    {
                        tmp.color = ColorSpaceHelper.Convert(fill.Color, fill.Opacity);
                        break;
                    }
                }
            }

            BlendModeHelper.TryApplyToText(tmp, node.BlendMode, ctx.Logger);

            // Node opacity → CanvasGroup (composes correctly with parent CanvasGroups)
            if (node.Opacity < 1f)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            return go;
        }
    }
}
