namespace SoobakFigma2Unity.Editor.Models
{
    public enum FigmaNodeType
    {
        DOCUMENT,
        CANVAS,
        FRAME,
        GROUP,
        VECTOR,
        BOOLEAN_OPERATION,
        STAR,
        LINE,
        ELLIPSE,
        REGULAR_POLYGON,
        RECTANGLE,
        TEXT,
        SLICE,
        COMPONENT,
        COMPONENT_SET,
        INSTANCE,
        STICKY,
        SHAPE_WITH_TEXT,
        CONNECTOR,
        SECTION,
        TABLE,
        TABLE_CELL,
        WASHI_TAPE,
        TRANSFORM_GROUP,
        UNKNOWN
    }

    public enum ConstraintType
    {
        MIN,
        CENTER,
        MAX,
        STRETCH,
        SCALE
    }

    public enum LayoutMode
    {
        NONE,
        HORIZONTAL,
        VERTICAL
    }

    public enum AxisSizingMode
    {
        FIXED,
        AUTO
    }

    public enum PrimaryAxisAlignItems
    {
        MIN,
        CENTER,
        MAX,
        SPACE_BETWEEN
    }

    public enum CounterAxisAlignItems
    {
        MIN,
        CENTER,
        MAX,
        BASELINE
    }

    public enum LayoutSizing
    {
        FIXED,
        HUG,
        FILL
    }

    public enum PaintType
    {
        SOLID,
        GRADIENT_LINEAR,
        GRADIENT_RADIAL,
        GRADIENT_ANGULAR,
        GRADIENT_DIAMOND,
        IMAGE,
        EMOJI
    }

    public enum ScaleMode
    {
        FILL,
        FIT,
        TILE,
        STRETCH
    }

    public enum EffectType
    {
        INNER_SHADOW,
        DROP_SHADOW,
        LAYER_BLUR,
        BACKGROUND_BLUR
    }

    public enum BlendMode
    {
        PASS_THROUGH,
        NORMAL,
        DARKEN,
        MULTIPLY,
        LINEAR_BURN,
        COLOR_BURN,
        LIGHTEN,
        SCREEN,
        LINEAR_DODGE,
        COLOR_DODGE,
        OVERLAY,
        SOFT_LIGHT,
        HARD_LIGHT,
        DIFFERENCE,
        EXCLUSION,
        HUE,
        SATURATION,
        COLOR,
        LUMINOSITY
    }

    public enum TextAlignHorizontal
    {
        LEFT,
        CENTER,
        RIGHT,
        JUSTIFIED
    }

    public enum TextAlignVertical
    {
        TOP,
        CENTER,
        BOTTOM
    }

    public enum TextAutoResize
    {
        NONE,
        HEIGHT,
        WIDTH_AND_HEIGHT,
        TRUNCATE
    }

    public enum StrokeAlign
    {
        INSIDE,
        OUTSIDE,
        CENTER
    }
}
