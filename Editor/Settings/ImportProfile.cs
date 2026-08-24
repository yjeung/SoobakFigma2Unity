namespace SoobakFigma2Unity.Editor.Settings
{
    public enum ColorSpaceMode
    {
        Auto,
        Linear,
        Gamma
    }

    /// <summary>
    /// Controls what happens on re-import when a prefab already exists.
    /// </summary>
    public enum MergeMode
    {
        /// <summary>Merge with existing prefab. Preserve user-added components, children,
        /// and any components the user locked (either whole-GO or per-type). Default.</summary>
        SmartMerge,
        /// <summary>Blow the existing prefab away and save the freshly-built one as-is. Loses
        /// all user edits. Available for debugging or for intentional resets.</summary>
        FullReplace
    }

    [System.Serializable]
    public sealed class ImportProfile
    {
        // Connection
        public string FigmaUrl = "";
        public string PersonalAccessToken = "";

        // Import always extracts every Figma COMPONENT referenced by the imported screens
        // as a standalone prefab (in ComponentOutputPath) AND writes the screen prefab(s)
        // with INSTANCEs linked back as PrefabInstances. There is no mode toggle — the
        // earlier ComponentsOnly / ScreenOnly options shipped half-built imports more often
        // than they helped.

        // Image
        public float ImageScale = 2f;
        public bool AutoNineSlice = true;
        public bool SolidColorOptimization = true;

        // Layout
        public bool ConvertAutoLayout = true;
        // Off by default: preserve Figma Group structure 1:1 in Unity hierarchy.
        // When on, empty Groups are removed and their children are placed under
        // the outer parent (positioning is recalculated to remain correct).
        public bool FlattenEmptyGroups = false;
        public bool ApplyConstraints = true;

        // Color
        public ColorSpaceMode ColorSpace = ColorSpaceMode.Auto;

        // Prefab (P1 features, kept as flags)
        public bool GeneratePrefabVariants = true;
        public bool MapComponentInstances = true;

        // Re-import behaviour
        public MergeMode MergeMode = MergeMode.SmartMerge;
        public int BackupRetentionCount = 5;

        // Output paths
        public string PrefabOutputPath = "Assets/UI/Prefabs";
        public string ScreenOutputPath = "Assets/UI/Screens";
        public string ImageOutputPath = "Assets/UI/Images";

        // Component auto-extraction (zero-config Figma component → Unity prefab)
        // When enabled, the import pre-pass walks the entire imported tree, builds
        // a dependency graph of every Figma COMPONENT/INSTANCE, and ensures each
        // one has a .prefab file in ComponentOutputPath before the main convert
        // runs. The screen prefab then ends up referencing those prefabs as
        // PrefabInstances rather than inlining everything.
        public string ComponentOutputPath = "Assets/UI/Components";
        public bool ExtractFigmaComponentsAsPrefabs = true;

        // FLAT rasterisation per atomic-visual-group.
        //
        // When true, an explicitly image-named (`img_`, `ic_`, or `slice_`) FRAME /
        // GROUP / INSTANCE whose descendant tree is purely decorative is exported
        // as a single PNG and lands as one GameObject with one Image. Nodes without
        // one of those prefixes stay structural regardless of their geometry.
        //
        // Why this matters: Figma's compositing (alpha, blend modes, mask
        // intersections, stroke offsets, drop-shadow under/over rules) does not
        // match UGUI's. Even when each individual element exports correctly the
        // assembled visual drifts — wavy lines flatten, strokes clip, layers mis-
        // overlap. Folding decorative groups into a single PNG sidesteps the
        // compositing question for the parts that don't need editing while
        // preserving the editability of the parts that do.
        //
        // Concrete win: a button variant whose `slice_btn_primary` decorative instance
        // contains only inner rectangles ships as one bg PNG (curves intact),
        // while the variant's "Label" text node stays a TMP for runtime editing.
        //
        // Default: true. Designers can disable for fully-structural mode (every
        // primitive becomes its own UGUI component) if they need maximum runtime
        // mutability and accept the compositing drift.
        public bool RasterizeAtomicVisualGroups = true;

        // Phase 2 (opt-in): structural duplicate detection — promote subtrees that
        // repeat 2+ times in a frame even when the designer didn't mark them as
        // Figma components. Off by default because false-positives (visually
        // similar but semantically distinct subtrees getting collapsed) are hard
        // to debug.
        public bool DetectStructuralDuplicates = false;
    }
}
