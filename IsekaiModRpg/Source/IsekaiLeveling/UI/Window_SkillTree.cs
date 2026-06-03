using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// PoE2-inspired constellation window with pan/zoom layout.
    /// Dark background, glowing nodes, connection lines, and interactive allocation.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_SkillTree : Window
    {
        // ── Pawn & component ──
        private readonly Pawn pawn;
        private readonly IsekaiComponent comp;

        // ── View state ──
        private Vector2 panOffset    = Vector2.zero;
        private Vector2 classTabScroll = Vector2.zero;
        private float zoom = 1f;
        private const float MIN_ZOOM = 0.35f;
        private const float MAX_ZOOM = 2.0f;
        private bool isPanning;
        private Vector2 panStart;
        private Vector2 panStartOffset;

        // ── Hover / selection ──
        private string hoveredNodeId;
        private string selectedNodeId;
        private readonly Dictionary<string, float> _btnHover = new Dictionary<string, float>();

        // ── Motion design state ──
        private float  _winElapsed  = 0f;   // cumulative seconds since window opened
        private float  _winOpenT    = 0f;   // 0→1 entrance fade (~0.4 s)
        private float  _detailT     = 1f;   // 0→1 detail content fade on selection change
        private string _lastSelId   = null;
        private int    _lastPtCount = -1;   // detect available-points change
        private float  _ptFlashT    = 0f;   // 0→1 points-change flash
        private string  _lastTreeId   = null;          // detect tree switch → restart spawns
        private float   _treeElapsed  = 0f;              // seconds since last tree switch (for spawn stagger)
        private float   _spawnMaxDist = 1f;              // cached max grid-distance from Start node
        private Vector2 _spawnOrigin  = Vector2.zero;    // cached Start-node position (grid coords)
        private readonly Dictionary<string, float> _nodeHoverT  = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _nodeUnlockT = new Dictionary<string, float>(); // realtimeSinceStartup at unlock
        private readonly Dictionary<string, float> _nodeSpawnT  = new Dictionary<string, float>(); // 0→1 per node
        private readonly Dictionary<string, float> _tabHoverT   = new Dictionary<string, float>();

        // ── Layout ──
        private const float GRID_SCALE   = 70f;
        private const float NODE_MINOR   = 32f;
        private const float NODE_NOTABLE = 44f;
        private const float NODE_KEYSTONE= 56f;
        private const float FOOTER_H     = 46f;  // overlay footer inside tree container

        // Native pixel dimensions of each texture — used to derive all layout rects
        private const float TEX_CLASSPANEL_W  = 321f;
        private const float TEX_CLASSPANEL_H  = 737f;
        private const float TEX_CONSTBG_W     = 1183f;
        private const float TEX_CONSTBG_H     = 734f;
        private const float TEX_TREECONT_W    = 1077f;
        private const float TEX_TREECONT_H    = 621f;
        private const float TEX_DETAILPANEL_W = 260f;
        private const float TEX_DETAILPANEL_H = 586f;
        private const float TEX_CLASSTAB_W    = 230f;
        private const float TEX_CLASSTAB_H    = 47f;

        // ── Colors (custom palette: #312021 #231610 #2B1C19 #49362F #F9E5AA #E6B067) ──
        private static readonly Color BgDark       = new Color(0.137f, 0.086f, 0.063f);       // #231610
        private static readonly Color PanelDark    = new Color(0.192f, 0.125f, 0.129f);       // #312021
        private static readonly Color SurfaceDark  = new Color(0.169f, 0.110f, 0.098f);       // #2B1C19
        private static readonly Color BorderBrown  = new Color(0.286f, 0.212f, 0.184f);       // #49362F
        private static readonly Color TextLight    = new Color(0.976f, 0.898f, 0.667f);       // #F9E5AA
        private static readonly Color TextGold     = new Color(0.902f, 0.690f, 0.404f);       // #E6B067
        private static readonly Color TextMuted    = new Color(0.286f, 0.212f, 0.184f, 0.8f); // #49362F muted
        private static readonly Color NodeMinorLocked     = SurfaceDark;
        private static readonly Color NodeMinorUnlocked   = new Color(0.902f, 0.690f, 0.404f, 0.6f); // #E6B067 soft
        private static readonly Color NodeNotableLocked   = BorderBrown;
        private static readonly Color NodeNotableUnlocked = TextGold;
        private static readonly Color NodeKeystoneLocked  = new Color(0.192f, 0.125f, 0.129f); // #312021
        private static readonly Color NodeKeystoneUnlocked = new Color(0.976f, 0.898f, 0.667f); // #F9E5AA
        private static readonly Color ConnLocked    = new Color(0.286f, 0.212f, 0.184f, 0.35f); // #49362F dim
        private static readonly Color ConnUnlocked  = new Color(0.902f, 0.690f, 0.404f, 0.8f);  // #E6B067
        private static readonly Color ConnAvailable = new Color(0.286f, 0.212f, 0.184f, 0.55f); // #49362F mid
        private static readonly Color GlowGold      = new Color(0.902f, 0.690f, 0.404f, 0.25f); // #E6B067 glow

        // ── Node shape textures (cached) ──
        private static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        private static Texture2D GetCachedTex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (texCache.TryGetValue(path, out var tex) && tex != null) return tex;
            tex = ContentFinder<Texture2D>.Get(path, false);
            if (tex != null) texCache[path] = tex;
            return tex;
        }

        // ── GL rendering support ──
        private static Material _glTexMat;
        private static void EnsureGLTexMat()
        {
            if (_glTexMat != null) return;
            _glTexMat = new Material(Shader.Find("UI/Default"));
        }

        /// <summary>
        /// Loads a texture from disk as raw RGBA32, bypassing RimWorld's content pipeline
        /// which applies destructive DXT block-compression. Use this for large background
        /// textures where compression artifacts are clearly visible.
        /// </summary>
        private static Texture2D LoadUncompressedTex(string rimTexPath)
        {
            if (string.IsNullOrEmpty(rimTexPath)) return null;
            const string cacheKey = "__uncompressed__";
            string fullKey = cacheKey + rimTexPath;
            if (texCache.TryGetValue(fullKey, out var cached) && cached != null) return cached;

            string[] extensions = { ".png", ".jpg", ".jpeg" };
            foreach (var mod in LoadedModManager.RunningMods)
            {
                foreach (var ext in extensions)
                {
                    string filePath = Path.Combine(
                        mod.RootDir,
                        "Textures",
                        rimTexPath.Replace('/', Path.DirectorySeparatorChar) + ext);

                    if (!File.Exists(filePath)) continue;

                    byte[] data = File.ReadAllBytes(filePath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
                    ImageConversion.LoadImage(tex, data); // auto-resizes, no compression
                    tex.filterMode = FilterMode.Bilinear;
                    tex.anisoLevel = 4;
                    tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                    texCache[fullKey] = tex;
                    return tex;
                }
            }

            // Fallback — at least set Bilinear on whatever ContentFinder returns
            var fallback = ContentFinder<Texture2D>.Get(rimTexPath, false);
            if (fallback != null)
            {
                fallback.filterMode = FilterMode.Bilinear;
                texCache[fullKey] = fallback;
            }
            return fallback;
        }

        /// <summary>Node shape texture path based on type and state</summary>
        private static string NodeShapePath(PassiveNodeType type, bool active)
        {
            switch (type)
            {
                case PassiveNodeType.Keystone:
                    return active ? "SkillTree/NodeKeystoneActive" : "SkillTree/NodeKeystone";
                case PassiveNodeType.Notable:
                    return active ? "SkillTree/NodeNotableActive" : "SkillTree/NodeNotable";
                case PassiveNodeType.Start:
                    return active ? "SkillTree/NodeStartActive" : "SkillTree/NodeStart";
                default: // Minor
                    return active ? "SkillTree/NodeMinorActive" : "SkillTree/NodeMinor";
            }
        }

        // ── Class tabs ──
        private PassiveTreeDef currentTree;
        private string selectedClass = "Knight";
        private static readonly string[] ALL_CLASSES = { "Knight", "Mage", "Paladin", "Sage", "Ranger", "Duelist", "Crafter", "Leader", "Survivor", "Berserker", "Alchemist", "Beastmaster" };
        private static readonly Color[] CLASS_COLORS =
        {
            new Color(0.95f, 0.45f, 0.35f), // Knight - red
            new Color(0.45f, 0.55f, 0.95f), // Mage - blue
            new Color(0.90f, 0.80f, 0.30f), // Paladin - gold
            new Color(0.60f, 0.80f, 0.95f), // Sage - light blue
            new Color(0.45f, 0.85f, 0.45f), // Ranger - green
            new Color(0.85f, 0.45f, 0.65f), // Duelist - magenta
            new Color(0.85f, 0.65f, 0.35f), // Crafter - orange
            new Color(0.75f, 0.55f, 0.95f), // Leader - purple
            new Color(0.40f, 0.80f, 0.70f), // Survivor - teal
            new Color(0.85f, 0.25f, 0.25f), // Berserker - blood red
            new Color(0.55f, 0.90f, 0.55f), // Alchemist - lime green
            new Color(0.80f, 0.60f, 0.30f), // Beastmaster - amber
        };

        // ── Window config ──
        public override Vector2 InitialSize
        {
            get
            {
                float maxW = Verse.UI.screenWidth;
                float maxH = Verse.UI.screenHeight - 35f;
                return new Vector2(Mathf.Min(1473f, maxW), Mathf.Min(720f, maxH));
            }
        }
        protected override float Margin => 0f;

        public Window_SkillTree(Pawn pawn)
        {
            this.pawn = pawn;
            this.comp = pawn.GetComp<IsekaiComponent>();

            bool vanilla = !IsekaiLevelingSettings.UseIsekaiUI;
            doCloseButton = false;
            doCloseX = vanilla;
            draggable = true;
            resizeable = false;
            doWindowBackground = vanilla;
            drawShadow = vanilla;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            forcePause = false;

            // If pawn already chose a tree, show that
            if (!string.IsNullOrEmpty(comp?.passiveTree?.assignedTree))
                selectedClass = comp.passiveTree.assignedTree;

            LoadTree(selectedClass);
        }

        private void LoadTree(string treeClass)
        {
            currentTree = DefDatabase<PassiveTreeDef>.AllDefs
                .FirstOrDefault(t => t.treeClass == treeClass);
            selectedNodeId = null; // Clear stale selection from previous tree
        }

        // ──────────────────── Animation ────────────────────

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private void TickAnimations()
        {
            float dt = Time.unscaledDeltaTime;
            _winElapsed  += dt;
            _treeElapsed += dt;

            // Window entrance fade — EaseOutCubic baked in so all draw sites get smooth alpha
            _winOpenT = EaseOutCubic(Mathf.Clamp01(_winElapsed / 0.65f));

            // Detail panel content fade — resets to 0 whenever selection changes
            if (_lastSelId != selectedNodeId)
            {
                _lastSelId = selectedNodeId;
                _detailT   = 0f;
            }
            _detailT = Mathf.MoveTowards(_detailT, 1f, dt / 0.35f);

            // Available-points flash — fires on point gain/spend
            int curPts = comp?.passiveTree?.availablePoints ?? 0;
            if (_lastPtCount >= 0 && curPts != _lastPtCount)
                _ptFlashT = 1f;
            _lastPtCount = curPts;
            _ptFlashT = Mathf.MoveTowards(_ptFlashT, 0f, dt * 1.8f);

            // Node spawn stagger — radiates outward from the Start node
            string treeId = currentTree?.treeClass ?? "";
            if (treeId != _lastTreeId)
            {
                _lastTreeId = treeId;
                _nodeSpawnT.Clear();
                _treeElapsed = 0f; // reset spawn stagger timer; _winElapsed keeps running for entrance fade

                // Cache origin and max-distance once per tree switch
                if (currentTree != null && currentTree.nodes != null && currentTree.nodes.Count > 0)
                {
                    var startNode = currentTree.GetStartNode();
                    _spawnOrigin = startNode != null
                        ? new Vector2(startNode.x, startNode.y)
                        : Vector2.zero;

                    float maxD = 0f;
                    foreach (var n in currentTree.nodes)
                    {
                        float d = Vector2.Distance(new Vector2(n.x, n.y), _spawnOrigin);
                        if (d > maxD) maxD = d;
                    }
                    _spawnMaxDist = maxD < 0.001f ? 1f : maxD;
                }
            }
            if (currentTree != null)
            {
                const float SPAWN_SPREAD = 2.80f; // seconds from first to last node starting
                const float FADE_DUR     = 1.00f; // seconds each node takes to scale 0→1

                foreach (var node in currentTree.nodes)
                {
                    if (!_nodeSpawnT.ContainsKey(node.nodeId)) _nodeSpawnT[node.nodeId] = 0f;
                    float dist  = Vector2.Distance(new Vector2(node.x, node.y), _spawnOrigin);
                    float delay = (dist / _spawnMaxDist) * SPAWN_SPREAD;
                    if (_treeElapsed > delay)
                        _nodeSpawnT[node.nodeId] = Mathf.MoveTowards(
                            _nodeSpawnT[node.nodeId], 1f, dt / FADE_DUR);
                }
            }
        }

        // ──────────────────── Main Layout ────────────────────

        /// <summary>
        /// Derives all layout rects from texture native aspect ratios so every element
        /// renders proportionally regardless of window scale.
        /// </summary>
        private void ComputeRects(Rect inRect,
            out Rect classPanelRect,
            out Rect treeContRect,
            out Rect treeRect,
            out Rect detailPanelRect,
            out Rect footerRect)
        {
            float winW = inRect.width;
            float winH = inRect.height;

            // ClassPanel: left column; height = window height, width from texture ratio
            float classPanelW = winH * (TEX_CLASSPANEL_W / TEX_CLASSPANEL_H);
            classPanelRect = new Rect(0, 0, classPanelW, winH);

            // ConstellationBg fills the remaining width
            float constBgW = winW - classPanelW;
            float bgScale  = constBgW / TEX_CONSTBG_W; // uniform scale relative to bg texture

            // SkillTreeContainerBG: scaled uniformly, centered inside ConstellationBg strip
            float tcW = TEX_TREECONT_W * bgScale;
            float tcH = TEX_TREECONT_H * bgScale;
            float tcX = classPanelW + (constBgW - tcW) * 0.5f;
            float tcY = (winH - tcH) * 0.5f;
            treeContRect = new Rect(tcX, tcY, tcW, tcH);

            // DetailPanel: scaled uniformly, vertically centered in container, inset from right wall
            float dpW      = TEX_DETAILPANEL_W * bgScale;
            float dpH      = TEX_DETAILPANEL_H * bgScale;
            float dpInsetR = tcW * 0.018f;   // ~1.8% of container width from right wall
            float dpX      = treeContRect.xMax - dpW - dpInsetR;
            float dpY      = tcY + (tcH - dpH) * 0.5f;
            detailPanelRect = new Rect(dpX, dpY, dpW, dpH);

            // Tree area: all of container left of the detail panel
            treeRect = new Rect(tcX, tcY, tcW - dpW, tcH);

            // Footer: bottom overlay strip of the tree area
            footerRect = new Rect(treeRect.x, treeRect.yMax - FOOTER_H, treeRect.width, FOOTER_H);
        }

        public override void DoWindowContents(Rect inRect)
        {
            TickAnimations();
            float winW = inRect.width;
            float winH = inRect.height;

            float classPanelW = winH * (TEX_CLASSPANEL_W / TEX_CLASSPANEL_H);
            float constBgW    = winW - classPanelW;
            Rect  constBgRect = new Rect(classPanelW, 0, constBgW, winH);

            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                var constBgTex = LoadUncompressedTex("SkillTree/ConstellationBg");
                if (constBgTex != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, _winOpenT);
                    GUI.DrawTexture(constBgRect, constBgTex, ScaleMode.StretchToFill);
                }
            }

            // ── Derive all layout rects ──
            ComputeRects(inRect,
                out Rect classPanelRect,
                out Rect treeContRect,
                out Rect treeRect,
                out Rect detailPanelRect,
                out Rect footerRect);

            // ── SkillTreeContainerBG ──
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                var treeContTex = LoadUncompressedTex("SkillTree/SkillTreeContainerBG");
                if (treeContTex != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, _winOpenT);
                    GUI.DrawTexture(treeContRect, treeContTex, ScaleMode.StretchToFill);
                }
            }
            else
            {
                GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
                GUI.DrawTexture(treeContRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
                Widgets.DrawBox(treeContRect);
            }

            // ── Tree container: clip all interior draws to treeContRect ──
            Rect lcTree   = new Rect(treeRect.x        - treeContRect.x, treeRect.y        - treeContRect.y, treeRect.width,        treeRect.height);
            Rect lcDetail = new Rect(detailPanelRect.x  - treeContRect.x, detailPanelRect.y  - treeContRect.y, detailPanelRect.width, detailPanelRect.height);
            Rect lcFooter = new Rect(footerRect.x       - treeContRect.x, footerRect.y       - treeContRect.y, footerRect.width,      footerRect.height);

            GUI.BeginGroup(treeContRect);
            DrawTreeArea(lcTree);
            DrawDetailPanel(lcDetail);
            DrawFooter(lcFooter);
            GUI.EndGroup();

            // Class panel is outside the container, drawn in screen space
            DrawClassPanel(classPanelRect);

            // Close button — top-right corner (custom only; vanilla uses built-in doCloseX)
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                Rect closeRect = new Rect(winW - 36f, 8f, 28f, 28f);
                if (DrawStyledButton(closeRect, "X", "close_window", true))
                    Close();
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        // ──────────────────── Header (stub — no longer used) ────────────────────

        private void DrawHeader(Rect rect) { }

        // ──────────────────── Class Panel ────────────────────

        private void DrawClassPanel(Rect rect)
        {
            // ── Panel background ──
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                var panelTex = LoadUncompressedTex("SkillTree/ClassPanel");
                GUI.color = new Color(1f, 1f, 1f, _winOpenT);
                if (panelTex != null)
                    GUI.DrawTexture(rect, panelTex, ScaleMode.StretchToFill);
                else
                {
                    GUI.color = new Color(SurfaceDark.r, SurfaceDark.g, SurfaceDark.b, 0.95f);
                    GUI.DrawTexture(rect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }
            else
            {
                Widgets.DrawWindowBackground(rect);
            }

            // Content drawn in local (group-relative) coordinates
            GUI.BeginGroup(rect);
            float W = rect.width;
            float H = rect.height;

            // Side padding
            float padX   = W * 0.075f;
            float innerW = W - padX * 2f;

            // The ClassPanel.png art border occupies ~13% from top and ~12% from bottom.
            // All content must stay inside those zones.
            float topBorder    = H * 0.13f;
            float bottomBorder = H * 0.12f;
            float pawnInfoH    = H * 0.10f;  // zone for pawn name at bottom, above border

            // ── Header: title + class subtitle ──
            bool vanillaPanel = !IsekaiLevelingSettings.UseIsekaiUI;
            float titleY = vanillaPanel ? 10f : topBorder + 4f;
            GUI.color = vanillaPanel ? Color.white : TextLight;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(0, titleY, W, 22f), "Class Selection");

            string currentCls = !string.IsNullOrEmpty(comp?.passiveTree?.assignedTree)
                ? comp.passiveTree.assignedTree
                : selectedClass ?? ALL_CLASSES[0];
            GUI.color = vanillaPanel ? new Color(0.7f, 0.7f, 0.7f) : new Color(TextMuted.r * 1.6f, TextMuted.g * 1.6f, TextMuted.b * 1.6f, 0.9f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, titleY + 23f, W, 18f), $"Pawn Class : {currentCls}");

            float headerH = titleY + 23f + 22f;  // bottom of subtitle + small gap

            // ── Class tab scroll list ──
            const float TAB_H   = 46f;
            const float TAB_W   = 229f;
            const float TAB_GAP = 5f;

            float tabAreaY = headerH;
            float tabAreaH = vanillaPanel
                ? H - headerH - 10f - pawnInfoH - 38f - 10f
                : H - headerH - bottomBorder - pawnInfoH - 38f - 10f;
            float tabW     = innerW;

            float contentH = ALL_CLASSES.Length * TAB_H + (ALL_CLASSES.Length - 1) * TAB_GAP;
            bool  needScroll = contentH > tabAreaH;

            Rect scrollView = new Rect(padX, tabAreaY, tabW, tabAreaH);
            Rect scrollCont = needScroll
                ? new Rect(0, 0, tabW - 16f, contentH)
                : new Rect(0, 0, tabW, contentH);

            var classTex = GetCachedTex("SkillTree/ClassTab");
            bool hasAssigned = !string.IsNullOrEmpty(comp?.passiveTree?.assignedTree);

            Widgets.BeginScrollView(scrollView, ref classTabScroll, scrollCont, needScroll);

            float scrollInnerW = needScroll ? tabW - 16f : tabW;

            for (int i = 0; i < ALL_CLASSES.Length; i++)
            {
                string cls         = ALL_CLASSES[i];
                bool isSelected    = selectedClass == cls;
                bool isAssigned    = hasAssigned && comp.passiveTree.assignedTree == cls;
                bool hasEntered    = hasAssigned && !isAssigned && comp.passiveTree.HasEnteredTree(
                    DefDatabase<PassiveTreeDef>.AllDefs.FirstOrDefault(t => t.treeClass == cls));
                bool isLocked      = hasAssigned && !isAssigned && !hasEntered;
                bool isImplemented = cls == "Knight" || cls == "Mage" || cls == "Paladin" || cls == "Sage" || cls == "Ranger" || cls == "Duelist" || cls == "Crafter" || cls == "Leader" || cls == "Survivor" || cls == "Berserker" || cls == "Alchemist" || cls == "Beastmaster";

                float tabY    = i * (TAB_H + TAB_GAP);
                float tabX    = (scrollInnerW - TAB_W) * 0.5f;  // centre horizontally
                Rect tabRect  = new Rect(tabX, tabY, TAB_W, TAB_H);

                // Tab background
                if (IsekaiLevelingSettings.UseIsekaiUI)
                {
                    if (classTex != null)
                    {
                        GUI.color = !isImplemented  ? new Color(0.38f, 0.30f, 0.26f, 0.55f)
                                  : isSelected      ? Color.white
                                  : isLocked        ? new Color(0.55f, 0.50f, 0.46f, 0.65f)
                                                    : new Color(0.72f, 0.65f, 0.58f, 0.85f);
                        GUI.DrawTexture(tabRect, classTex, ScaleMode.StretchToFill);
                    }

                    // Smooth tab hover glow
                    if (!_tabHoverT.ContainsKey(cls)) _tabHoverT[cls] = 0f;
                    _tabHoverT[cls] = Mathf.MoveTowards(_tabHoverT[cls],
                        (isImplemented && Mouse.IsOver(tabRect) && !isSelected) ? 1f : 0f,
                        Time.unscaledDeltaTime * 8f);
                    if (_tabHoverT[cls] > 0f)
                    {
                        GUI.color = new Color(1f, 1f, 1f, EaseOutCubic(_tabHoverT[cls]) * 0.08f);
                        GUI.DrawTexture(tabRect, BaseContent.WhiteTex);
                    }
                }
                else
                {
                    if (isSelected)
                    {
                        Widgets.DrawHighlight(tabRect);
                        Widgets.DrawHighlight(tabRect);
                    }
                    else if (isImplemented && Mouse.IsOver(tabRect))
                        Widgets.DrawHighlight(tabRect);
                    if (!isImplemented)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.15f);
                        GUI.DrawTexture(tabRect, BaseContent.WhiteTex);
                        GUI.color = Color.white;
                    }
                }

                // Label
                if (vanillaPanel)
                {
                    GUI.color = !isImplemented
                        ? new Color(0.5f, 0.5f, 0.5f, 0.4f)
                        : isSelected    ? CLASS_COLORS[i]
                        : isAssigned    ? new Color(0.9f, 0.75f, 0.25f)
                        : hasEntered    ? new Color(0.6f, 0.8f, 1.0f)
                        : isLocked      ? new Color(0.7f, 0.7f, 0.7f, 0.65f)
                                        : Color.white;
                }
                else
                {
                    GUI.color = !isImplemented
                        ? new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.40f)
                        : isSelected    ? CLASS_COLORS[i]
                        : isAssigned    ? new Color(1.0f, 0.85f, 0.40f, 0.90f)
                        : hasEntered    ? new Color(0.75f, 0.90f, 1.0f, 0.90f)
                        : isLocked      ? new Color(TextLight.r, TextLight.g, TextLight.b, 0.65f)
                                        : TextLight;
                }
                Text.Font   = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(tabRect, cls);

                // "Soon" sub-label
                if (!isImplemented)
                {
                    GUI.color = vanillaPanel
                        ? new Color(0.5f, 0.5f, 0.5f, 0.28f)
                        : new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.28f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(tabRect.x, tabRect.yMax - 13f, tabRect.width, 13f), "— soon —");
                }

                if (Widgets.ButtonInvisible(tabRect))
                {
                    if (!isImplemented)
                        TooltipHandler.TipRegion(tabRect, "Isekai_TreeComingSoon".Translate());
                    else
                    {
                        selectedClass = cls;
                        LoadTree(cls);
                    }
                }
            }

            Widgets.EndScrollView();

            // ── Respec button (shown when non-start nodes are allocated) ──
            bool hasNonStartNodes = comp?.passiveTree?.HasNonStartNodes ?? false;
            bool canRespec = comp?.passiveTree?.CanRespec ?? false;
            int respecsRemaining = comp?.passiveTree?.RespecsRemaining ?? 0;

            if (hasNonStartNodes || !canRespec)
            {
                float respecY = vanillaPanel
                    ? H - 10f - pawnInfoH - 38f - 5f
                    : H - bottomBorder - pawnInfoH - 38f - 5f;

                // Show remaining respecs label above the button
                GUI.color = canRespec
                    ? (vanillaPanel ? new Color(0.7f, 0.7f, 0.7f, 0.6f) : new Color(TextLight.r, TextLight.g, TextLight.b, 0.6f))
                    : new Color(1f, 0.45f, 0.4f, 0.7f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                string respecLabel = canRespec
                    ? "Isekai_TreeRespecRemaining".Translate(respecsRemaining.ToString())
                    : "Isekai_TreeRespecNone".Translate();
                Widgets.Label(new Rect(padX, respecY - 18f, innerW, 16f), respecLabel);
                Text.Anchor = TextAnchor.UpperLeft;

                Rect respecR = new Rect(padX, respecY, innerW, 34f);
                bool canClickRespec = hasNonStartNodes && canRespec;
                if (DrawStyledButton(respecR, "Isekai_PassiveRespec".Translate(), "respec_cls", canClickRespec))
                {
                    comp.passiveTree.Respec();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }

            // ── Pawn info pinned above the bottom border ──
            float piY = vanillaPanel
                ? H - 10f - pawnInfoH
                : H - bottomBorder - pawnInfoH;
            GUI.color = vanillaPanel ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.35f);
            GUI.DrawTexture(new Rect(padX, piY, innerW, 1f), BaseContent.WhiteTex);
            GUI.color = vanillaPanel ? new Color(0.85f, 0.85f, 0.85f) : new Color(TextLight.r, TextLight.g, TextLight.b, 0.75f);
            Text.Font   = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(0, piY, W, pawnInfoH),
                $"{pawn.LabelShortCap}  —  Lv.{comp.Level} [{comp.GetRankString()}]");

            GUI.EndGroup();
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ──────────────────── Tree Area ────────────────────

        private void DrawTreeArea(Rect rect)
        {
            GUI.BeginGroup(rect);
            Rect local = new Rect(0, 0, rect.width, rect.height);

            HandlePanZoom(local);

            if (currentTree == null)
            {
                GUI.color = TextMuted;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(local, "Isekai_NoTreeData".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.EndGroup();
                return;
            }

            Vector2 center = local.center;

            // Draw magic circles (behind everything, clipped to tree area)
            DrawMagicCircles(local);

            // Draw connections (behind nodes)
            DrawAllConnections(center, local);

            // Draw nodes and detect hover/clicks
            hoveredNodeId = null;
            DrawAllNodes(local, center);

            // Draw tooltip last (on top)
            if (hoveredNodeId != null)
            {
                var node = currentTree.GetNode(hoveredNodeId);
                if (node != null)
                    DrawNodeTooltip(local, node);
            }

            GUI.EndGroup();
        }

        /// <summary>Draws layered magic circle textures that rotate continuously.</summary>
        private void DrawMagicCircles(Rect treeRect)
        {
            var circleTex = LoadUncompressedTex("SkillTree/MagicCircle");
            if (circleTex == null) return;

            Vector2 screenCenter = treeRect.center + panOffset;
            float t = Time.realtimeSinceStartup;

            // Three layered circles: large slow, medium, small fast (opposite dir)
            DrawRotatingCircle(circleTex, screenCenter, 900f * zoom, t *  8f, 0.10f, treeRect);
            DrawRotatingCircle(circleTex, screenCenter, 500f * zoom, t * -14f, 0.14f, treeRect);
            DrawRotatingCircle(circleTex, screenCenter, 200f * zoom, t *  22f, 0.18f, treeRect);
        }

        /// <summary>Draws a single rotating magic circle texture centered on pivot.
        /// Uses GL rendering with Sutherland-Hodgman polygon clipping so the rotated
        /// quad is properly clipped to the tree container. GL bypasses the IMGUI
        /// GUIClip system which cannot clip rotated GUI.matrix draws correctly.</summary>
        private void DrawRotatingCircle(Texture2D tex, Vector2 pivot, float size, float angleDeg, float alpha, Rect clipRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            float half = size * 0.5f;

            // Quick AABB cull — rotated square's AABB is at most size * sqrt(2)
            float aabbHalf = half * 1.42f;
            Rect aabb = new Rect(pivot.x - aabbHalf, pivot.y - aabbHalf, aabbHalf * 2f, aabbHalf * 2f);
            if (!aabb.Overlaps(clipRect)) return;

            // Compute rotated quad corners in local GUI coordinates
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            // Unrotated corner offsets from pivot: TL, TR, BR, BL
            // UV mapping for GL (v=0 bottom, v=1 top) with Y-down LoadPixelMatrix:
            //   TL corner (small y = screen top) → v=1 (texture top)
            //   BL corner (large y = screen bottom) → v=0 (texture bottom)
            var quad = new List<VertUV>(4);
            float[] ox = { -half, half, half, -half };
            float[] oy = { -half, -half, half, half };
            float[] ux = { 0f, 1f, 1f, 0f };
            float[] uy = { 1f, 1f, 0f, 0f };
            for (int i = 0; i < 4; i++)
            {
                quad.Add(new VertUV
                {
                    pos = new Vector2(
                        pivot.x + ox[i] * cos - oy[i] * sin,
                        pivot.y + ox[i] * sin + oy[i] * cos),
                    uv = new Vector2(ux[i], uy[i])
                });
            }

            // Clip rotated polygon to the visible area
            var clipped = ClipPolygonToRect(quad, clipRect);
            if (clipped.Count < 3) return;

            // Convert local GUI positions to screen pixels
            float uiScale = Prefs.UIScale;
            Vector2 gOff = GUIUtility.GUIToScreenPoint(Vector2.zero);

            EnsureGLTexMat();
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            _glTexMat.mainTexture = tex;
            _glTexMat.SetPass(0);

            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, alpha));

            // Fan triangulation from first vertex
            for (int i = 1; i < clipped.Count - 1; i++)
            {
                Vector2 sp0 = (clipped[0].pos + gOff) * uiScale;
                Vector2 sp1 = (clipped[i].pos + gOff) * uiScale;
                Vector2 sp2 = (clipped[i + 1].pos + gOff) * uiScale;

                GL.TexCoord2(clipped[0].uv.x, clipped[0].uv.y);
                GL.Vertex3(sp0.x, sp0.y, 0);
                GL.TexCoord2(clipped[i].uv.x, clipped[i].uv.y);
                GL.Vertex3(sp1.x, sp1.y, 0);
                GL.TexCoord2(clipped[i + 1].uv.x, clipped[i + 1].uv.y);
                GL.Vertex3(sp2.x, sp2.y, 0);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Draws all node connections using GL rendering.
        /// Each line is drawn as a quad with computed perpendicular corners,
        /// avoiding GUI.matrix rotation which causes IMGUI GUIClip to incorrectly
        /// cull the unrotated draw rect. All lines are batched into one GL call.
        /// </summary>
        private void DrawAllConnections(Vector2 center, Rect clipRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            bool vanilla = !IsekaiLevelingSettings.UseIsekaiUI;

            float uiScale = Prefs.UIScale;
            Vector2 gOff = GUIUtility.GUIToScreenPoint(Vector2.zero);

            EnsureGLTexMat();
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            _glTexMat.mainTexture = BaseContent.WhiteTex;
            _glTexMat.SetPass(0);

            GL.Begin(GL.QUADS);

            foreach (var (nodeA, nodeB) in currentTree.GetConnectionPairs())
            {
                Vector2 posA = GridToScreen(nodeA.x, nodeA.y, center);
                Vector2 posB = GridToScreen(nodeB.x, nodeB.y, center);

                // Clip line endpoints to the visible area in local GUI coords
                Vector2 drawA = posA, drawB = posB;
                if (!ClipLineToRect(ref drawA, ref drawB, clipRect))
                    continue;

                bool aUnlocked = comp?.passiveTree?.IsUnlocked(nodeA.nodeId) ?? false;
                bool bUnlocked = comp?.passiveTree?.IsUnlocked(nodeB.nodeId) ?? false;
                bool both = aUnlocked && bUnlocked;
                bool oneAvail = !both && (
                    (aUnlocked && (comp?.passiveTree?.CanUnlock(nodeB.nodeId, comp?.Pawn) ?? false)) ||
                    (bUnlocked && (comp?.passiveTree?.CanUnlock(nodeA.nodeId, comp?.Pawn) ?? false)));

                Color lineCol;
                float w;
                if (vanilla)
                {
                    lineCol = both ? new Color(0.4f, 0.8f, 0.4f, 0.9f)
                            : oneAvail ? new Color(0.7f, 0.7f, 0.7f, 0.7f)
                            : new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    w = (both ? 2.5f : 1.5f) * Mathf.Clamp(zoom, 0.5f, 1.5f);
                }
                else
                {
                    lineCol = both ? ConnUnlocked : oneAvail ? ConnAvailable : ConnLocked;
                    w = (both ? 3f : 2f) * Mathf.Clamp(zoom, 0.5f, 1.5f);
                }

                // Compute perpendicular offset for line width
                float dx = drawB.x - drawA.x;
                float dy = drawB.y - drawA.y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 0.5f) continue;

                float px = (-dy / len) * w * 0.5f;
                float py = (dx / len) * w * 0.5f;

                // Four corners of the thick line, converted to screen pixels
                Vector2 c0 = (new Vector2(drawA.x + px, drawA.y + py) + gOff) * uiScale;
                Vector2 c1 = (new Vector2(drawB.x + px, drawB.y + py) + gOff) * uiScale;
                Vector2 c2 = (new Vector2(drawB.x - px, drawB.y - py) + gOff) * uiScale;
                Vector2 c3 = (new Vector2(drawA.x - px, drawA.y - py) + gOff) * uiScale;

                GL.Color(lineCol);
                GL.TexCoord2(0, 0);
                GL.Vertex3(c0.x, c0.y, 0);
                GL.Vertex3(c1.x, c1.y, 0);
                GL.Vertex3(c2.x, c2.y, 0);
                GL.Vertex3(c3.x, c3.y, 0);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void DrawAllNodes(Rect local, Vector2 center)
        {
            foreach (var node in currentTree.nodes)
            {
                Vector2 pos = GridToScreen(node.x, node.y, center);
                float size = GetNodeSize(node.nodeType);
                Rect nodeRect = new Rect(pos.x - size / 2f, pos.y - size / 2f, size, size);

                // Cull off-screen
                if (nodeRect.xMax < -size || nodeRect.x > local.width + size ||
                    nodeRect.yMax < -size || nodeRect.y > local.height + size)
                    continue;

                bool unlocked = comp?.passiveTree?.IsUnlocked(node.nodeId) ?? false;
                bool canUnlock = !unlocked && (comp?.passiveTree?.CanUnlock(node.nodeId, comp?.Pawn) ?? false);
                bool hovered = Mouse.IsOver(nodeRect);

                if (hovered) hoveredNodeId = node.nodeId;

                // Smooth per-node hover
                if (!_nodeHoverT.ContainsKey(node.nodeId)) _nodeHoverT[node.nodeId] = 0f;
                _nodeHoverT[node.nodeId] = Mathf.MoveTowards(
                    _nodeHoverT[node.nodeId], hovered ? 1f : 0f, Time.unscaledDeltaTime * 9f);

                // Unlock burst ring — expands outward for 0.65 s (themed mode only)
                if (IsekaiLevelingSettings.UseIsekaiUI && _nodeUnlockT.TryGetValue(node.nodeId, out float uTime))
                {
                    float age = Time.realtimeSinceStartup - uTime;
                    if (age < 0.65f)
                    {
                        float prog   = age / 0.65f;
                        float expand = prog * size * 1.8f;
                        float alpha  = (1f - prog) * 0.85f;
                        Rect ringRect = new Rect(
                            pos.x - size * 0.5f - expand,
                            pos.y - size * 0.5f - expand,
                            size + expand * 2f, size + expand * 2f);
                        GUI.color = new Color(TextGold.r, TextGold.g, TextGold.b, alpha);
                        var shapeTex = GetCachedTex(NodeShapePath(node.nodeType, true));
                        GUI.DrawTexture(ringRect, shapeTex ?? BaseContent.WhiteTex);
                    }
                }

                DrawNode(nodeRect, node, unlocked, canUnlock, hovered);

                // Left-click: select node
                if (hovered && Event.current.type == EventType.MouseUp && Event.current.button == 0)
                {
                    selectedNodeId = node.nodeId;
                    Event.current.Use();
                }
                
                // Double-click on MouseDown: unlock node (clickCount is only reliable on MouseDown)
                if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && canUnlock)
                {
                    if (comp?.passiveTree?.Unlock(node.nodeId, comp?.Pawn) == true)
                    {
                        _nodeUnlockT[node.nodeId] = Time.realtimeSinceStartup;
                        SoundDefOf.Lesson_Activated.PlayOneShotOnCamera();
                    }
                    Event.current.Use();
                }
            }
        }

        // ──────────────────── Node Rendering ────────────────────

        private void DrawNode(Rect rect, PassiveNodeRecord node, bool unlocked, bool canUnlock, bool hovered)
        {
            // ── Spawn + hover scale ──
            float spawnV = _nodeSpawnT.TryGetValue(node.nodeId, out var sv) ? sv : 0f;
            if (spawnV <= 0f) return; // fully hidden — skip draw to avoid degenerate 0-scale matrix

            float hoverV     = _nodeHoverT.TryGetValue(node.nodeId, out var hv) ? hv : 0f;
            float totalScale = EaseOutCubic(spawnV) * (1f + EaseOutCubic(hoverV) * 0.10f);

            // Scale the rect directly around its center
            if (Mathf.Abs(totalScale - 1f) > 0.001f)
            {
                float sw = rect.width  * totalScale;
                float sh = rect.height * totalScale;
                rect = new Rect(rect.center.x - sw * 0.5f, rect.center.y - sh * 0.5f, sw, sh);
            }

            bool isSelected = selectedNodeId == node.nodeId;

            // ── Vanilla research-tree style ──
            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                DrawNodeVanilla(rect, node, unlocked, canUnlock, hovered, isSelected);
                return;
            }
            Color fill, border;
            GetNodeColors(node.nodeType, unlocked, canUnlock, out fill, out border);

            if (hovered || isSelected)
            {
                fill = Color.Lerp(fill, Color.white, isSelected ? 0.2f : 0.15f);
                border = Color.Lerp(border, Color.white, isSelected ? 0.3f : 0.2f);
            }

            // ── Selection glow ring ──
            if (isSelected)
            {
                Color selGlow = new Color(TextGold.r, TextGold.g, TextGold.b, 0.35f);
                Rect selRect = new Rect(rect.x - 6f, rect.y - 6f, rect.width + 12f, rect.height + 12f);
                GUI.color = selGlow;
                Texture2D shapeTex = GetCachedTex(NodeShapePath(node.nodeType, true));
                GUI.DrawTexture(selRect, shapeTex ?? BaseContent.WhiteTex);
            }

            // ── Glow for unlocked / available nodes ──
            if (unlocked || canUnlock)
            {
                Color glow = unlocked ? GlowGold : new Color(GlowGold.r, GlowGold.g, GlowGold.b, 0.1f);
                Rect glowRect = new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f);
                GUI.color = glow;
                Texture2D shapeTex = GetCachedTex(NodeShapePath(node.nodeType, true));
                GUI.DrawTexture(glowRect, shapeTex ?? BaseContent.WhiteTex);
            }

            // ── Node shape (custom texture) ──
            bool isActive = unlocked || canUnlock;
            Texture2D nodeShape = GetCachedTex(NodeShapePath(node.nodeType, isActive));
            if (nodeShape != null)
            {
                // Draw tinted node shape
                GUI.color = fill;
                GUI.DrawTexture(rect, nodeShape);
                // Slight border/highlight pass at slightly larger size
                GUI.color = new Color(border.r, border.g, border.b, 0.4f);
                Rect borderRect = new Rect(rect.x - 1.5f, rect.y - 1.5f, rect.width + 3f, rect.height + 3f);
                GUI.DrawTexture(borderRect, nodeShape);
                // Re-draw the fill on top so border is just the rim
                GUI.color = fill;
                GUI.DrawTexture(rect, nodeShape);
            }
            else
            {
                // Fallback: programmatic circle if textures are missing
                DrawCircleNode(rect, fill, border, node.nodeType == PassiveNodeType.Notable ? 2.5f : 1.5f);
            }

            // ── Icon overlay inside the node ──
            if (!string.IsNullOrEmpty(node.icon) && zoom >= 0.4f)
            {
                Texture2D iconTex = GetCachedTex(node.icon);
                if (iconTex != null)
                {
                    // Icon fills about 55% of the node
                    float iconScale = 0.55f;
                    float iconSize = rect.width * iconScale;
                    Rect iconRect = new Rect(
                        rect.center.x - iconSize / 2f,
                        rect.center.y - iconSize / 2f,
                        iconSize, iconSize);

                    // Tint icon: white for unlocked, muted for locked
                    GUI.color = unlocked ? new Color(TextLight.r, TextLight.g, TextLight.b, 0.9f)
                              : canUnlock ? new Color(TextGold.r, TextGold.g, TextGold.b, 0.7f)
                              : new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.5f);
                    GUI.DrawTexture(iconRect, iconTex);
                }
            }

            // Label for Notable/Keystone (if zoomed in enough)
            if (node.nodeType != PassiveNodeType.Minor && zoom >= 0.55f)
            {
                GUI.color = unlocked ? TextLight : canUnlock ? TextGold : TextMuted;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                string lbl = node.label.Length > 16 ? node.label.Substring(0, 14) + ".." : node.label;
                Rect lblRect = new Rect(rect.center.x - 65f, rect.yMax + 2f, 130f, 20f);
                Widgets.Label(lblRect, lbl);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Cost pips for keystones
            if (node.nodeType == PassiveNodeType.Keystone && node.cost > 1 && zoom >= 0.45f)
            {
                float pipSz = 5f * zoom;
                float totalW = node.cost * pipSz + (node.cost - 1) * 2f;
                float px = rect.center.x - totalW / 2f;
                float py = rect.yMax - pipSz - 2f * zoom;
                GUI.color = unlocked ? TextGold : TextMuted;
                for (int p = 0; p < node.cost; p++)
                {
                    GUI.DrawTexture(new Rect(px + p * (pipSz + 2f), py, pipSz, pipSz), BaseContent.WhiteTex);
                }
            }
        }

        /// <summary>Vanilla research-tree style node: simple box with border, icon, and label.</summary>
        private void DrawNodeVanilla(Rect rect, PassiveNodeRecord node, bool unlocked, bool canUnlock, bool hovered, bool isSelected)
        {
            // Widen non-minor nodes into research-card shaped rects
            if (node.nodeType != PassiveNodeType.Minor)
            {
                float cardW = rect.width * 1.8f;
                float cardH = rect.height * 1.1f;
                rect = new Rect(rect.center.x - cardW * 0.5f, rect.center.y - cardH * 0.5f, cardW, cardH);
            }

            // Background
            Color bgCol = unlocked ? new Color(0.22f, 0.32f, 0.22f)
                        : canUnlock ? new Color(0.22f, 0.22f, 0.22f)
                        : new Color(0.15f, 0.15f, 0.15f);
            GUI.color = bgCol;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);

            // Border
            Color borderCol = unlocked ? new Color(0.4f, 0.8f, 0.4f)
                            : canUnlock ? new Color(0.7f, 0.7f, 0.7f)
                            : new Color(0.35f, 0.35f, 0.35f);
            if (isSelected) borderCol = Color.white;
            else if (hovered) borderCol = Color.Lerp(borderCol, Color.white, 0.3f);

            int borderThick = node.nodeType == PassiveNodeType.Keystone ? 2 : 1;
            GUI.color = borderCol;
            Widgets.DrawBox(rect, borderThick);

            // Selection highlight
            if (isSelected)
                Widgets.DrawHighlight(rect);

            // Icon
            if (!string.IsNullOrEmpty(node.icon) && zoom >= 0.4f)
            {
                Texture2D iconTex = GetCachedTex(node.icon);
                if (iconTex != null)
                {
                    float iconScale = node.nodeType == PassiveNodeType.Minor ? 0.55f : 0.45f;
                    float iconSize = Mathf.Min(rect.width, rect.height) * iconScale;
                    float iconY = node.nodeType == PassiveNodeType.Minor
                        ? rect.center.y - iconSize * 0.5f
                        : rect.y + 3f;
                    Rect iconRect = new Rect(rect.center.x - iconSize * 0.5f, iconY, iconSize, iconSize);
                    GUI.color = unlocked ? new Color(0.9f, 1f, 0.9f) : canUnlock ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                    GUI.DrawTexture(iconRect, iconTex);
                }
            }

            // Label for Notable/Keystone/Start
            if (node.nodeType != PassiveNodeType.Minor && zoom >= 0.45f)
            {
                GUI.color = unlocked ? new Color(0.7f, 1f, 0.7f) : canUnlock ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerCenter;
                string lbl = node.label.Length > 14 ? node.label.Substring(0, 12) + ".." : node.label;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 2f), lbl);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Cost pips for keystones
            if (node.nodeType == PassiveNodeType.Keystone && node.cost > 1 && zoom >= 0.45f)
            {
                float pipSz = 4f * zoom;
                float totalW = node.cost * pipSz + (node.cost - 1) * 2f;
                float px = rect.center.x - totalW / 2f;
                float py = rect.yMax + 2f;
                GUI.color = unlocked ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
                for (int p = 0; p < node.cost; p++)
                    GUI.DrawTexture(new Rect(px + p * (pipSz + 2f), py, pipSz, pipSz), BaseContent.WhiteTex);
            }

            GUI.color = Color.white;
        }

        private void GetNodeColors(PassiveNodeType type, bool unlocked, bool canUnlock, out Color fill, out Color border)
        {
            switch (type)
            {
                case PassiveNodeType.Keystone:
                    fill = unlocked ? NodeKeystoneUnlocked : NodeKeystoneLocked;
                    border = unlocked ? TextLight :
                             canUnlock ? TextGold : TextMuted;
                    break;
                case PassiveNodeType.Notable:
                    fill = unlocked ? NodeNotableUnlocked : NodeNotableLocked;
                    border = unlocked ? TextGold :
                             canUnlock ? BorderBrown : TextMuted;
                    break;
                case PassiveNodeType.Start:
                    fill = unlocked ? new Color(TextGold.r * 0.5f, TextGold.g * 0.5f, TextGold.b * 0.5f) : SurfaceDark;
                    border = unlocked ? TextGold : canUnlock ? BorderBrown : TextMuted;
                    break;
                default:
                    fill = unlocked ? NodeMinorUnlocked : NodeMinorLocked;
                    border = unlocked ? TextGold :
                             canUnlock ? BorderBrown : new Color(SurfaceDark.r, SurfaceDark.g, SurfaceDark.b, 0.8f);
                    break;
            }
        }

        private void DrawCircleNode(Rect rect, Color fill, Color border, float borderW)
        {
            Texture2D tex = IsekaiUIHelper.CircleTexture ?? BaseContent.WhiteTex;
            // Border
            GUI.color = border;
            Rect bRect = new Rect(rect.x - borderW, rect.y - borderW, rect.width + borderW * 2, rect.height + borderW * 2);
            GUI.DrawTexture(bRect, tex);
            // Fill
            GUI.color = fill;
            GUI.DrawTexture(rect, tex);
        }

        private void DrawDiamond(Rect rect, Color fill, Color border)
        {
            Vector2 c = rect.center;
            float inner = rect.width * 0.38f;
            Matrix4x4 saved = GUI.matrix;

            // Use absolute pivot (local + all group offsets) so the rotation matrix
            // cancels the GUIClip offset, preventing displacement at non-1x UIScale.
            Vector2 gOff = GUIUtility.GUIToScreenPoint(Vector2.zero);
            Vector3 absC = new Vector3(c.x + gOff.x, c.y + gOff.y, 0f);
            GUI.matrix = saved
                * Matrix4x4.TRS(absC, Quaternion.Euler(0f, 0f, 45f), Vector3.one)
                * Matrix4x4.Translate(-absC);

            Rect innerR = new Rect(c.x - inner, c.y - inner, inner * 2, inner * 2);
            Rect outerR = new Rect(innerR.x - 2.5f, innerR.y - 2.5f, innerR.width + 5f, innerR.height + 5f);

            GUI.color = border;
            GUI.DrawTexture(outerR, BaseContent.WhiteTex);
            GUI.color = fill;
            GUI.DrawTexture(innerR, BaseContent.WhiteTex);

            GUI.matrix = saved;
        }

        // ──────────────────── Tooltip ────────────────────

        private void DrawNodeTooltip(Rect treeRect, PassiveNodeRecord node)
        {
            // Bumped from 260 to 320 to give long localized bonus labels (German,
            // Russian, French) more horizontal room before they wrap. With dynamic
            // label heights below, wrapping is correct, but extra width also reduces
            // visual clutter on 4K where users complained labels were getting clipped.
            float tipW = 320f;
            // Pre-calculate content height
            float contentH = EstimateTooltipHeight(node);
            float tipH = contentH + 20f;

            Vector2 mouse = Event.current.mousePosition;
            float x = mouse.x + 18f;
            float y = mouse.y + 18f;
            if (x + tipW > treeRect.xMax) x = mouse.x - tipW - 18f;
            if (y + tipH > treeRect.yMax) y = treeRect.yMax - tipH;
            if (x < 0) x = 4f;
            if (y < 0) y = 4f;

            Rect tipRect = new Rect(x, y, tipW, tipH);

            // Background
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                GUI.color = new Color(BgDark.r, BgDark.g, BgDark.b, 0.95f);
                GUI.DrawTexture(tipRect, BaseContent.WhiteTex);

                // Border color by node type
                Color bc = node.nodeType == PassiveNodeType.Keystone ? TextLight :
                           node.nodeType == PassiveNodeType.Notable ? TextGold : BorderBrown;
                GUI.color = bc;
                Widgets.DrawBox(tipRect, 1);
            }
            else
            {
                Widgets.DrawWindowBackground(tipRect);
                GUI.color = Color.white;
                Widgets.DrawBox(tipRect, 1);
            }

            float cx = tipRect.x + 10f;
            float cy = tipRect.y + 8f;
            float cw = tipW - 20f;

            // Every label below uses Text.CalcHeight to size its rect. Without this,
            // long localized strings (German/Russian/French) wrap to multiple lines
            // but the fixed-height rect crops everything past line 1 — which is the
            // "text clipped across the entire UI" bug users have reported on 4K.

            // Node name
            bool vanillaTip = !IsekaiLevelingSettings.UseIsekaiUI;
            GUI.color = vanillaTip ? Color.white
                      : node.nodeType == PassiveNodeType.Keystone ? TextLight
                      : node.nodeType == PassiveNodeType.Notable ? TextGold : TextLight;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            float nameH = Text.CalcHeight(node.label ?? "", cw);
            Widgets.Label(new Rect(cx, cy, cw, nameH), node.label);
            cy += nameH;

            // Type label
            GUI.color = vanillaTip ? new Color(0.7f, 0.7f, 0.7f) : TextMuted;
            Text.Font = GameFont.Tiny;
            string typeLbl = GetNodeTypeLabel(node.nodeType);
            float typeH = Text.CalcHeight(typeLbl ?? "", cw);
            Widgets.Label(new Rect(cx, cy, cw, typeH), typeLbl);
            cy += typeH;

            // Description (already used CalcHeight — left intact)
            if (!string.IsNullOrEmpty(node.description))
            {
                cy += 4f;
                GUI.color = vanillaTip ? new Color(0.9f, 0.85f, 0.7f) : TextGold;
                float dh = Text.CalcHeight(node.description, cw);
                Widgets.Label(new Rect(cx, cy, cw, dh), node.description);
                cy += dh + 4f;
            }

            // Bonuses — each label CalcHeight'd so wrap doesn't truncate
            if (node.bonuses != null && node.bonuses.Count > 0)
            {
                cy += 4f;
                foreach (var bonus in node.bonuses)
                {
                    bool isInverted = IsInvertedStat(bonus.bonusType);
                    bool neg = isInverted ? bonus.value > 0 : bonus.value < 0;
                    GUI.color = neg ? new Color(0.9f, 0.4f, 0.35f) : new Color(0.45f, 0.85f, 0.45f);
                    string label = FormatBonus(bonus);
                    float bonusH = Text.CalcHeight(label ?? "", cw);
                    Widgets.Label(new Rect(cx, cy, cw, bonusH), label);
                    cy += bonusH;
                }
            }

            // Cost / allocated status
            bool isUnlocked = comp?.passiveTree?.IsUnlocked(node.nodeId) ?? false;
            if (node.nodeType != PassiveNodeType.Start || isUnlocked)
            {
                cy += 4f;
                string statusLbl = isUnlocked
                    ? "Isekai_NodeAllocated".Translate().ToString()
                    : (node.cost > 0 ? "Isekai_NodeCost".Translate(node.cost.ToString()).ToString() : null);
                if (!string.IsNullOrEmpty(statusLbl))
                {
                    GUI.color = isUnlocked
                        ? new Color(0.45f, 0.85f, 0.45f)
                        : (vanillaTip ? new Color(0.6f, 0.6f, 0.6f) : TextMuted);
                    float statusH = Text.CalcHeight(statusLbl, cw);
                    Widgets.Label(new Rect(cx, cy, cw, statusH), statusLbl);
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private float EstimateTooltipHeight(PassiveNodeRecord node)
        {
            // Mirror DrawNodeTooltip's per-label CalcHeight so the tooltip box
            // matches the rendered content exactly. Width here MUST match the
            // tipW - 20 used by the renderer (320 - 20 = 300).
            const float cw = 300f;
            GameFont prevFont = Text.Font;
            float h = 0f;

            // Name (Small)
            Text.Font = GameFont.Small;
            h += Text.CalcHeight(node.label ?? "", cw);

            // Type (Tiny) and everything after uses Tiny
            Text.Font = GameFont.Tiny;
            h += Text.CalcHeight(GetNodeTypeLabel(node.nodeType) ?? "", cw);

            if (!string.IsNullOrEmpty(node.description))
                h += 4f + Text.CalcHeight(node.description, cw) + 4f;

            if (node.bonuses != null && node.bonuses.Count > 0)
            {
                h += 4f;
                foreach (var bonus in node.bonuses)
                    h += Text.CalcHeight(FormatBonus(bonus) ?? "", cw);
            }

            // Cost / allocated status (single short label most of the time)
            h += 4f + Text.CalcHeight("Isekai_NodeCost".Translate("999").ToString(), cw);

            Text.Font = prevFont;
            return h;
        }

        private string GetNodeTypeLabel(PassiveNodeType type)
        {
            switch (type)
            {
                case PassiveNodeType.Keystone: return "Isekai_NodeKeystone".Translate();
                case PassiveNodeType.Notable: return "Isekai_NodeNotable".Translate();
                case PassiveNodeType.Start: return "Isekai_NodeStart".Translate();
                default: return "Isekai_NodeMinor".Translate();
            }
        }

        private string FormatBonus(PassiveBonus bonus)
        {
            // Class Gimmick Tier gets special formatting: "Class Passive +1 Tier" or "{GimmickName} +1 Tier"
            if (bonus.bonusType == PassiveBonusType.ClassGimmickTier)
            {
                int tiers = Mathf.RoundToInt(bonus.value);
                string gimmickLabel = !string.IsNullOrEmpty(currentTree?.classGimmickName)
                    ? currentTree.classGimmickName
                    : "Isekai_Effect_ClassPassive".Translate();
                return $"{gimmickLabel} +{tiers} " + "Isekai_Tier".Translate();
            }

            float multiplier = IsekaiMod.Settings?.ConstellationBonusMultiplier ?? 1f;
            float display = bonus.value * multiplier * 100f;
            // Inverted stats: positive XML value = reduction, so flip the display sign
            if (IsInvertedStat(bonus.bonusType)) display = -display;
            string sign = display >= 0 ? "+" : "";
            string name = GetBonusTypeName(bonus.bonusType);
            return $"{sign}{display:F0}% {name}";
        }

        private string GetBonusTypeName(PassiveBonusType type)
        {
            switch (type)
            {
                case PassiveBonusType.MeleeDamage: return "Isekai_Effect_MeleeDamage".Translate();
                case PassiveBonusType.CarryCapacity: return "Isekai_Effect_CarryCapacity".Translate();
                case PassiveBonusType.MaxHealth: return "Isekai_Effect_MaxHealth".Translate();
                case PassiveBonusType.HealthRegen: return "Isekai_Effect_HealthRegen".Translate();
                case PassiveBonusType.DamageReduction: return "Isekai_Effect_DamageReduction".Translate();
                case PassiveBonusType.SharpArmor: return "Isekai_Effect_SharpArmor".Translate();
                case PassiveBonusType.BluntArmor: return "Isekai_Effect_BluntArmor".Translate();
                case PassiveBonusType.HeatArmor: return "Isekai_Effect_HeatArmor".Translate();
                case PassiveBonusType.PainThreshold: return "Isekai_Effect_PainThreshold".Translate();
                case PassiveBonusType.RestRate: return "Isekai_Effect_RestRate".Translate();
                case PassiveBonusType.ToxicResist: return "Isekai_Effect_ToxicResist".Translate();
                case PassiveBonusType.ImmunityGain: return "Isekai_Effect_ImmunityGain".Translate();
                case PassiveBonusType.MoveSpeed: return "Isekai_Effect_MoveSpeed".Translate();
                case PassiveBonusType.MeleeDodge: return "Isekai_Effect_DodgeChance".Translate();
                case PassiveBonusType.ShootingAccuracy: return "Isekai_Effect_ShootingAccuracy".Translate();
                case PassiveBonusType.MeleeHitChance: return "Isekai_Effect_MeleeHit".Translate();
                case PassiveBonusType.AimingDelay: return "Isekai_Effect_AimingDelay".Translate();
                case PassiveBonusType.WorkSpeed: return "Isekai_Effect_WorkSpeed".Translate();
                case PassiveBonusType.ResearchSpeed: return "Isekai_Effect_ResearchSpeed".Translate();
                case PassiveBonusType.LearningSpeed: return "Isekai_Effect_LearningRate".Translate();
                case PassiveBonusType.MentalBreakThreshold: return "Isekai_Effect_MentalBreakResist".Translate();
                case PassiveBonusType.MeditationFocus: return "Isekai_Effect_MeditationGain".Translate();
                case PassiveBonusType.TendQuality: return "Isekai_Effect_TendQuality".Translate();
                case PassiveBonusType.SurgerySuccess: return "Isekai_Effect_SurgerySuccess".Translate();
                case PassiveBonusType.TrainAnimal: return "Isekai_Effect_TrainAnimal".Translate();
                case PassiveBonusType.GatherYield: return "Isekai_Effect_GatherYield".Translate();
                case PassiveBonusType.SocialImpact: return "Isekai_Effect_SocialImpact".Translate();
                case PassiveBonusType.Negotiation: return "Isekai_Effect_Negotiation".Translate();
                case PassiveBonusType.TradePrice: return "Isekai_Effect_TradePriceImprove".Translate();
                case PassiveBonusType.Taming: return "Isekai_Effect_Taming".Translate();
                case PassiveBonusType.ArrestSuccess: return "Isekai_Effect_ArrestSuccess".Translate();
                case PassiveBonusType.ClassGimmickTier: return "Isekai_Effect_GimmickTier".Translate();
                // RimWorld of Magic bonus types
                case PassiveBonusType.RoM_MaxMana: return "Isekai_Effect_RoM_MaxMana".Translate();
                case PassiveBonusType.RoM_ManaRegen: return "Isekai_Effect_RoM_ManaRegen".Translate();
                case PassiveBonusType.RoM_MagicDamage: return "Isekai_Effect_RoM_MagicDamage".Translate();
                case PassiveBonusType.RoM_MagicCooldown: return "Isekai_Effect_RoM_MagicCooldown".Translate();
                case PassiveBonusType.RoM_ManaCost: return "Isekai_Effect_RoM_ManaCost".Translate();
                case PassiveBonusType.RoM_MaxStamina: return "Isekai_Effect_RoM_MaxStamina".Translate();
                case PassiveBonusType.RoM_StaminaRegen: return "Isekai_Effect_RoM_StaminaRegen".Translate();
                case PassiveBonusType.RoM_MightDamage: return "Isekai_Effect_RoM_MightDamage".Translate();
                case PassiveBonusType.RoM_MightCooldown: return "Isekai_Effect_RoM_MightCooldown".Translate();
                case PassiveBonusType.RoM_StaminaCost: return "Isekai_Effect_RoM_StaminaCost".Translate();
                case PassiveBonusType.RoM_ChiMax: return "Isekai_Effect_RoM_ChiMax".Translate();
                case PassiveBonusType.RoM_PsionicMax: return "Isekai_Effect_RoM_PsionicMax".Translate();
                case PassiveBonusType.RoM_SummonDuration: return "Isekai_Effect_RoM_SummonDuration".Translate();
                case PassiveBonusType.RoM_BuffDuration: return "Isekai_Effect_RoM_BuffDuration".Translate();
                default: return type.ToString();
            }
        }

        /// <summary>
        /// Returns true for stats where a positive XML value represents a reduction
        /// (i.e. the stat is "lower is better"). These stats need inverted sign/color
        /// in the UI so that positive bonuses show as green reductions.
        /// </summary>
        private bool IsInvertedStat(PassiveBonusType type)
        {
            return type == PassiveBonusType.AimingDelay
                || type == PassiveBonusType.RoM_MagicCooldown
                || type == PassiveBonusType.RoM_MightCooldown
                || type == PassiveBonusType.RoM_ManaCost
                || type == PassiveBonusType.RoM_StaminaCost;
        }

        // ──────────────────── Detail Panel ────────────────────

        private Vector2 detailScrollPos;

        private void DrawDetailPanel(Rect rect)
        {
            // ── Background ──
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                var panelTex = LoadUncompressedTex("SkillTree/DetailPanel");
                GUI.color = new Color(1f, 1f, 1f, _winOpenT);
                if (panelTex != null)
                    GUI.DrawTexture(rect, panelTex, ScaleMode.StretchToFill);
                else
                {
                    GUI.color = new Color(BgDark.r, BgDark.g, BgDark.b, 0.95f);
                    GUI.DrawTexture(rect, BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }
            else
            {
                Widgets.DrawMenuSection(rect);
            }

            float pad = rect.width * 0.05f;   // ~5% side padding
            float cw  = rect.width - pad * 2f;

            // ── Passive points header ──
            int available = comp?.passiveTree?.availablePoints ?? 0;
            float ptH  = rect.height * 0.075f;
            float ptY  = rect.y + rect.height * 0.025f;
            Rect ptRect = new Rect(rect.x + pad, ptY, cw, ptH);
            Color ptBase = available > 0 ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 1f, 1f, 0.4f);
            GUI.color = _ptFlashT > 0f ? Color.Lerp(ptBase, Color.white, EaseOutCubic(_ptFlashT) * 0.75f) : ptBase;
            Text.Font   = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(ptRect, "Isekai_PassivePoints".Translate(available.ToString()));
            GUI.color = IsekaiLevelingSettings.UseIsekaiUI
                ? new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.30f)
                : new Color(0.5f, 0.5f, 0.5f, 0.30f);
            GUI.DrawTexture(new Rect(rect.x + pad, ptRect.yMax + 2f, cw, 1f), BaseContent.WhiteTex);

            float topPad = ptH + rect.height * 0.04f;
            Rect viewRect = new Rect(rect.x + pad, rect.y + topPad, cw, rect.height - topPad - pad);

            // Estimate content height to know if we need scrolling
            float contentH = EstimateDetailPanelHeight(cw);
            Rect scrollContent = new Rect(0, 0, cw - 16f, contentH);
            bool needScroll = contentH > viewRect.height;

            Widgets.BeginScrollView(viewRect, ref detailScrollPos, scrollContent, needScroll);
            float cy = 0f;
            float innerW = needScroll ? cw - 32f : cw;

            // ── Section 1: Selected Node ──
            cy = DrawDetailSection_SelectedNode(cy, innerW);

            // ── Section 2: Tree Summary ──
            cy = DrawDetailSection_TreeSummary(cy, innerW);

            // ── Section 3: Class Gimmick ──
            cy = DrawDetailSection_ClassGimmick(cy, innerW);

            Widgets.EndScrollView();

            // Content fade-in overlay — covers detail content and fades away when _detailT > 0
            if (_detailT < 0.999f && IsekaiLevelingSettings.UseIsekaiUI)
            {
                GUI.color = new Color(BgDark.r, BgDark.g, BgDark.b, 1f - EaseOutCubic(_detailT));
                GUI.DrawTexture(viewRect, BaseContent.WhiteTex);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private float EstimateDetailPanelHeight(float width)
        {
            float h = 0f;
            // Selected node section
            if (selectedNodeId != null && currentTree?.GetNode(selectedNodeId) != null)
            {
                var node = currentTree.GetNode(selectedNodeId);
                h += 24f + 18f; // title + type
                if (!string.IsNullOrEmpty(node.description))
                    h += Text.CalcHeight(node.description, width - 16f) + 12f;
                if (node.bonuses != null && node.bonuses.Count > 0)
                {
                    GameFont prevFont = Text.Font;
                    Text.Font = GameFont.Tiny;
                    float bonusW = width - 16f - 6f;
                    foreach (var bonus in node.bonuses)
                        h += Mathf.Max(18f, Text.CalcHeight("• " + FormatBonus(bonus), bonusW));
                    h += 6f;
                    Text.Font = prevFont;
                }
                h += 24f + 16f; // cost/status + spacing
                h += 40f; // unlock button
            }
            else
            {
                h += 60f; // "Select a node" message
            }

            h += 12f; // separator

            // Tree summary: title + 6 lines
            h += 30f + 120f + 12f;

            // Class gimmick
            if (currentTree != null && currentTree.classGimmick != ClassGimmickType.None)
            {
                h += 30f + 22f;
                if (!string.IsNullOrEmpty(currentTree.classGimmickDescription))
                    h += Text.CalcHeight(currentTree.classGimmickDescription, width - 16f) + 8f;
                // Live value line
                h += 24f;
            }

            return h + 20f; // padding
        }

        private float DrawDetailSection_SelectedNode(float cy, float w)
        {
            PassiveNodeRecord node = null;
            if (selectedNodeId != null)
                node = currentTree?.GetNode(selectedNodeId);

            if (node == null)
            {
                // No node selected prompt
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(0, cy + 16f, w, 28f), "Isekai_DetailSelectNode".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return cy + 60f;
            }

            bool unlocked = comp?.passiveTree?.IsUnlocked(node.nodeId) ?? false;
            bool canUnlock = !unlocked && (comp?.passiveTree?.CanUnlock(node.nodeId, comp?.Pawn) ?? false);

            // Node name with color based on type
            Color nameColor = node.nodeType == PassiveNodeType.Keystone ? new Color(1f, 0.65f, 0.5f) :
                              node.nodeType == PassiveNodeType.Notable ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 1f, 1f, 0.95f);
            GUI.color = nameColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, cy, w, 24f), node.label);
            cy += 22f;

            // Type tag with colored indicator
            string typeLbl = GetNodeTypeLabel(node.nodeType);
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, cy, w, 18f), typeLbl);
            cy += 18f;

            // Separator line
            GUI.color = IsekaiLevelingSettings.UseIsekaiUI
                ? new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.4f)
                : new Color(0.5f, 0.5f, 0.5f, 0.4f);
            GUI.DrawTexture(new Rect(0, cy, w, 1f), BaseContent.WhiteTex);
            cy += 6f;

            // Description
            if (!string.IsNullOrEmpty(node.description))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Text.Font = GameFont.Tiny;
                float dh = Text.CalcHeight(node.description, w);
                Widgets.Label(new Rect(0, cy, w, dh), node.description);
                cy += dh + 8f;
            }

            // Bonuses list with icons-style formatting.
            // CalcHeight per label so long localized bonus names don't get clipped
            // to one line in the side panel (matches the tooltip fix).
            if (node.bonuses != null && node.bonuses.Count > 0)
            {
                Text.Font = GameFont.Tiny;
                float bonusW = w - 6f;
                foreach (var bonus in node.bonuses)
                {
                    bool neg = bonus.value < 0;
                    GUI.color = neg ? new Color(1f, 0.5f, 0.45f) : new Color(0.55f, 1f, 0.55f);
                    string label = "• " + FormatBonus(bonus);
                    float bonusH = Mathf.Max(18f, Text.CalcHeight(label, bonusW));
                    Widgets.Label(new Rect(6f, cy, bonusW, bonusH), label);
                    cy += bonusH;
                }
                cy += 6f;
            }

            // Status badge + Unlock button
            if (unlocked)
            {
                GUI.color = new Color(0.55f, 1f, 0.55f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0, cy, w, 20f), "✓ " + "Isekai_NodeAllocated".Translate());
                cy += 24f;
            }
            else
            {
                // Cost info and requirements
                GUI.color = canUnlock ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 1f, 1f, 0.4f);
                Text.Font = GameFont.Tiny;
                
                // Determine if this is a cross-tree Start node or first class selection
                bool isCrossTreeStart = node.nodeType == PassiveNodeType.Start
                    && !string.IsNullOrEmpty(comp?.passiveTree?.assignedTree)
                    && currentTree != null
                    && currentTree.treeClass != comp.passiveTree.assignedTree;
                bool isFirstClassSelection = node.nodeType == PassiveNodeType.Start
                    && string.IsNullOrEmpty(comp?.passiveTree?.assignedTree);
                
                if (node.cost > 0)
                {
                    string costText = "Isekai_NodeCost".Translate(node.cost.ToString());
                    if (!canUnlock)
                    {
                        // Determine the reason the node can't be unlocked
                        string reason;
                        if ((comp?.passiveTree?.availablePoints ?? 0) < node.cost)
                            reason = "Isekai_DetailNotEnoughPoints".Translate();
                        else if (isFirstClassSelection && (comp?.Level ?? 0) < 11)
                            reason = "Isekai_RequiresDRank".Translate();
                        else if (isCrossTreeStart && !PassiveTreeTracker.PawnHasStarFragment(comp?.Pawn))
                            reason = "Isekai_StarFragment_RequiresFragment".Translate();
                        else
                            reason = "Isekai_DetailNotConnected".Translate();
                        costText += " — " + reason;
                    }
                    Widgets.Label(new Rect(0, cy, w, 20f), costText);
                    cy += 22f;
                }
                
                // Always show special requirements for Start nodes
                if (isFirstClassSelection)
                {
                    GUI.color = (comp?.Level ?? 0) >= 11 ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.5f, 0.45f);
                    Widgets.Label(new Rect(0, cy, w, 18f), "Isekai_RequiresDRank".Translate());
                    cy += 20f;
                }
                else if (isCrossTreeStart)
                {
                    bool hasFragment = PassiveTreeTracker.PawnHasStarFragment(comp?.Pawn);
                    GUI.color = hasFragment ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.5f, 0.45f);
                    int absorbed = comp?.passiveTree?.starFragmentsAbsorbed ?? 0;
                    string fragText = hasFragment
                        ? "Isekai_StarFragment_HasFragment".Translate() + $" ({absorbed})"
                        : "Isekai_StarFragment_RequiresFragment".Translate();
                    Widgets.Label(new Rect(0, cy, w, 18f), fragText);
                    cy += 20f;
                }

                // Unlock button
                cy += 4f;
                Rect btnRect = new Rect(0, cy, w, 32f);
                if (DrawStyledButton(btnRect, "Isekai_UnlockNode".Translate(), "unlock", canUnlock))
                {
                    if (comp?.passiveTree?.Unlock(node.nodeId, comp?.Pawn) == true)
                    {
                        _nodeUnlockT[node.nodeId] = Time.realtimeSinceStartup;
                        SoundDefOf.Lesson_Activated.PlayOneShotOnCamera();
                    }
                }
                cy += 36f;
            }

            // Separator
            cy += 4f;
            GUI.color = IsekaiLevelingSettings.UseIsekaiUI
                ? new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.3f)
                : new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUI.DrawTexture(new Rect(0, cy, w, 1f), BaseContent.WhiteTex);
            cy += 8f;

            return cy;
        }

        private float DrawDetailSection_TreeSummary(float cy, float w)
        {
            // Section header
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, cy, w, 24f), "Isekai_DetailTreeSummary".Translate());
            cy += 24f;

            var tracker = comp?.passiveTree;
            int totalNodes = currentTree?.nodes?.Count ?? 0;
            int unlockedCount = (tracker != null && currentTree != null) ? tracker.UnlockedCountInTree(currentTree) : 0;
            int keystoneCount = 0;
            int unlockedKeystones = 0;
            if (currentTree?.nodes != null)
            {
                foreach (var nd in currentTree.nodes)
                {
                    if (nd.nodeType == PassiveNodeType.Keystone)
                    {
                        keystoneCount++;
                        if (tracker?.IsUnlocked(nd.nodeId) == true) unlockedKeystones++;
                    }
                }
            }

            // Stats
            Text.Font = GameFont.Tiny;
            DrawSummaryLine(ref cy, w, "Isekai_DetailNodes".Translate(), $"{unlockedCount} / {totalNodes}");
            DrawSummaryLine(ref cy, w, "Isekai_DetailKeystones".Translate(), $"{unlockedKeystones} / {keystoneCount}");
            DrawSummaryLine(ref cy, w, "Isekai_DetailPointsSpent".Translate(), (tracker?.TotalAllocatedPoints ?? 0).ToString());
            DrawSummaryLine(ref cy, w, "Isekai_DetailPointsAvail".Translate(), (tracker?.availablePoints ?? 0).ToString(),
                (tracker?.availablePoints ?? 0) > 0 ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 1f, 1f, 0.4f));

            // Top bonuses from the tree
            cy += 6f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(0, cy, w, 18f), "Isekai_DetailActiveBonuses".Translate());
            cy += 18f;

            if (tracker != null && !string.IsNullOrEmpty(tracker.assignedTree))
            {
                // Collect all non-zero bonuses
                var activeBonuses = new List<KeyValuePair<PassiveBonusType, float>>();
                foreach (PassiveBonusType bt in System.Enum.GetValues(typeof(PassiveBonusType)))
                {
                    if (bt == PassiveBonusType.ClassGimmickTier) continue; // Internal — shown via gimmick section
                    float val = tracker.GetTotalBonus(bt);
                    if (val != 0f)
                        activeBonuses.Add(new KeyValuePair<PassiveBonusType, float>(bt, val));
                }

                if (activeBonuses.Count == 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(6f, cy, w - 6f, 18f), "Isekai_DetailNoBonuses".Translate());
                    cy += 18f;
                }
                else
                {
                    foreach (var kvp in activeBonuses)
                    {
                        bool isInverted = IsInvertedStat(kvp.Key);
                        bool neg = isInverted ? kvp.Value > 0 : kvp.Value < 0;
                        GUI.color = neg ? new Color(1f, 0.5f, 0.45f) : new Color(0.55f, 1f, 0.55f);
                        Text.Font = GameFont.Tiny;
                        float displayVal = kvp.Value * 100f;
                        if (isInverted) displayVal = -displayVal;
                        string sign = displayVal >= 0 ? "+" : "";
                        string name = GetBonusTypeName(kvp.Key);
                        Widgets.Label(new Rect(6f, cy, w - 6f, 18f), $"  {sign}{displayVal:F0}% {name}");
                        cy += 17f;
                    }
                }
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(6f, cy, w - 6f, 18f), "Isekai_DetailNoBonuses".Translate());
                cy += 18f;
            }

            cy += 8f;
            GUI.color = new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.3f);
            GUI.DrawTexture(new Rect(0, cy, w, 1f), BaseContent.WhiteTex);
            cy += 8f;

            return cy;
        }

        private void DrawSummaryLine(ref float cy, float w, string label, string value, Color? valueColor = null)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(0, cy, w * 0.6f, 18f), label);
            GUI.color = valueColor ?? new Color(1f, 1f, 1f, 0.85f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(w * 0.5f, cy, w * 0.5f, 18f), value);
            Text.Anchor = TextAnchor.UpperLeft;
            cy += 18f;
        }

        private float DrawDetailSection_ClassGimmick(float cy, float w)
        {
            if (currentTree == null || currentTree.classGimmick == ClassGimmickType.None)
                return cy;

            // Section header with special color
            Color gimmickColor = new Color(1f, 0.6f, 0.3f); // Warm orange
            GUI.color = gimmickColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            string gimmickName = !string.IsNullOrEmpty(currentTree.classGimmickName)
                ? currentTree.classGimmickName
                : currentTree.classGimmick.ToString();
            Widgets.Label(new Rect(0, cy, w, 24f), "⚔ " + gimmickName);
            cy += 24f;

            // "Class Passive" sub-label
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, cy, w, 16f), "Isekai_DetailClassPassive".Translate());
            cy += 18f;

            // Description
            if (!string.IsNullOrEmpty(currentTree.classGimmickDescription))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Text.Font = GameFont.Tiny;
                float dh = Text.CalcHeight(currentTree.classGimmickDescription, w);
                Widgets.Label(new Rect(0, cy, w, dh), currentTree.classGimmickDescription);
                cy += dh + 8f;
            }

            // Live status for active gimmicks
            bool hasTree = comp?.passiveTree?.HasEnteredTree(currentTree) == true;

            if (hasTree && currentTree.classGimmick == ClassGimmickType.WrathOfTheFallen)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    // No gimmick nodes unlocked yet
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Warlord's Path");
                    cy += 22f;
                }
                else
                {
                    // Show tier badge
                    GUI.color = new Color(1f, 0.6f, 0.3f);
                    Text.Font = GameFont.Tiny;
                    string[] thresholds = { "", "50%", "50%", "50%", "50%" };
                    string[] maxBonuses = { "", "25%", "35%", "50%", "75%" };
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Below {thresholds[tier]} HP → up to +{maxBonuses[tier]}");
                    cy += 20f;

                    float wrathBonus = comp.passiveTree.CalcWrathOfTheFallen(pawn);
                    float hpPct = pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;

                    if (wrathBonus > 0f)
                    {
                        GUI.color = new Color(1f, 0.4f, 0.3f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE — HP: {hpPct * 100f:F0}% → +{wrathBonus * 100f:F0}% Melee Damage");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"Inactive — HP: {hpPct * 100f:F0}% (triggers below {thresholds[tier]})");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.ArcaneOverflow)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Archmage's Path");
                    cy += 22f;
                }
                else
                {
                    string[] arcanePsyThresholds = { "", "50%", "40%", "30%", "20%" };
                    string[] arcaneMaxBonuses    = { "", "20%", "35%", "55%", "80%" };
                    GUI.color = new Color(0.5f, 0.75f, 1.0f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Above {arcanePsyThresholds[tier]} psyfocus → up to +{arcaneMaxBonuses[tier]}");
                    cy += 20f;

                    float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(pawn, tier);
                    float psyfocus    = pawn?.psychicEntropy?.CurrentPsyfocus ?? 0f;

                    if (arcaneBonus > 0f)
                    {
                        GUI.color = new Color(0.4f, 0.75f, 1.0f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE — Psyfocus: {psyfocus * 100f:F0}% → +{arcaneBonus * 100f:F0}% Psychic Sensitivity");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"Inactive — Psyfocus: {psyfocus * 100f:F0}% (triggers above {arcanePsyThresholds[tier]})");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.DivineRetribution)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Sanctuary's Vow");
                    cy += 22f;
                }
                else
                {
                    string[] retCaps      = { "", "50", "75", "100", "150" };
                    string[] retMaxBonuses = { "", "15%", "30%", "45%", "60%" };
                    GUI.color = new Color(1.0f, 0.85f, 0.35f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Cap {retCaps[tier]} dmg absorbed → up to +{retMaxBonuses[tier]} on release");
                    cy += 20f;

                    int stored              = comp.passiveTree.retributionStoredDamage;
                    float retributionBonus  = comp.passiveTree.CalcDivineRetribution();

                    if (retributionBonus > 0f)
                    {
                        GUI.color = new Color(1.0f, 0.75f, 0.2f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"CHARGED: {stored}/{retCaps[tier]} dmg → +{retributionBonus * 100f:F0}% next strike");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No charge — take hits to build retribution, then strike");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.InnerCalm)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Ascetic Path");
                    cy += 22f;
                }
                else
                {
                    string[] calmCaps    = { "", "4h", "3h", "2h", "1h" };
                    string[] calmBonuses = { "", "20%", "35%", "55%", "80%" };
                    GUI.color = new Color(0.4f, 0.9f, 0.5f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Build calm over {calmCaps[tier]} → up to +{calmBonuses[tier]} Tend/Surgery");
                    cy += 20f;

                    float calmBonus   = comp.passiveTree.CalcInnerCalm();
                    int lastHit       = comp.passiveTree.lastHitTick;
                    bool neverHit     = lastHit < 0;
                    int calmTicks     = neverHit ? int.MaxValue / 2
                                                 : (Find.TickManager?.TicksGame ?? 0) - lastHit;

                    if (calmBonus >= 0.999f * (tier == 1 ? 0.20f : tier == 2 ? 0.35f : tier == 3 ? 0.55f : 0.80f))
                    {
                        GUI.color = new Color(0.35f, 1.0f, 0.45f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"FULL CALM: +{calmBonus * 100f:F0}% Tend/Surgery, +{calmBonus * 50f:F0}% Research");
                    }
                    else if (calmBonus > 0f)
                    {
                        GUI.color = new Color(0.4f, 0.85f, 0.5f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"Building: +{calmBonus * 100f:F0}% Tend/Surgery, +{calmBonus * 50f:F0}% Research");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "Just hit — calm resets and begins building again");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.PredatorFocus)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Hawkeye Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxStacks  = { "", "3", "4", "5", "7" };
                    string[] perStacks  = { "", "4%", "5%", "6%", "7%" };
                    string[] maxBonuses = { "", "12%", "20%", "30%", "49%" };
                    GUI.color = new Color(0.9f, 0.6f, 0.2f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Max {maxStacks[tier]} stacks, +{perStacks[tier]}/stack → up to +{maxBonuses[tier]}");
                    cy += 20f;

                    int stacks      = comp.passiveTree.huntMarkStacks;
                    float focusBonus = comp.passiveTree.CalcPredatorFocus();

                    if (focusBonus > 0f)
                    {
                        GUI.color = new Color(1.0f, 0.65f, 0.15f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"LOCKED ON: {stacks}/{maxStacks[tier]} stacks → +{focusBonus * 100f:F0}% Shooting Accuracy");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No target — land ranged hits to stack focus");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.CounterStrike)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Centerpoint Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxCharges   = { "", "3", "5", "7", "10" };
                    string[] perCharges   = { "", "7%", "8%", "9%", "10%" };
                    string[] maxBonuses   = { "", "21%", "40%", "63%", "100%" };
                    GUI.color = new Color(0.5f, 0.8f, 1.0f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Max {maxCharges[tier]} charges, +{perCharges[tier]}/charge → up to +{maxBonuses[tier]}");
                    cy += 20f;

                    int charges       = comp.passiveTree.counterStrikeCharges;
                    float counterBonus = comp.passiveTree.CalcCounterStrike();

                    if (counterBonus > 0f)
                    {
                        GUI.color = new Color(0.4f, 0.75f, 1.0f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"CHARGED: {charges}/{maxCharges[tier]} charges → +{counterBonus * 100f:F0}% Melee Damage");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No charges — dodge melee attacks to store counters");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.MasterworkInsight)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Mastercraft Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxBonuses = { "", "15%", "30%", "50%", "75%" };
                    GUI.color = new Color(0.9f, 0.7f, 0.2f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Crafting skill scales Work Speed, up to +{maxBonuses[tier]}");
                    cy += 20f;

                    Pawn pawn = comp.parent as Pawn;
                    float insightBonus = PassiveTreeTracker.CalcMasterworkInsight(pawn, tier);

                    if (insightBonus > 0f)
                    {
                        int craftLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                        GUI.color = new Color(1.0f, 0.8f, 0.2f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE: Crafting {craftLevel}/20 → +{insightBonus * 100f:F0}% Work Speed");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "Inactive — Crafting skill at 0");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.RallyingPresence)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Rally Path");
                    cy += 22f;
                }
                else
                {
                    string[] perColonist = { "", "2%", "2.5%", "3%", "3.5%" };
                    string[] maxCaps     = { "", "8", "12", "16", "20" };
                    GUI.color = new Color(0.7f, 0.5f, 1.0f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — +{perColonist[tier]}/colonist, cap {maxCaps[tier]} colonists");
                    cy += 20f;

                    Pawn pawn = comp.parent as Pawn;
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(pawn, tier);

                    if (rallyBonus > 0f)
                    {
                        int colonists = PassiveTreeTracker.GetCachedColonistCount(pawn?.Map);
                        GUI.color = new Color(0.8f, 0.6f, 1.0f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE: {colonists} colonists → +{rallyBonus * 100f:F0}% Social Impact");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No colonists on map — recruit allies to empower");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.UnyieldingSpirit)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Unyielding Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxBonuses = { "", "20%", "35%", "55%", "80%" };
                    GUI.color = new Color(0.3f, 0.8f, 0.7f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Low mood boosts Immunity & Rest, up to +{maxBonuses[tier]}");
                    cy += 20f;

                    Pawn pawn = comp.parent as Pawn;
                    float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(pawn, tier);

                    if (spiritBonus > 0f)
                    {
                        float mood = pawn?.needs?.mood?.CurLevel ?? 0.5f;
                        GUI.color = new Color(0.4f, 0.9f, 0.8f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE: Mood {mood * 100f:F0}% → +{spiritBonus * 100f:F0}% Immunity/Rest");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "Mood above 50% — bonus activates when mood drops");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.BloodFrenzy)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Frenzy Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxStacks  = { "", "3", "5", "7", "10" };
                    string[] perStacks  = { "", "5%", "6%", "7%", "8%" };
                    string[] maxBonuses = { "", "15%", "30%", "49%", "80%" };
                    GUI.color = new Color(0.85f, 0.25f, 0.25f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Max {maxStacks[tier]} stacks, +{perStacks[tier]}/kill → up to +{maxBonuses[tier]}");
                    cy += 20f;

                    int stacks = comp.passiveTree.frenzyStacks;
                    float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();

                    if (frenzyBonus > 0f)
                    {
                        GUI.color = new Color(1.0f, 0.3f, 0.2f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"FRENZIED: {stacks}/{maxStacks[tier]} stacks → +{frenzyBonus * 100f:F0}% Melee Damage & Move Speed");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No frenzy — kill enemies to stack blood rage");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.EurekaSynthesis)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Eureka Path");
                    cy += 22f;
                }
                else
                {
                    string[] maxStacks  = { "", "5", "8", "12", "16" };
                    string[] perStacks  = { "", "3%", "4%", "5%", "6%" };
                    string[] maxBonuses = { "", "15%", "32%", "60%", "96%" };
                    GUI.color = new Color(0.55f, 0.90f, 0.55f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — Max {maxStacks[tier]} stacks, +{perStacks[tier]}/tend → up to +{maxBonuses[tier]}");
                    cy += 20f;

                    int stacks = comp.passiveTree.eurekaInsightStacks;
                    float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();

                    if (eurekaBonus > 0f)
                    {
                        GUI.color = new Color(0.5f, 1.0f, 0.5f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"EUREKA: {stacks}/{maxStacks[tier]} stacks → +{eurekaBonus * 100f:F0}% TendQ, +{eurekaBonus * 50f:F0}% WorkSpeed");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No insight — tend patients to build stacks");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (hasTree && currentTree.classGimmick == ClassGimmickType.PackAlpha)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(currentTree.classGimmick);
                if (tier <= 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 20f),
                        "Not unlocked — allocate nodes in Pack Path");
                    cy += 22f;
                }
                else
                {
                    string[] perAnimal = { "", "3%", "4%", "5%", "6%" };
                    string[] maxCaps   = { "", "3", "5", "7", "10" };
                    GUI.color = new Color(0.80f, 0.60f, 0.30f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0, cy, w, 18f),
                        $"Tier {tier}/4 — +{perAnimal[tier]}/bonded animal, max {maxCaps[tier]} counted");
                    cy += 20f;

                    Pawn pawn = comp.parent as Pawn;
                    float packBonus = PassiveTreeTracker.CalcPackAlpha(pawn, tier);

                    if (packBonus > 0f)
                    {
                        int bondCount = 0;
                        if (pawn?.relations != null && pawn.Map != null)
                        {
                            foreach (var rel in pawn.relations.DirectRelations)
                            {
                                if (rel.def == PawnRelationDefOf.Bond && rel.otherPawn != null
                                    && !rel.otherPawn.Dead && rel.otherPawn.Map == pawn.Map)
                                    bondCount++;
                            }
                        }
                        GUI.color = new Color(0.9f, 0.7f, 0.3f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            $"ACTIVE: {bondCount} bonded animals → +{packBonus * 100f:F0}% Taming/Training/Gather");
                    }
                    else
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(0, cy, w, 20f),
                            "No bonded animals on map — bond animals to empower");
                    }
                    cy += 22f;
                }
                cy += 22f;
            }
            else if (!hasTree)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0, cy, w, 20f), "Isekai_DetailGimmickLocked".Translate());
                cy += 22f;
            }

            return cy;
        }

        // ──────────────────── Footer ────────────────────

        private void DrawFooter(Rect rect)
        {
            // Allocated count
            int allocated = comp?.passiveTree?.TotalAllocatedPoints ?? 0;
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.7f);
            Text.Font   = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.center.x - 80f, rect.y, 160f, rect.height),
                "Isekai_PassiveAllocated".Translate(allocated.ToString()));

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ──────────────────── Styled Button ────────────────────

        /// <summary>Custom styled button matching the mod's visual language: palette colors, hover glow, scale.</summary>
        private bool DrawStyledButton(Rect rect, string label, string id, bool enabled)
        {
            if (!IsekaiLevelingSettings.UseIsekaiUI)
                return Widgets.ButtonText(rect, label, true, true, enabled) && enabled;

            bool isOver = Mouse.IsOver(rect) && enabled;

            // Smooth hover animation (0→1)
            if (!_btnHover.ContainsKey(id)) _btnHover[id] = 0f;
            float target = isOver ? 1f : 0f;
            _btnHover[id] = Mathf.MoveTowards(_btnHover[id], target, Time.unscaledDeltaTime * 6f);
            float hover = _btnHover[id];

            Rect scaled = rect;

            // Background
            Color bgCol = enabled
                ? Color.Lerp(SurfaceDark, BorderBrown, hover * 0.5f)
                : new Color(BgDark.r, BgDark.g, BgDark.b, 0.5f);
            GUI.color = bgCol;
            GUI.DrawTexture(scaled, BaseContent.WhiteTex);

            // Border with hover glow
            Color borderCol = enabled
                ? Color.Lerp(BorderBrown, TextGold, hover * 0.6f)
                : new Color(BorderBrown.r, BorderBrown.g, BorderBrown.b, 0.3f);
            GUI.color = borderCol;
            Widgets.DrawBox(scaled, 1);

            // Label
            GUI.color = enabled
                ? Color.Lerp(TextLight, TextGold, hover)
                : new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.5f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(scaled, label);
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = Color.white;

            // Consume MouseDown so RimWorld doesn't start a drag-select
            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        // ──────────────────── Input ────────────────────

        private void HandlePanZoom(Rect area)
        {
            // Zoom with scroll wheel
            if (Mouse.IsOver(area) && Event.current.type == EventType.ScrollWheel)
            {
                float delta = -Event.current.delta.y * 0.06f;
                float oldZoom = zoom;
                zoom = Mathf.Clamp(zoom + delta, MIN_ZOOM, MAX_ZOOM);

                // Zoom toward mouse position
                if (zoom != oldZoom)
                {
                    Vector2 mouse = Event.current.mousePosition;
                    Vector2 focus = (mouse - area.center - panOffset) / oldZoom;
                    panOffset = mouse - area.center - focus * zoom;
                }

                Event.current.Use();
            }

            // Pan with right-click or middle-click drag
            if (Mouse.IsOver(area))
            {
                if (Event.current.type == EventType.MouseDown && (Event.current.button == 1 || Event.current.button == 2))
                {
                    isPanning = true;
                    panStart = Event.current.mousePosition;
                    panStartOffset = panOffset;
                    Event.current.Use();
                }
            }

            if (isPanning)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    panOffset = panStartOffset + (Event.current.mousePosition - panStart);
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    isPanning = false;
                    Event.current.Use();
                }
            }
        }

        // ──────────────────── Helpers ────────────────────

        private Vector2 GridToScreen(float gx, float gy, Vector2 screenCenter)
        {
            return new Vector2(
                screenCenter.x + gx * GRID_SCALE * zoom + panOffset.x,
                screenCenter.y - gy * GRID_SCALE * zoom + panOffset.y  // Y inverted
            );
        }

        private float GetNodeSize(PassiveNodeType type)
        {
            switch (type)
            {
                case PassiveNodeType.Keystone: return NODE_KEYSTONE * zoom;
                case PassiveNodeType.Notable: return NODE_NOTABLE * zoom;
                case PassiveNodeType.Start: return NODE_NOTABLE * zoom; // Start same as notable
                default: return NODE_MINOR * zoom;
            }
        }

        // ──────────────────── Clipping Utilities ────────────────────

        /// <summary>Vertex with position and UV, used for polygon clipping.</summary>
        private struct VertUV
        {
            public Vector2 pos;
            public Vector2 uv;
        }

        /// <summary>Sutherland-Hodgman polygon clipping against an axis-aligned rect.
        /// Clips the polygon against all four edges (left, right, top, bottom),
        /// interpolating UVs at new intersection vertices.</summary>
        private static List<VertUV> ClipPolygonToRect(List<VertUV> poly, Rect r)
        {
            if (poly.Count == 0) return poly;

            var input = poly;
            for (int edge = 0; edge < 4; edge++)
            {
                if (input.Count == 0) break;
                var output = new List<VertUV>(input.Count + 2);
                VertUV prev = input[input.Count - 1];

                for (int i = 0; i < input.Count; i++)
                {
                    VertUV cur = input[i];
                    bool prevIn = IsInsideEdge(prev.pos, edge, r);
                    bool curIn  = IsInsideEdge(cur.pos, edge, r);

                    if (curIn)
                    {
                        if (!prevIn) output.Add(IntersectEdge(prev, cur, edge, r));
                        output.Add(cur);
                    }
                    else if (prevIn)
                    {
                        output.Add(IntersectEdge(prev, cur, edge, r));
                    }

                    prev = cur;
                }
                input = output;
            }
            return input;
        }

        private static bool IsInsideEdge(Vector2 p, int edge, Rect r)
        {
            switch (edge)
            {
                case 0: return p.x >= r.xMin;
                case 1: return p.x <= r.xMax;
                case 2: return p.y >= r.yMin;
                case 3: return p.y <= r.yMax;
                default: return false;
            }
        }

        private static VertUV IntersectEdge(VertUV a, VertUV b, int edge, Rect r)
        {
            float t;
            switch (edge)
            {
                case 0: t = (r.xMin - a.pos.x) / (b.pos.x - a.pos.x); break;
                case 1: t = (r.xMax - a.pos.x) / (b.pos.x - a.pos.x); break;
                case 2: t = (r.yMin - a.pos.y) / (b.pos.y - a.pos.y); break;
                case 3: t = (r.yMax - a.pos.y) / (b.pos.y - a.pos.y); break;
                default: t = 0; break;
            }
            return new VertUV
            {
                pos = Vector2.Lerp(a.pos, b.pos, t),
                uv  = Vector2.Lerp(a.uv, b.uv, t)
            };
        }

        /// <summary>Cohen-Sutherland outcode for a point against a rect.</summary>
        private static int OutCode(Vector2 p, Rect r)
        {
            int c = 0;
            if (p.x < r.xMin) c |= 1;       // LEFT
            else if (p.x > r.xMax) c |= 2;  // RIGHT
            if (p.y < r.yMin) c |= 4;       // TOP (small y)
            else if (p.y > r.yMax) c |= 8;  // BOTTOM (large y)
            return c;
        }

        /// <summary>Cohen-Sutherland line clipping. Returns false if the line is entirely outside the rect.</summary>
        private static bool ClipLineToRect(ref Vector2 a, ref Vector2 b, Rect r)
        {
            int codeA = OutCode(a, r), codeB = OutCode(b, r);
            while (true)
            {
                if ((codeA | codeB) == 0) return true;   // both inside
                if ((codeA & codeB) != 0) return false;  // both outside same side
                int code = codeA != 0 ? codeA : codeB;
                float x, y, dx = b.x - a.x, dy = b.y - a.y;
                if      ((code & 8) != 0) { x = a.x + dx * (r.yMax - a.y) / dy; y = r.yMax; }
                else if ((code & 4) != 0) { x = a.x + dx * (r.yMin - a.y) / dy; y = r.yMin; }
                else if ((code & 2) != 0) { y = a.y + dy * (r.xMax - a.x) / dx; x = r.xMax; }
                else                      { y = a.y + dy * (r.xMin - a.x) / dx; x = r.xMin; }
                if (code == codeA) { a = new Vector2(x, y); codeA = OutCode(a, r); }
                else               { b = new Vector2(x, y); codeB = OutCode(b, r); }
            }
        }

    }
}
