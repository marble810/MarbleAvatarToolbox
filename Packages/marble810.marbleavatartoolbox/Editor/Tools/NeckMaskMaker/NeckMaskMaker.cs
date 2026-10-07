using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace marble810.MarbleAvatarToolbox.NeckMaskMaker
{
    /// <summary>
    /// 颈部遮罩生成器。
    ///
    /// 工作流：
    /// 1. 指定 Avatar 的 Body（完整身体）与 Body_base（去掉头部的身体基底）。
    /// 2. 在 Scene 中交互式点击 Body_base 的颈部边界线，自动吸附为整条循环线（Enter 确认）。
    /// 3. 按 UV 表面位置在 GPU 计算 Mask，参数拖动复用距离缓存。
    /// 4. 打开 Preview 在网格上以红色叠加显示 Mask 范围。
    /// 5. 勾选目标材质槽，按槽独立烘焙、预览与导出 PNG，不合并跨槽 UV。
    ///
    /// 窗口交互集中在本文件；NeckMaskGpuBaker 封装常驻 RT、采样缓存与 GPU 运算。
    /// </summary>
    public partial class NeckMaskMaker : EditorWindow
    {
        #region 常量

        private const string WindowTitle = "Neck Mask Maker";
        private const string MenuPath = "MarbleAvatarToolbox/Neck Mask Maker";
        private const string PreviewShaderName = "Hidden/MarbleAvatarToolbox/NeckMaskPreview";
        private const string EdgeShaderName = "Hidden/MarbleAvatarToolbox/NeckMaskEdge";
        private const string LastSaveDirectoryKey = "MarbleAvatarToolbox.NeckMaskMaker.LastSaveDirectory";

        /// <summary>贴图尺寸上限。GPU 贡献缓存与 RT 均随像素数增长，限制到 2048 控制内存峰值。</summary>
        private const int MaxTextureSize = 2048;

        /// <summary>鼠标拾取边的屏幕像素半径。</summary>
        private const float PickPixelRadius = 14f;

        /// <summary>几何回退方案中允许的最大方向偏折（点积阈值，0.2 ≈ 78°）。</summary>
        private const float GeometricContinuationMinDot = 0.2f;

        /// <summary>Scene 贴图预览面板的记忆键（尺寸沿用；位置保存为锚定结果，不再读取旧坐标）。</summary>
        private const string PreviewPanelXKey = "MarbleAvatarToolbox.NeckMaskMaker.PreviewPanelX";
        private const string PreviewPanelYKey = "MarbleAvatarToolbox.NeckMaskMaker.PreviewPanelY";
        private const string PreviewPanelSizeKey = "MarbleAvatarToolbox.NeckMaskMaker.PreviewPanelSize";

        /// <summary>Scene 贴图预览面板的默认位置、缩放与布局参数（单位：GUI 像素）。</summary>
        private const float SceneOverlayMargin = 12f;
        private const float DefaultTexturePreviewSize = 180f;
        private const float MinTexturePreviewSize = 64f;
        private const float MaxTexturePreviewSize = 384f;
        private const float TexturePreviewPadding = 8f;
        private const float TexturePreviewHeaderHeight = 20f;
        private const float TexturePreviewLabelHeight = 18f;
        private const float TexturePreviewSpacing = 6f;

        /// <summary>面板绘制深度：值越小越靠前，保证盖住 Scene 中其它 GUI。</summary>
        private const int TexturePreviewGuiDepth = -1000;

        /// <summary>Card 底板：每个功能区块用比窗口背景略暗的纯色底分块，并加 1 像素更深的描边，标题也画在卡片内。</summary>
        private const float CardHorizontalPadding = 8f;
        private const float CardSpacing = 6f;
        private const int CardBorderPixels = 1;
        private static readonly Color CardFillProSkin = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color CardBorderProSkin = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color CardFillLightSkin = new Color(0.76f, 0.76f, 0.76f, 1f);
        private static readonly Color CardBorderLightSkin = new Color(0.60f, 0.60f, 0.60f, 1f);

        /// <summary>Card 与窗口左右边缘之间的外边距。</summary>
        private const float CardOuterMargin = 8f;

        /// <summary>垂直滚动条宽度预留，用于让底部保存按钮与 Card 的右边缘对齐。</summary>
        private const float ScrollViewScrollbarAllowance = 16f;

        /// <summary>目标材质槽分栏时的列间距。</summary>
        private const float MaterialColumnSpacing = 6f;

        /// <summary>
        /// 窗口默认尺寸：宽度保证两列材质槽不挤压材质名，
        /// 高度按语言选择行、五个 Card 区块与底部保存栏的估算内容高度给出，打开时按需拉高。
        /// </summary>
        private const float MinWindowWidth = 480f;
        private const float DefaultWindowWidth = 540f;
        private const float DefaultWindowHeight = 748f;

        /// <summary>语言下拉框与标签宽度；两者分开绘制，避免宽度被 Unity 默认标签宽度挤掉。</summary>
        private const float LanguageLabelWidth = 80f;
        private const float LanguagePopupWidth = 150f;

        /// <summary>选择模式 HUD 配色：纯白文字 + 近乎不透明的深色底，避免亮场景透出后看不清。</summary>
        private static readonly Color SelectionHudTextColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SelectionHudBackgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        /// <summary>HUD 标签样式缓存，避免每帧重建 GUIStyle。</summary>
        private static GUIStyle _selectionHudTitleStyle;
        private static GUIStyle _selectionHudHintStyle;

        /// <summary>需要被预览面板拦截、不穿透到 Scene 相机操作的鼠标事件。</summary>
        private static readonly EventType[] TexturePreviewBlockedEvents =
        {
            EventType.MouseDown,
            EventType.MouseUp,
            EventType.MouseDrag,
            EventType.ContextClick,
            EventType.ScrollWheel,
        };

        #endregion

        #region 常量表

        private static readonly int[] TextureSizeOptions = { 256, 512, 1024, 2048 };

        /// <summary>衰减模式下拉框选项，与 <see cref="NeckMaskSampling.ModeNames"/> 顺序一致。</summary>
        private static readonly GUIContent[] SamplingModeOptions = BuildSamplingModeOptions();

        private static GUIContent[] BuildSamplingModeOptions()
        {
            var options = new GUIContent[NeckMaskSampling.ModeNames.Length];
            for (int i = 0; i < options.Length; i++) options[i] = new GUIContent(NeckMaskSampling.ModeNames[i]);
            return options;
        }

        #endregion

        #region 可序列化状态

        [SerializeField] private GameObject _body;
        [SerializeField] private GameObject _bodyBase;
        [SerializeField] private List<MaterialSlotSelection> _materialSlots = new List<MaterialSlotSelection>();

        [SerializeField] private float _maxDistance = 0.04f;

        /// <summary>距离衰减模式；默认 Smooth 就是旧版默认（Smoothness = 1）的外观。</summary>
        [SerializeField] private NeckMaskSamplingMode _samplingMode = NeckMaskSamplingMode.Smooth;

        /// <summary>Smooth 模式的曲率；与 Constant 模式的阈值各自保存，切换模式不丢值。</summary>
        [SerializeField] private float _smoothCurvature = NeckMaskSampling.DefaultCurvature;
        [SerializeField] private float _constantThreshold = NeckMaskSampling.DefaultThreshold;

        [SerializeField] private int _textureSize = 1024;
        [SerializeField] private int _dilation = 8;

        [SerializeField] private bool _preview;

        /// <summary>用户选择的颈部循环线。顶点索引指向 <see cref="_selectionSource"/> 的网格。</summary>
        [SerializeField] private List<LoopSelection> _loops = new List<LoopSelection>();

        /// <summary>循环线所属对象（body_base 优先，未设置时退回 body）。</summary>
        [SerializeField] private GameObject _selectionSource;

        #endregion

        #region 运行时状态

        [NonSerialized] private bool _isSelecting;
        [NonSerialized] private int _hoverEdge = -1;
        [NonSerialized] private List<int> _hoverLoop;

        [NonSerialized] private readonly Dictionary<int, MeshAnalysis> _analysisCache = new Dictionary<int, MeshAnalysis>();

        [NonSerialized] private bool _textureDirty = true;

        [NonSerialized] private LoopGeometry _loopGeometry;
        [NonSerialized] private bool _loopDirty = true;
        [NonSerialized] private int _gpuGeometryVersion = 1;
        [NonSerialized] private int _poseCheckGeneration;
        [NonSerialized] private int _geometryCheckGeneration = -1;
        [NonSerialized] private double _lastEditorTick;
        [NonSerialized] private NeckMaskGpuBaker _gpuBaker;
        [NonSerialized] private readonly Dictionary<long, string> _gpuTargetErrors = new Dictionary<long, string>();
        [NonSerialized] private readonly Dictionary<long, int> _gpuPreviewVersions = new Dictionary<long, int>();

        [NonSerialized] private Material _previewMaterial;
        [NonSerialized] private Material _edgeMaterial;
        [NonSerialized] private Mesh _edgeMesh;
        [NonSerialized] private readonly Dictionary<long, Mesh> _previewMeshes = new Dictionary<long, Mesh>();

        [NonSerialized] private bool _showTexturePreview;

        /// <summary>每个选中材质槽的独立预览贴图，不跨槽合并。</summary>
        [NonSerialized] private readonly List<TexturePreviewEntry> _texturePreviews = new List<TexturePreviewEntry>();
        [NonSerialized] private bool _texturePreviewsBuilt;
        [NonSerialized] private Rect _texturePreviewPanel;
        [NonSerialized] private float _texturePreviewImageSize = DefaultTexturePreviewSize;

        [NonSerialized] private string _statusMessage = string.Empty;
        [NonSerialized] private bool _statusIsError;
        [NonSerialized] private string _lastSaveDirectory = string.Empty;
        [NonSerialized] private Vector2 _scrollPosition;

        /// <summary>Card 底板样式与 3x3 纯色贴图（1 像素描边 + 可拉伸填充），随编辑器主题创建。</summary>
        [NonSerialized] private GUIStyle _cardStyle;
        [NonSerialized] private Texture2D _cardTexture;
        [NonSerialized] private bool _cardStyleForProSkin;

        // 复用的临时缓冲，避免每次刷新都产生大量 GC。
        [NonSerialized] private readonly List<Vector3> _vertexScratch = new List<Vector3>(4096);

        #endregion

        #region 生命周期

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<NeckMaskMaker>(WindowTitle);
            window.minSize = new Vector2(MinWindowWidth, 480f);
            window.FitDefaultSize();
        }

        /// <summary>窗口比默认尺寸更小时拉到默认尺寸，保证改动后的 Card 布局完整显示。</summary>
        private void FitDefaultSize()
        {
            var rect = position;
            float width = Mathf.Max(rect.width, DefaultWindowWidth);
            float height = Mathf.Max(rect.height, DefaultWindowHeight);
            if (Mathf.Approximately(width, rect.width) && Mathf.Approximately(height, rect.height)) return;
            position = new Rect(rect.x, rect.y, width, height);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            _lastSaveDirectory = EditorPrefs.GetString(LastSaveDirectoryKey, string.Empty);
            _texturePreviewImageSize = Mathf.Clamp(
                EditorPrefs.GetFloat(PreviewPanelSizeKey, DefaultTexturePreviewSize),
                MinTexturePreviewSize, MaxTexturePreviewSize);
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            MarkDirty();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;

            _isSelecting = false;
            _hoverEdge = -1;
            _hoverLoop = null;

            ReleaseRuntimeResources();
        }

        private void OnEditorUpdate()
        {
            if (!_preview && !_isSelecting && !_showTexturePreview) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastEditorTick < 1d / 60d) return;
            _lastEditorTick = now;
            _poseCheckGeneration++;
            SceneView.RepaintAll();
        }

        private void OnUndoRedo()
        {
            InvalidateAnalysis();
            MarkDirty();
        }

        private void ReleaseRuntimeResources()
        {
            ResetMaterialSlotResources();
            foreach (var pair in _analysisCache)
            {
                if (pair.Value != null && pair.Value.ownsMesh && pair.Value.mesh != null)
                {
                    DestroyImmediate(pair.Value.mesh);
                }
            }

            _analysisCache.Clear();

            foreach (var pair in _previewMeshes)
            {
                if (pair.Value != null) DestroyImmediate(pair.Value);
            }

            _previewMeshes.Clear();

            if (_previewMaterial != null)
            {
                DestroyImmediate(_previewMaterial);
                _previewMaterial = null;
            }

            ReleaseTexturePreviews();
            ReleaseCardStyle();

            if (_edgeMaterial != null) DestroyImmediate(_edgeMaterial);
            if (_edgeMesh != null) DestroyImmediate(_edgeMesh);
            _edgeMaterial = null;
            _edgeMesh = null;
        }

        #endregion

        #region 界面

        private void OnGUI()
        {
            DrawLanguageBar();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // 上下左右留出同样的外边距，Card 不贴窗口边缘。
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(CardOuterMargin);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Space(CardOuterMargin);
                    DrawTargetSection();
                    DrawMaterialSlotSection();
                    DrawLoopSection();
                    DrawMaskSettingsSection();
                    DrawPreviewSection();
                    DrawStatusSection();
                    GUILayout.Space(CardOuterMargin);
                }
                GUILayout.Space(CardOuterMargin);
            }

            EditorGUILayout.EndScrollView();

            DrawBottomBar();
        }

        /// <summary>
        /// 窗口顶部的语言选择行：不随内容滚动，切换后整窗文案立即按新语言重绘。
        /// 标签固定为 Language，不参与翻译；Tooltip 同时挂在标签与下拉框选项上。
        /// </summary>
        private void DrawLanguageBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(CardOuterMargin);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Space(CardOuterMargin);

                    string tooltip = NeckMaskLoc.T("由 Deepseek V4.1 Flash 翻译");
                    var options = new GUIContent[NeckMaskLoc.LanguageLabels.Length];
                    for (int i = 0; i < options.Length; i++)
                        options[i] = new GUIContent(NeckMaskLoc.LanguageLabels[i], tooltip);

                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent("Language", tooltip),
                            GUILayout.Width(LanguageLabelWidth));
                        int language = EditorGUILayout.Popup(
                            (int)NeckMaskLoc.Language, options, GUILayout.Width(LanguagePopupWidth));
                        if (EditorGUI.EndChangeCheck())
                        {
                            NeckMaskLoc.Language = (NeckMaskLanguage)Mathf.Clamp(language, 0, options.Length - 1);
                            // 状态与逐槽错误在生成时就已按旧语言格式化，切换后清掉，避免两种语言混排。
                            _statusMessage = string.Empty;
                            _gpuTargetErrors.Clear();
                            _textureDirty = true;
                            Repaint();
                            SceneView.RepaintAll();
                        }
                    }
                }
                GUILayout.Space(CardOuterMargin);
            }
        }

        /// <summary>底部固定栏：保存按钮不随内容滚动，左右边缘与 Card 一致。</summary>
        private void DrawBottomBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(CardOuterMargin);
                using (new EditorGUI.DisabledScope(GetSelectedMaterialSlots().Count == 0))
                {
                    if (GUILayout.Button(NeckMaskLoc.T("保存所选材质槽 Mask"), GUILayout.ExpandWidth(true), GUILayout.Height(28f)))
                    {
                        SaveMaskTexture();
                    }
                }
                GUILayout.Space(CardOuterMargin);
            }
        }

        /// <summary>开始一个带较暗 Card 底板的区块，标题画在卡片内；与 <see cref="EndCard"/> 成对使用。</summary>
        private void BeginCard(string title)
        {
            EnsureCardStyle();
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
        }

        private void EndCard()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(CardSpacing);
        }

        /// <summary>按当前编辑器主题创建 3x3 底板：外圈为描边色，中心为填充色，由 9 宫格拉伸成 1 像素边框。</summary>
        private void EnsureCardStyle()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_cardStyle != null && _cardStyleForProSkin == proSkin) return;

            ReleaseCardStyle();
            _cardStyleForProSkin = proSkin;
            Color fill = proSkin ? CardFillProSkin : CardFillLightSkin;
            Color border = proSkin ? CardBorderProSkin : CardBorderLightSkin;

            int size = CardBorderPixels * 2 + 1;
            _cardTexture = new Texture2D(size, size)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x < CardBorderPixels || y < CardBorderPixels
                    || x >= size - CardBorderPixels || y >= size - CardBorderPixels;
                _cardTexture.SetPixel(x, y, edge ? border : fill);
            }
            _cardTexture.Apply();

            // 以 helpBox 为基底：GUI.skin.box 的内置纹理不会被 normal.background 覆盖，画不出纯色底板。
            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                border = new RectOffset(CardBorderPixels, CardBorderPixels, CardBorderPixels, CardBorderPixels),
                margin = new RectOffset(),
                padding = new RectOffset(
                    (int)CardHorizontalPadding + CardBorderPixels,
                    (int)CardHorizontalPadding + CardBorderPixels,
                    6 + CardBorderPixels,
                    8 + CardBorderPixels),
            };
            _cardStyle.normal.background = _cardTexture;
        }

        private void ReleaseCardStyle()
        {
            if (_cardTexture != null) DestroyImmediate(_cardTexture);
            _cardTexture = null;
            _cardStyle = null;
        }

        private void DrawTargetSection()
        {
            BeginCard(NeckMaskLoc.T("目标对象"));

            EditorGUI.BeginChangeCheck();
            _body = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Body", NeckMaskLoc.T("完整身体（含头部）的网格对象。")), _body, typeof(GameObject), true);
            _bodyBase = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Body_base", NeckMaskLoc.T("去掉头部的身体基底，颈部循环线从这里拾取。")), _bodyBase, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (_selectionSource != null && _selectionSource != ResolveSelectionSource())
                {
                    _loops.Clear();
                    _selectionSource = null;
                    _loopGeometry = null;
                }

                InvalidateAnalysis();
                MarkDirty();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(NeckMaskLoc.T("刷新网格"), GUILayout.Width(110f), GUILayout.Height(20f)))
                {
                    InvalidateAnalysis();
                    MarkDirty();
                    SetStatus(NeckMaskLoc.T("已重新读取网格数据。"), false);
                }
            }

            if (_body == null && _bodyBase == null)
            {
                EditorGUILayout.HelpBox(NeckMaskLoc.T("请先指定 Body 与 Body_base。"), MessageType.Warning);
            }
            else
            {
                if (_bodyBase == null)
                {
                    EditorGUILayout.HelpBox(NeckMaskLoc.T("未指定 Body_base：颈部边界线将改为从 Body 拾取（通常需要内部循环选择）。"), MessageType.Info);
                }

                if (_body != null && GetRendererMesh(_body) == null)
                {
                    EditorGUILayout.HelpBox(NeckMaskLoc.T("Body 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。"), MessageType.Warning);
                }

                if (_bodyBase != null && GetRendererMesh(_bodyBase) == null)
                {
                    EditorGUILayout.HelpBox(NeckMaskLoc.T("Body_base 上找不到可用的 MeshRenderer / SkinnedMeshRenderer。"), MessageType.Warning);
                }
            }

            EndCard();
        }

        private void DrawLoopSection()
        {
            BeginCard(NeckMaskLoc.T("颈部边界线"));

            var source = ResolveSelectionSource();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(source == null))
                {
                    var label = _isSelecting ? NeckMaskLoc.T("结束选择（Enter）") : NeckMaskLoc.T("选择颈部边界线");
                    if (GUILayout.Button(label, GUILayout.Height(24f)))
                    {
                        if (_isSelecting) FinishSelection();
                        else BeginSelection();
                    }
                }

                using (new EditorGUI.DisabledScope(_loops.Count == 0))
                {
                    if (GUILayout.Button(NeckMaskLoc.T("清除"), GUILayout.Width(60f), GUILayout.Height(24f)))
                    {
                        _loops.Clear();
                        _selectionSource = null;
                        _loopGeometry = null;
                        MarkDirty();
                        Repaint();
                    }
                }
            }

            if (source == null)
            {
                EditorGUILayout.HelpBox(NeckMaskLoc.T("没有可拾取的对象，请先设置 Body_base 或 Body。"), MessageType.Warning);
                EndCard();
                return;
            }

            if (_loops.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    NeckMaskLoc.T("点击“选择颈部边界线”后，在 Scene 视图中点击 Body_base 颈部的任意一条边，" +
                        "工具会自动吸附为整条循环线。Shift + 左键可追加循环线，Enter 确认，Esc 取消。"),
                    MessageType.Info);
                EndCard();
                return;
            }

            int vertexCount = 0;
            for (int i = 0; i < _loops.Count; i++)
            {
                if (_loops[i] != null && _loops[i].vertices != null) vertexCount += _loops[i].vertices.Count;
            }

            EditorGUILayout.LabelField(NeckMaskLoc.F("已选择 {0} 条循环线，共 {1} 个顶点", _loops.Count, vertexCount), EditorStyles.miniLabel);
            EndCard();
        }

        private void DrawMaskSettingsSection()
        {
            BeginCard(NeckMaskLoc.T("Mask 参数"));

            EditorGUI.BeginChangeCheck();

            _maxDistance = EditorGUILayout.Slider(
                new GUIContent("Max Distance", NeckMaskLoc.T("边界线到 Mask 完全消失处的距离（单位：米，按世界空间计算）。")),
                _maxDistance, 0.0005f, 0.3f);

            int modeIndex = Mathf.Clamp((int)_samplingMode, 0, SamplingModeOptions.Length - 1);
            modeIndex = EditorGUILayout.Popup(
                new GUIContent("Sampling Mode", NeckMaskLoc.T("距离衰减方式：Linear 线性、Smooth 平滑（曲率可调）、Constant 按阈值二值化。")),
                modeIndex, SamplingModeOptions);
            _samplingMode = (NeckMaskSamplingMode)modeIndex;
            DrawSamplingParameter(modeIndex);

            EditorGUILayout.Space(4f);

            int sizeIndex = Mathf.Max(0, Array.IndexOf(TextureSizeOptions, _textureSize));
            sizeIndex = EditorGUILayout.Popup(new GUIContent("Texture Size", NeckMaskLoc.T("输出贴图尺寸。")), sizeIndex,
                Array.ConvertAll(TextureSizeOptions, option => new GUIContent(option + " x " + option)));
            _textureSize = TextureSizeOptions[Mathf.Clamp(sizeIndex, 0, TextureSizeOptions.Length - 1)];

            _dilation = EditorGUILayout.IntSlider(
                new GUIContent("Dilation", NeckMaskLoc.T("烘焙后向 UV 岛外扩张的像素数，用于避免采样时出现接缝。")),
                _dilation, 0, 32);

            if (EditorGUI.EndChangeCheck())
            {
                MarkParametersDirty();
            }

            EndCard();
        }

        /// <summary>只显示当前衰减模式自己的参数：Linear 没有参数，Smooth 是曲率，Constant 是阈值。</summary>
        private void DrawSamplingParameter(int modeIndex)
        {
            switch ((NeckMaskSamplingMode)modeIndex)
            {
                case NeckMaskSamplingMode.Smooth:
                    _smoothCurvature = EditorGUILayout.Slider(
                        new GUIContent("Curvature", NeckMaskLoc.T("平滑衰减的曲率。1 = 标准 smoothstep；越大 Mask 越饱满，越小越贴近边界线。")),
                        NeckMaskSampling.ClampParameter(NeckMaskSamplingMode.Smooth, _smoothCurvature),
                        NeckMaskSampling.MinCurvature, NeckMaskSampling.MaxCurvature);
                    break;
                case NeckMaskSamplingMode.Constant:
                    _constantThreshold = EditorGUILayout.Slider(
                        new GUIContent("Threshold", NeckMaskLoc.T("二值化阈值（0–1，相对 Max Distance）：归一化距离不超过该值时为 1，超过则为 0。")),
                        NeckMaskSampling.ClampParameter(NeckMaskSamplingMode.Constant, _constantThreshold), 0f, 1f);
                    break;
            }
        }

        /// <summary>当前衰减模式对应的参数值；Linear 没有参数，传 0 由 GPU 忽略。</summary>
        private float SamplingParameter =>
            _samplingMode == NeckMaskSamplingMode.Constant ? _constantThreshold
            : _samplingMode == NeckMaskSamplingMode.Smooth ? _smoothCurvature : 0f;

        /// <summary>两个预览功能统一放在同一区块：表面预览（Scene 红色叠加）与贴图预览（Scene 贴图面板）。</summary>
        private void DrawPreviewSection()
        {
            BeginCard(NeckMaskLoc.T("预览"));

            EditorGUI.BeginChangeCheck();
            _preview = EditorGUILayout.Toggle(
                new GUIContent(NeckMaskLoc.T("表面预览"), NeckMaskLoc.T("在 Scene 视图中以红色叠加显示所选材质槽的 Mask 范围。")), _preview);
            if (EditorGUI.EndChangeCheck())
            {
                if (_preview)
                {
                    InvalidateAnalysis();
                    MarkDirty();
                }
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            _showTexturePreview = EditorGUILayout.Toggle(
                new GUIContent(NeckMaskLoc.T("贴图预览"), NeckMaskLoc.T("在 Scene 视图中叠加显示每个材质槽的 Mask 贴图面板。")), _showTexturePreview);
            if (EditorGUI.EndChangeCheck())
            {
                if (_showTexturePreview)
                {
                    _textureDirty = true;
                }
                else
                {
                    ReleaseTexturePreviews();
                }

                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(!_preview && !_showTexturePreview))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(NeckMaskLoc.T("刷新预览"), GUILayout.Height(20f)))
                {
                    InvalidateAnalysis();
                    MarkDirty();
                    SceneView.RepaintAll();
                }
            }

            EndCard();
        }

        private void DrawStatusSection()
        {
            if (string.IsNullOrEmpty(_statusMessage)) return;
            EditorGUILayout.HelpBox(_statusMessage, _statusIsError ? MessageType.Error : MessageType.Info);
        }

        private void SetStatus(string message, bool isError)
        {
            _statusMessage = message;
            _statusIsError = isError;
            Repaint();
        }

        #endregion

        #region 目标解析

        private GameObject ResolveSelectionSource()
        {
            if (_bodyBase != null && GetRendererMesh(_bodyBase) != null) return _bodyBase;
            if (_body != null && GetRendererMesh(_body) != null) return _body;
            return null;
        }

        private static Renderer GetRenderer(GameObject go)
        {
            if (go == null) return null;

            var skinned = go.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null) return skinned;

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null) return meshRenderer;

            return go.GetComponentInChildren<Renderer>(true);
        }

        private static Mesh GetRendererMesh(GameObject go)
        {
            var renderer = GetRenderer(go);
            if (renderer == null) return null;

            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        #endregion

        #region Scene 交互

        private void BeginSelection()
        {
            var source = ResolveSelectionSource();
            if (source == null)
            {
                SetStatus(NeckMaskLoc.T("没有可拾取的对象。"), true);
                return;
            }

            if (_selectionSource != null && _selectionSource != source)
            {
                _loops.Clear();
            }

            _selectionSource = source;
            InvalidateAnalysis();
            EnsureAnalysis(source, true);

            _isSelecting = true;
            _hoverEdge = -1;
            _hoverLoop = null;
            SetStatus(NeckMaskLoc.T("左键点击颈部边界线，Enter 确认，Esc 取消。"), false);
            SceneView.RepaintAll();
        }

        private void FinishSelection()
        {
            _isSelecting = false;
            _hoverEdge = -1;
            _hoverLoop = null;

            if (_loops.Count == 0)
            {
                SetStatus(NeckMaskLoc.T("未选择任何边界线。"), true);
            }
            else
            {
                MarkDirty();
                SetStatus(NeckMaskLoc.F("已确认 {0} 条循环线。", _loops.Count), false);
            }

            SceneView.RepaintAll();
        }

        private void CancelSelection()
        {
            _isSelecting = false;
            _hoverEdge = -1;
            _hoverLoop = null;
            SetStatus(NeckMaskLoc.T("已取消选择。"), false);
            SceneView.RepaintAll();
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (sceneView == null) return;

            if (_isSelecting)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                // 选择逻辑先于面板绘制，必须让面板内的输入留给关闭按钮，避免误选网格。
                var current = Event.current;
                bool overTexturePreview = _showTexturePreview && _texturePreviewPanel.Contains(current.mousePosition)
                    && Array.IndexOf(TexturePreviewBlockedEvents, current.type) >= 0;
                if (!overTexturePreview) ProcessSelectionEvents();
            }

            if (_preview && Event.current.type == EventType.Repaint)
            {
                DrawMaskPreview();
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawLoopHandles(sceneView.camera);
            }

            if (_isSelecting)
            {
                DrawSelectionHud(sceneView);
            }

            if (_showTexturePreview)
            {
                DrawTexturePreviewOverlay(sceneView);
            }

        }

        private void ProcessSelectionEvents()
        {
            var current = Event.current;
            if (current == null) return;

            switch (current.type)
            {
                case EventType.MouseMove:
                    UpdateHoverEdge(current.mousePosition);
                    break;

                case EventType.MouseDown:
                    if (current.button == 0 && !current.alt)
                    {
                        if (TrySelectAt(current.mousePosition, current.shift))
                        {
                            current.Use();
                        }
                    }
                    break;

                case EventType.KeyDown:
                    if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                    {
                        FinishSelection();
                        current.Use();
                    }
                    else if (current.keyCode == KeyCode.Escape)
                    {
                        CancelSelection();
                        current.Use();
                    }
                    break;
            }
        }

        private void UpdateHoverEdge(Vector2 mousePosition)
        {
            var analysis = _selectionSource != null ? EnsureAnalysis(_selectionSource, false) : null;
            if (analysis == null)
            {
                _hoverEdge = -1;
                _hoverLoop = null;
                return;
            }

            _hoverEdge = PickEdge(analysis, mousePosition);
            _hoverLoop = _hoverEdge >= 0 ? BuildLoop(analysis, _hoverEdge) : null;
        }

        private bool TrySelectAt(Vector2 mousePosition, bool append)
        {
            var analysis = _selectionSource != null ? EnsureAnalysis(_selectionSource, false) : null;
            if (analysis == null) return false;

            int edge = PickEdge(analysis, mousePosition);
            if (edge < 0)
            {
                SetStatus(NeckMaskLoc.T("该位置附近没有找到边线，请点击颈部边界附近。"), true);
                return true;
            }

            var loop = BuildLoop(analysis, edge);
            if (loop == null || loop.Count < 2)
            {
                SetStatus(NeckMaskLoc.T("无法从该边线推导出循环线。"), true);
                return true;
            }

            if (!append) _loops.Clear();

            _loops.Add(new LoopSelection { vertices = loop });
            _loopGeometry = null;
            MarkDirty();

            SetStatus(NeckMaskLoc.F("已选中一条循环线（{0} 个顶点）。Shift + 左键可继续追加，Enter 确认。", loop.Count), false);
            return true;
        }

        /// <summary>Scene 的实际绘制区域（GUI 像素），不包含窗口工具栏。</summary>
        private static Vector2 GetSceneViewportSize(SceneView sceneView)
        {
            return sceneView.camera != null
                ? new Vector2(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight) / EditorGUIUtility.pixelsPerPoint
                : sceneView.position.size;
        }

        private static Rect GetSelectionHudRect(SceneView sceneView)
        {
            var viewport = GetSceneViewportSize(sceneView);
            float width = Mathf.Min(520f, Mathf.Max(0f, viewport.x - SceneOverlayMargin * 2f));
            return new Rect((viewport.x - width) * 0.5f,
                Mathf.Max(SceneOverlayMargin, viewport.y - SceneOverlayMargin - 58f), width, 58f);
        }

        private void DrawSelectionHud(SceneView sceneView)
        {
            Handles.BeginGUI();

            var boxRect = GetSelectionHudRect(sceneView);

            // 原生 Box 背景几乎全透明，亮场景会直接透出把文字糊掉：先铺近乎不透明的深色底，再叠 helpBox 边框。
            EditorGUI.DrawRect(boxRect, SelectionHudBackgroundColor);
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            var areaRect = new Rect(boxRect.x + 10f, boxRect.y + 6f, boxRect.width - 20f, boxRect.height - 12f);
            GUILayout.BeginArea(areaRect);
            GUILayout.Label(NeckMaskLoc.T("颈部边界线选择模式"), GetSelectionHudTitleStyle());
            GUILayout.Label(NeckMaskLoc.T("左键点击边线自动吸附整条循环 · Shift + 左键追加 · Enter 确认 · Esc 取消"), GetSelectionHudHintStyle());
            GUILayout.EndArea();

            Handles.EndGUI();
        }

        private static GUIStyle GetSelectionHudTitleStyle()
        {
            if (_selectionHudTitleStyle == null)
            {
                _selectionHudTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = SelectionHudTextColor },
                };
            }
            return _selectionHudTitleStyle;
        }

        private static GUIStyle GetSelectionHudHintStyle()
        {
            if (_selectionHudHintStyle == null)
            {
                _selectionHudHintStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = SelectionHudTextColor },
                    wordWrap = true,
                };
            }
            return _selectionHudHintStyle;
        }

        private void DrawLoopHandles(Camera camera)
        {
            var analysis = _selectionSource != null ? EnsureAnalysis(_selectionSource, false) : null;
            if (analysis == null || camera == null) return;
            RefreshPositions(analysis);

            // 世界顶点只投影一次，不经过 Handles 的 GUI / 相机矩阵转换。
            for (int i = 0; i < _loops.Count; i++)
            {
                DrawScreenSpaceLoop(camera, GetLoopWorldPoints(analysis, _loops[i]),
                    new Color(1f, 0.75f, 0.1f, 0.95f));
            }

            if (_hoverLoop != null && _hoverLoop.Count >= 2)
            {
                DrawScreenSpaceLoop(camera, GetLoopWorldPoints(analysis,
                    new LoopSelection { vertices = _hoverLoop }), new Color(0.25f, 0.9f, 1f, 0.95f));
            }
        }

        private void DrawScreenSpaceLoop(Camera camera, Vector3[] points, Color color)
        {
            if (points == null || points.Length < 2) return;
            if (_edgeMaterial == null)
            {
                var shader = Shader.Find(EdgeShaderName);
                if (shader == null) return;
                _edgeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_edgeMesh == null) _edgeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };

            // 选中与悬停采用相同宽度，避免鼠标经过时忽粗忽细。
            float halfWidth = 1.5f * EditorGUIUtility.pixelsPerPoint;
            if (!BuildScreenSpaceLineMesh(_edgeMesh, camera, points, halfWidth + 1f)) return;
            _edgeMaterial.SetColor("_Color", color);
            _edgeMaterial.SetFloat("_HalfWidth", halfWidth);
            _edgeMaterial.SetVector("_ViewportSize", new Vector4(camera.pixelWidth, camera.pixelHeight, 0f, 0f));

            bool wireframe = GL.wireframe;
            GL.PushMatrix();
            try
            {
                GL.wireframe = false;
                GL.modelview = camera.worldToCameraMatrix;
                GL.LoadProjectionMatrix(camera.projectionMatrix);
                if (_edgeMaterial.SetPass(0)) Graphics.DrawMeshNow(_edgeMesh, Matrix4x4.identity);
            }
            finally
            {
                GL.PopMatrix();
                GL.wireframe = wireframe;
            }
        }

        /// <summary>世界顶点为线的中心；只在裁剪空间中按像素向两侧扩展。</summary>
        private static bool BuildScreenSpaceLineMesh(Mesh mesh, Camera camera, Vector3[] points, float outerWidth)
        {
            int count = points.Length;
            bool closed = count > 2 && (points[0] - points[count - 1]).sqrMagnitude < 1e-12f;
            var screen = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 projected = camera.WorldToScreenPoint(points[i]);
                // 不把近裁剪面背后的点投影成跨越整个视口的巨大三角形。
                if (projected.z <= camera.nearClipPlane) return false;
                screen[i] = new Vector2(projected.x, projected.y);
            }

            var vertices = new Vector3[count * 2];
            var offsets = new Vector2[count * 2];
            var distances = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];
            for (int i = 0; i < count; i++)
            {
                Vector2 incoming = i > 0 ? screen[i] - screen[i - 1]
                    : closed ? screen[0] - screen[count - 2] : screen[1] - screen[0];
                Vector2 outgoing = i < count - 1 ? screen[i + 1] - screen[i]
                    : closed ? screen[1] - screen[0] : incoming;
                incoming.Normalize();
                outgoing.Normalize();
                Vector2 n0 = new Vector2(-incoming.y, incoming.x);
                Vector2 n1 = new Vector2(-outgoing.y, outgoing.x);
                Vector2 miter = (n0 + n1).normalized;
                float denominator = Vector2.Dot(miter, n1);
                Vector2 offset = denominator > 0.5f ? miter * (outerWidth / denominator) : n1 * outerWidth;
                vertices[i * 2] = vertices[i * 2 + 1] = points[i];
                offsets[i * 2] = offset;
                offsets[i * 2 + 1] = -offset;
                distances[i * 2] = new Vector2(outerWidth, 0f);
                distances[i * 2 + 1] = new Vector2(-outerWidth, 0f);
                if (i == count - 1) continue;
                int k = i * 6;
                int v = i * 2;
                triangles[k] = v;
                triangles[k + 1] = v + 1;
                triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 2;
                triangles[k + 4] = v + 1;
                triangles[k + 5] = v + 3;
            }

            mesh.Clear();
            mesh.indexFormat = count * 2 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = offsets;
            mesh.uv2 = distances;
            mesh.SetTriangles(triangles, 0);
            return true;
        }

        private Vector3[] GetLoopWorldPoints(MeshAnalysis analysis, LoopSelection loop)
        {
            if (analysis == null || loop == null || loop.vertices == null || loop.vertices.Count < 2) return null;

            var world = analysis.worldVertices;
            var result = new Vector3[loop.vertices.Count];

            for (int i = 0; i < loop.vertices.Count; i++)
            {
                int index = loop.vertices[i];
                if (index < 0 || index >= world.Length) return null;
                result[i] = world[index];
            }

            return result;
        }

        private void DrawMaskPreview()
        {
            if (TryEnsureGpuTextures(out string error)) DrawGpuMaskPreview();
            else ReportGpuError(error);
        }

        private void DrawGpuMaskPreview()
        {
            var material = GetPreviewMaterial();
            if (material == null) return;
            foreach (var error in _gpuTargetErrors.Values) { ReportGpuError(error); break; }
            foreach (var slot in GetSelectedMaterialSlots())
            {
                long id = slot.Key;
                if (_gpuTargetErrors.ContainsKey(id)) continue;
                var baked = _gpuBaker.Find(id);
                var analysis = FindAnalysisByInstanceId(slot.target.GetInstanceID());
                if (baked == null || analysis == null) continue;
                var mesh = GetPreviewMesh(analysis, slot);
                material.SetTexture("_MaskTex", baked.Texture);
                if (material.SetPass(0)) Graphics.DrawMeshNow(mesh, analysis.transform.localToWorldMatrix);
            }
        }

        private Material GetPreviewMaterial()
        {
            if (_previewMaterial != null) return _previewMaterial;
            var shader = Shader.Find(PreviewShaderName);
            if (shader == null) return null;
            _previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _previewMaterial;
        }

        private Mesh GetPreviewMesh(MeshAnalysis analysis, MaterialSlotSelection slot)
        {
            long id = slot.Key;
            if (!_previewMeshes.TryGetValue(id, out var mesh) || mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.indexFormat = analysis.localVertices.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                _previewMeshes[id] = mesh;
            }
            if (!_gpuPreviewVersions.TryGetValue(id, out int version) || version != _gpuGeometryVersion
                || mesh.vertexCount != analysis.localVertices.Length)
            {
                mesh.Clear();
                mesh.SetVertices(analysis.localVertices);
                mesh.SetTriangles(analysis.subMeshTriangles[slot.SubMeshIndex], 0, false);
                mesh.uv = analysis.uvs;
                mesh.RecalculateBounds();
                _gpuPreviewVersions[id] = _gpuGeometryVersion;
            }
            return mesh;
        }

        #endregion

        #region 网格拓扑分析

        /// <summary>单个网格的拓扑与位置缓存。</summary>
        private sealed class MeshAnalysis
        {
            public GameObject sourceObject;
            public Transform transform;
            public Mesh mesh;
            public bool ownsMesh;

            public int meshInstanceId;
            public int vertexCount;
            public int indexCount;

            public Vector3[] localVertices;
            public Vector2[] uvs;
            public int[] triangles;
            public int[][] subMeshTriangles;

            // 仅分析拓扑时焊接 UV / 法线接缝；原网格、UV 和输出顶点数不变。
            public int[] topologyVertices;
            public int[] edgeA;
            public int[] edgeB;
            public byte[] edgeFaceCount;
            public int[] edgeFace0;
            public int[] edgeFace1;
            public Vector3[] edgeLocalNormals;

            /// <summary>每个面 3 条边的索引，顺序与面的顶点顺序一致。</summary>
            public int[] faceEdges;

            /// <summary>CSR 结构的顶点邻接边表。</summary>
            public int[] vertexEdgeStart;
            public int[] vertexEdges;

            public Matrix4x4 localToWorld = Matrix4x4.identity;
            public Matrix4x4 normalMatrix = Matrix4x4.identity;
            public Vector3[] worldVertices;
            public Vector3[] worldEdgeNormals;
            public Mesh sourceMesh;
            public SkinnedMeshRenderer skinned;
            public Transform[] poseBones;
            public Matrix4x4[] boneMatrices;
            public float[] blendWeights;
            public bool poseInitialized;
            public int poseCheckedGeneration = -1;
            public int positionVersion, notifiedPositionVersion, edgeNormalsVersion = -1;

            public int EdgeCount => edgeA != null ? edgeA.Length : 0;
            public int FaceCount => faceEdges != null ? faceEdges.Length / 3 : 0;
        }

        private void InvalidateAnalysis()
        {
            ResetMaterialSlotResources();
            _geometryCheckGeneration = -1;
            foreach (var pair in _analysisCache)
            {
                if (pair.Value != null && pair.Value.ownsMesh && pair.Value.mesh != null)
                {
                    DestroyImmediate(pair.Value.mesh);
                }
            }

            _analysisCache.Clear();
            _loopGeometry = null;
        }

        private MeshAnalysis EnsureAnalysis(GameObject go, bool forceRebuild)
        {
            if (go == null) return null;

            int id = go.GetInstanceID();
            if (!forceRebuild && _analysisCache.TryGetValue(id, out var cached) && IsAnalysisValid(cached))
            {
                return cached;
            }

            if (_analysisCache.TryGetValue(id, out var previous))
            {
                if (previous != null && previous.ownsMesh && previous.mesh != null) DestroyImmediate(previous.mesh);
                _analysisCache.Remove(id);
            }

            var analysis = BuildAnalysis(go);
            if (analysis != null)
            {
                _analysisCache[id] = analysis;
                RefreshPositions(analysis);
            }

            return analysis;
        }

        private MeshAnalysis FindAnalysisByInstanceId(int instanceId)
        {
            return _analysisCache.TryGetValue(instanceId, out var analysis) ? analysis : null;
        }

        private static bool IsAnalysisValid(MeshAnalysis analysis)
        {
            if (analysis == null || analysis.mesh == null || analysis.sourceObject == null) return false;
            if (analysis.sourceMesh != GetRendererMesh(analysis.sourceObject)) return false;
            if (analysis.meshInstanceId != analysis.mesh.GetInstanceID()) return false;
            if (analysis.vertexCount != analysis.mesh.vertexCount) return false;
            if (analysis.indexCount != (int)analysis.mesh.GetIndexCount(0)) return false;
            if (analysis.sourceMesh.vertexCount != analysis.vertexCount
                || analysis.sourceMesh.subMeshCount != analysis.subMeshTriangles.Length) return false;
            for (int i = 0; i < analysis.subMeshTriangles.Length; i++)
            {
                int count = analysis.sourceMesh.GetTopology(i) == MeshTopology.Triangles
                    ? (int)analysis.sourceMesh.GetIndexCount(i) : 0;
                if (count != analysis.subMeshTriangles[i].Length) return false;
            }
            return true;
        }

        private static MeshAnalysis BuildAnalysis(GameObject go)
        {
            var renderer = GetRenderer(go);
            if (renderer == null) return null;

            Mesh mesh;
            bool ownsMesh = false;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) return null;

                var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    skinned.BakeMesh(baked);
                    mesh = baked;
                    ownsMesh = true;
                }
                catch (Exception)
                {
                    DestroyImmediate(baked);
                    mesh = skinned.sharedMesh;
                }
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return null;
                mesh = filter.sharedMesh;
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount == 0)
            {
                if (ownsMesh) DestroyImmediate(mesh);
                return null;
            }

            var analysis = new MeshAnalysis
            {
                sourceObject = go,
                transform = renderer.transform,
                mesh = mesh,
                ownsMesh = ownsMesh,
                sourceMesh = GetRendererMesh(go),
                skinned = renderer as SkinnedMeshRenderer,
                meshInstanceId = mesh.GetInstanceID(),
                vertexCount = vertexCount,
                indexCount = (int)mesh.GetIndexCount(0),
            };

            analysis.localVertices = mesh.vertices;
            analysis.subMeshTriangles = new int[mesh.subMeshCount][];
            var allTriangles = new List<int>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                analysis.subMeshTriangles[slot] = mesh.GetTopology(slot) == MeshTopology.Triangles
                    ? mesh.GetTriangles(slot) : Array.Empty<int>();
                allTriangles.AddRange(analysis.subMeshTriangles[slot]);
            }
            analysis.triangles = allTriangles.ToArray();
            analysis.uvs = mesh.uv != null && mesh.uv.Length == vertexCount ? mesh.uv : null;

            BuildTopology(analysis);
            return analysis;
        }

        /// <summary>构建唯一边表、面-边关系以及 CSR 顶点邻接表。</summary>
        private static void BuildTopology(MeshAnalysis analysis)
        {
            var triangles = analysis.triangles;
            int faceCount = triangles.Length / 3;
            analysis.topologyVertices = WeldTopologyVertices(analysis.localVertices);

            var edgeLookup = new Dictionary<long, int>(faceCount * 2);
            var edgeA = new List<int>(faceCount * 2);
            var edgeB = new List<int>(faceCount * 2);
            var edgeFaceCount = new List<byte>(faceCount * 2);
            var edgeFace0 = new List<int>(faceCount * 2);
            var edgeFace1 = new List<int>(faceCount * 2);
            var faceEdges = new int[faceCount * 3];

            for (int face = 0; face < faceCount; face++)
            {
                int i0 = analysis.topologyVertices[triangles[face * 3]];
                int i1 = analysis.topologyVertices[triangles[face * 3 + 1]];
                int i2 = analysis.topologyVertices[triangles[face * 3 + 2]];
                if (i0 == i1 || i1 == i2 || i2 == i0)
                {
                    faceEdges[face * 3] = faceEdges[face * 3 + 1] = faceEdges[face * 3 + 2] = -1;
                    continue;
                }

                faceEdges[face * 3] = GetOrAddEdge(i0, i1, face);
                faceEdges[face * 3 + 1] = GetOrAddEdge(i1, i2, face);
                faceEdges[face * 3 + 2] = GetOrAddEdge(i2, i0, face);
            }

            analysis.edgeA = edgeA.ToArray();
            analysis.edgeB = edgeB.ToArray();
            analysis.edgeFaceCount = edgeFaceCount.ToArray();
            analysis.edgeFace0 = edgeFace0.ToArray();
            analysis.edgeFace1 = edgeFace1.ToArray();
            analysis.faceEdges = faceEdges;
            analysis.edgeLocalNormals = new Vector3[analysis.edgeA.Length];

            // CSR 顶点邻接边表。
            int vertexCount = analysis.localVertices.Length;
            var counts = new int[vertexCount + 1];
            for (int e = 0; e < analysis.edgeA.Length; e++)
            {
                counts[analysis.edgeA[e]]++;
                counts[analysis.edgeB[e]]++;
            }

            var start = new int[vertexCount + 1];
            int running = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                start[v] = running;
                running += counts[v];
            }

            start[vertexCount] = running;

            var cursor = new int[vertexCount];
            Array.Copy(start, cursor, vertexCount);

            var vertexEdges = new int[running];
            for (int e = 0; e < analysis.edgeA.Length; e++)
            {
                vertexEdges[cursor[analysis.edgeA[e]]++] = e;
                vertexEdges[cursor[analysis.edgeB[e]]++] = e;
            }

            analysis.vertexEdgeStart = start;
            analysis.vertexEdges = vertexEdges;

            return;

            int GetOrAddEdge(int a, int b, int ownerFace)
            {
                int min = Mathf.Min(a, b);
                int max = Mathf.Max(a, b);
                long key = ((long)min << 32) | (uint)max;

                if (edgeLookup.TryGetValue(key, out int existing))
                {
                    if (edgeFaceCount[existing] < 2)
                    {
                        if (edgeFaceCount[existing] == 0) edgeFace0[existing] = ownerFace;
                        else edgeFace1[existing] = ownerFace;
                    }
                    // 保留非流形信息，不能把三面共边伪装成普通内部边。
                    if (edgeFaceCount[existing] < byte.MaxValue) edgeFaceCount[existing]++;

                    return existing;
                }

                int index = edgeA.Count;
                edgeLookup[key] = index;
                edgeA.Add(min);
                edgeB.Add(max);
                edgeFaceCount.Add(1);
                edgeFace0.Add(ownerFace);
                edgeFace1.Add(-1);
                return index;
            }
        }

        /// <summary>按位置焊接分析顶点，消除导入网格的 UV / 硬法线接缝。</summary>
        private static int[] WeldTopologyVertices(Vector3[] vertices)
        {
            const float tolerance = 0.000001f;
            var cells = new Dictionary<Vector3Int, List<int>>();
            var result = new int[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 p = vertices[v];
                var cell = new Vector3Int(Mathf.FloorToInt(p.x / tolerance),
                    Mathf.FloorToInt(p.y / tolerance), Mathf.FloorToInt(p.z / tolerance));
                int representative = -1;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue(cell + new Vector3Int(x, y, z), out var candidates)) continue;
                    foreach (int candidate in candidates)
                    {
                        if ((vertices[candidate] - p).sqrMagnitude > tolerance * tolerance) continue;
                        if (representative < 0 || candidate < representative) representative = candidate;
                    }
                }
                result[v] = representative < 0 ? v : representative;
                if (representative >= 0) continue;
                if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new List<int>();
                bucket.Add(v);
            }
            return result;
        }

        /// <summary>重新读取顶点位置（蒙皮网格会重新 Bake），并刷新世界空间缓存。</summary>
        private bool RefreshPositions(MeshAnalysis analysis)
        {
            if (analysis == null || analysis.mesh == null) return false;
            if (analysis.poseCheckedGeneration == _poseCheckGeneration && analysis.poseInitialized)
            {
                EnsureSelectionNormals(analysis);
                return false;
            }
            analysis.poseCheckedGeneration = _poseCheckGeneration;
            Matrix4x4 matrix = analysis.transform != null ? analysis.transform.localToWorldMatrix : Matrix4x4.identity;
            bool changed = !analysis.poseInitialized || matrix != analysis.localToWorld;
            var skinned = analysis.skinned;
            if (skinned != null)
            {
                if (analysis.poseBones == null)
                {
                    analysis.poseBones = skinned.bones;
                    analysis.boneMatrices = new Matrix4x4[analysis.poseBones.Length];
                    changed = true;
                }
                for (int i = 0; i < analysis.poseBones.Length; i++)
                {
                    var bone = analysis.poseBones[i];
                    Matrix4x4 boneMatrix = bone != null ? bone.localToWorldMatrix : Matrix4x4.identity;
                    if (boneMatrix != analysis.boneMatrices[i]) changed = true;
                }
                int shapes = skinned.sharedMesh != null ? skinned.sharedMesh.blendShapeCount : 0;
                if (analysis.blendWeights == null || analysis.blendWeights.Length != shapes)
                {
                    analysis.blendWeights = new float[shapes];
                    changed = true;
                }
                for (int i = 0; i < shapes; i++)
                    if (analysis.blendWeights[i] != skinned.GetBlendShapeWeight(i)) changed = true;
            }
            if (!changed)
            {
                EnsureSelectionNormals(analysis);
                return false;
            }
            if (analysis.ownsMesh && skinned != null && skinned.sharedMesh != null)
            {
                try { skinned.BakeMesh(analysis.mesh); }
                catch (Exception exception)
                {
                    SetStatus(NeckMaskLoc.T("蒙皮 Bake 失败：") + exception.Message, true);
                    return false;
                }
            }
            _vertexScratch.Clear();
            analysis.mesh.GetVertices(_vertexScratch);
            if (_vertexScratch.Count != analysis.localVertices.Length) return false;
            _vertexScratch.CopyTo(analysis.localVertices);
            analysis.localToWorld = matrix;
            analysis.normalMatrix = matrix.inverse.transpose;
            int vertexCount = analysis.localVertices.Length;
            if (analysis.worldVertices == null || analysis.worldVertices.Length != vertexCount)
                analysis.worldVertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                analysis.worldVertices[i] = matrix.MultiplyPoint3x4(analysis.localVertices[i]);
            if (skinned != null)
            {
                for (int i = 0; i < analysis.poseBones.Length; i++)
                    analysis.boneMatrices[i] = analysis.poseBones[i] != null
                        ? analysis.poseBones[i].localToWorldMatrix : Matrix4x4.identity;
                for (int i = 0; i < analysis.blendWeights.Length; i++)
                    analysis.blendWeights[i] = skinned.GetBlendShapeWeight(i);
            }
            analysis.poseInitialized = true;
            analysis.positionVersion++;
            EnsureSelectionNormals(analysis);
            return true;
        }

        private void EnsureSelectionNormals(MeshAnalysis analysis)
        {
            if (!_isSelecting || analysis.edgeNormalsVersion == analysis.positionVersion) return;
            RecalculateEdgeNormals(analysis);
            analysis.edgeNormalsVersion = analysis.positionVersion;
        }

        private static void RecalculateEdgeNormals(MeshAnalysis analysis)
        {
            int edgeCount = analysis.EdgeCount;
            if (edgeCount == 0) return;

            var localNormals = analysis.edgeLocalNormals;
            Array.Clear(localNormals, 0, localNormals.Length);

            var triangles = analysis.triangles;
            var vertices = analysis.localVertices;
            int faceCount = triangles.Length / 3;

            for (int face = 0; face < faceCount; face++)
            {
                int i0 = triangles[face * 3];
                int i1 = triangles[face * 3 + 1];
                int i2 = triangles[face * 3 + 2];

                Vector3 normal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                float length = normal.magnitude;
                if (length < 1e-9f) continue;
                normal /= length;

                for (int k = 0; k < 3; k++)
                {
                    int edge = analysis.faceEdges[face * 3 + k];
                    if (edge >= 0) localNormals[edge] += normal;
                }
            }

            if (analysis.worldEdgeNormals == null || analysis.worldEdgeNormals.Length != edgeCount)
            {
                analysis.worldEdgeNormals = new Vector3[edgeCount];
            }

            for (int e = 0; e < edgeCount; e++)
            {
                analysis.worldEdgeNormals[e] = analysis.normalMatrix.MultiplyVector(localNormals[e]).normalized;
            }
        }

        #endregion

        #region 边的拾取与循环线推导

        /// <summary>返回鼠标下方最近的边索引，找不到时返回 -1。</summary>
        private static int PickEdge(MeshAnalysis analysis, Vector2 mousePosition)
        {
            if (analysis == null || analysis.EdgeCount == 0) return -1;

            var camera = Camera.current;
            if (camera == null) return -1;
            Vector3 cameraPosition = camera.transform.position;
            Vector2 screenMouse = HandleUtility.GUIPointToScreenPixelCoordinate(mousePosition);

            int bestEdge = -1;
            float bestDistance = PickPixelRadius * EditorGUIUtility.pixelsPerPoint;

            var world = analysis.worldVertices;
            var edgeNormals = analysis.worldEdgeNormals;

            for (int e = 0; e < analysis.EdgeCount; e++)
            {
                Vector3 a = world[analysis.edgeA[e]];
                Vector3 b = world[analysis.edgeB[e]];

                // 背面剔除：让被遮挡一侧的边不会被误拾取。
                Vector3 normal = edgeNormals[e];
                if (normal.sqrMagnitude > 1e-6f && Vector3.Dot(normal, cameraPosition - a) <= 0f) continue;

                // 拾取与边线使用同一套相机像素投影，不走 Handles AA 线的转换路径。
                Vector3 pa = camera.WorldToScreenPoint(a);
                Vector3 pb = camera.WorldToScreenPoint(b);
                if (pa.z <= camera.nearClipPlane || pb.z <= camera.nearClipPlane) continue;
                float distance = DistancePointSegment(new Vector3(screenMouse.x, screenMouse.y, 0f),
                    new Vector3(pa.x, pa.y, 0f), new Vector3(pb.x, pb.y, 0f));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestEdge = e;
                }
            }

            return bestEdge;
        }

        /// <summary>从一条边出发推导整条循环线，返回有序的顶点索引。</summary>
        private static List<int> BuildLoop(MeshAnalysis analysis, int startEdge)
        {
            var loop = new List<int>();
            if (analysis == null || startEdge < 0 || startEdge >= analysis.EdgeCount) return loop;

            if (analysis.edgeFaceCount[startEdge] > 2) return loop;
            int a = analysis.edgeA[startEdge];
            int b = analysis.edgeB[startEdge];
            loop.Add(a);
            loop.Add(b);
            var visited = new HashSet<int> { a, b };
            bool boundary = analysis.edgeFaceCount[startEdge] == 1;
            if (Walk(a, b, false)) return loop;
            // 开放链需要从种子边另一端继续，而不是只返回半条线。
            Walk(b, a, true);
            return loop;

            bool Walk(int previous, int current, bool prepend)
            {
                int edge = startEdge;
                for (int guard = 0; guard < analysis.EdgeCount; guard++)
                {
                    int next = boundary ? NextBoundaryEdge(analysis, current, edge)
                        : OppositeEdgeAtVertex(analysis, current, edge);
                    if (!boundary && next < 0)
                    {
                        var direction = (analysis.worldVertices[current] - analysis.worldVertices[previous]).normalized;
                        next = GeometricNextEdge(analysis, current, edge, direction, GeometricContinuationMinDot);
                    }
                    if (next < 0 || next == startEdge) return false;
                    int other = OtherVertex(analysis, next, current);
                    int end = prepend ? loop[loop.Count - 1] : loop[0];
                    if (other == end && loop.Count >= 3)
                    {
                        if (prepend) loop.Insert(0, other);
                        else loop.Add(other); // 显式闭合，Scene 不再漏画最后一段。
                        return true;
                    }
                    if (!visited.Add(other)) return false;
                    if (prepend) loop.Insert(0, other);
                    else loop.Add(other);
                    previous = current;
                    current = other;
                    edge = next;
                }
                return false;
            }
        }

        /// <summary>流形边界只有唯一续边；遇到分叉停止，避免跳入另一条边界。</summary>
        private static int NextBoundaryEdge(MeshAnalysis analysis, int vertex, int currentEdge)
        {
            int best = -1;

            int start = analysis.vertexEdgeStart[vertex];
            int end = analysis.vertexEdgeStart[vertex + 1];

            for (int i = start; i < end; i++)
            {
                int edge = analysis.vertexEdges[i];
                if (edge == currentEdge) continue;
                if (analysis.edgeFaceCount[edge] != 1) continue;

                if (best >= 0) return -1;
                best = edge;
            }

            return best;
        }

        /// <summary>
        /// 在顶点周围按面的扇面顺序走一圈，返回“对侧”的那条边。
        /// 仅在顶点为偶数价且扇面闭合时有效，否则返回 -1 交给几何回退。
        /// </summary>
        private static int OppositeEdgeAtVertex(MeshAnalysis analysis, int vertex, int edge)
        {
            if (analysis.edgeFaceCount[edge] != 2) return -1;
            var fan = new List<int>(8) { edge };
            bool closed = false;

            int currentEdge = edge;
            int currentFace = analysis.edgeFace0[edge];
            int guard = 64;

            while (currentFace >= 0 && guard-- > 0)
            {
                int next = OtherEdgeAtVertex(analysis, currentFace, vertex, currentEdge);
                if (next == edge)
                {
                    closed = true;
                    break;
                }
                if (next < 0 || analysis.edgeFaceCount[next] != 2 || fan.Contains(next)) return -1;

                fan.Add(next);

                int face0 = analysis.edgeFace0[next];
                int face1 = analysis.edgeFace1[next];
                currentFace = face0 == currentFace ? face1 : face0;
                currentEdge = next;
            }

            if (!closed) return -1;
            if (fan.Count < 4 || fan.Count % 2 != 0) return -1;

            return fan[fan.Count / 2];
        }

        private static int OtherEdgeAtVertex(MeshAnalysis analysis, int face, int vertex, int edge)
        {
            for (int k = 0; k < 3; k++)
            {
                int candidate = analysis.faceEdges[face * 3 + k];
                if (candidate == edge) continue;
                if (analysis.edgeA[candidate] == vertex || analysis.edgeB[candidate] == vertex) return candidate;
            }

            return -1;
        }

        /// <summary>几何回退：选择方向延续性最好、且偏折不超过阈值的邻接边。</summary>
        private static int GeometricNextEdge(MeshAnalysis analysis, int vertex, int currentEdge, Vector3 direction, float minDot)
        {
            int best = -1;
            float bestDot = minDot;

            int start = analysis.vertexEdgeStart[vertex];
            int end = analysis.vertexEdgeStart[vertex + 1];

            for (int i = start; i < end; i++)
            {
                int edge = analysis.vertexEdges[i];
                if (edge == currentEdge || analysis.edgeFaceCount[edge] != 2) continue;

                int other = OtherVertex(analysis, edge, vertex);
                Vector3 candidate = (analysis.worldVertices[other] - analysis.worldVertices[vertex]).normalized;
                float dot = Vector3.Dot(direction, candidate);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = edge;
                }
            }

            return best;
        }

        private static int OtherVertex(MeshAnalysis analysis, int edge, int vertex)
        {
            return analysis.edgeA[edge] == vertex ? analysis.edgeB[edge] : analysis.edgeA[edge];
        }

        #endregion

        #region Mask 计算

        /// <summary>颈部循环线的世界空间几何信息。</summary>
        private sealed class LoopGeometry
        {
            public Vector3[] segments = Array.Empty<Vector3>();
            public bool valid;
        }

        [Serializable]
        private sealed class LoopSelection
        {
            public List<int> vertices = new List<int>();
        }

        private void MarkDirty()
        {
            _loopDirty = true;
            _gpuGeometryVersion++;
            MarkParametersDirty();
        }

        private void MarkParametersDirty()
        {
            _textureDirty = true;
            _gpuTargetErrors.Clear();
            if (_preview || _showTexturePreview) SceneView.RepaintAll();
        }

        private void SynchronizeGeometry()
        {
            if (_geometryCheckGeneration == _poseCheckGeneration) return;
            _geometryCheckGeneration = _poseCheckGeneration;
            bool changed = CheckTargetPositions(_body);
            changed |= CheckTargetPositions(_bodyBase);
            if (_selectionSource != _body && _selectionSource != _bodyBase)
                changed |= CheckTargetPositions(_selectionSource);
            if (changed) MarkDirty();
        }

        private bool CheckTargetPositions(GameObject target)
        {
            if (target == null) return false;
            var analysis = EnsureAnalysis(target, false);
            if (analysis == null) return false;
            RefreshPositions(analysis);
            if (analysis.notifiedPositionVersion == analysis.positionVersion) return false;
            analysis.notifiedPositionVersion = analysis.positionVersion;
            return true;
        }

        private LoopGeometry EnsureLoopGeometry()
        {
            if (_loopDirty)
            {
                _loopGeometry = BuildLoopGeometry();
                _loopDirty = false;
            }
            return _loopGeometry;
        }

        private bool TryEnsureGpuTextures(out string error)
        {
            error = null;
            var slots = GetSelectedMaterialSlots();
            if (slots.Count == 0) { error = NeckMaskLoc.T("请至少勾选一个有效目标材质槽。"); return false; }
            SynchronizeGeometry();
            if (_gpuBaker == null && !NeckMaskGpuBaker.IsSupported(out error)) return false;
            var loop = EnsureLoopGeometry();
            if (loop == null || !loop.valid) { error = NeckMaskLoc.T("还没有可用的颈部循环线。"); return false; }
            try
            {
                int size = Mathf.Clamp(_textureSize, 64, MaxTextureSize);
                if (_gpuBaker == null || _gpuBaker.Size != size)
                {
                    ResetMaterialSlotResources();
                    _gpuBaker = new NeckMaskGpuBaker(size);
                }
                foreach (var slot in slots)
                {
                    long id = slot.Key;
                    if (_gpuTargetErrors.ContainsKey(id)) continue;
                    var analysis = EnsureAnalysis(slot.target, false);
                    if (analysis == null || analysis.uvs == null)
                    {
                        _gpuTargetErrors[id] = NeckMaskLoc.F("{0}：目标没有有效网格或 UV0。", slot.Label);
                        continue;
                    }
                    try
                    {
                        _gpuBaker.Prepare(id, analysis.uvs, analysis.subMeshTriangles[slot.SubMeshIndex], analysis.worldVertices,
                            loop.segments, _gpuGeometryVersion,
                            _maxDistance, _samplingMode, SamplingParameter, _dilation);
                    }
                    catch (Exception exception) { _gpuTargetErrors[id] = NeckMaskLoc.F("{0}：{1}", slot.Label, exception.Message); }
                }
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private void ReportGpuError(string error)
        {
            if (!string.IsNullOrEmpty(error) && (_statusMessage != error || !_statusIsError))
                SetStatus(error, true);
        }

        private LoopGeometry BuildLoopGeometry()
        {
            if (_loops.Count == 0 || _selectionSource == null) return null;

            var analysis = EnsureAnalysis(_selectionSource, false);
            if (analysis == null) return null;

            RefreshPositions(analysis);

            var pointCount = 0;
            var segments = new List<Vector3>();
            var world = analysis.worldVertices;

            for (int i = 0; i < _loops.Count; i++)
            {
                var loop = _loops[i];
                if (loop == null || loop.vertices == null || loop.vertices.Count < 2) continue;

                var loopPoints = new List<Vector3>();
                for (int k = 0; k < loop.vertices.Count; k++)
                {
                    int index = loop.vertices[k];
                    if (index < 0 || index >= world.Length) continue;
                    loopPoints.Add(world[index]);
                }
                if (loopPoints.Count < 2) continue;
                pointCount += loopPoints.Count;
                for (int k = 0; k < loopPoints.Count; k++)
                {
                    Vector3 a = loopPoints[k], b = loopPoints[(k + 1) % loopPoints.Count];
                    if ((a - b).sqrMagnitude < 1e-12f) continue;
                    segments.Add(a);
                    segments.Add(b);
                }
            }

            if (pointCount < 3) return null;

            return new LoopGeometry { segments = segments.ToArray(), valid = segments.Count > 0 };
        }

        private static float DistancePointSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-12f) return (point - a).magnitude;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);
            return (point - (a + ab * t)).magnitude;
        }

        #endregion

        #region 贴图预览

        /// <summary>Scene 叠加面板中的一条预览记录：一个材质槽对应一张独立贴图。</summary>
        private sealed class TexturePreviewEntry
        {
            public GameObject target;
            public string role;
            public RenderTexture texture;
            public string error;
        }

        /// <summary>
        /// 在 Scene 视图的最前层绘制贴图预览面板。
        /// 面板使用绝对坐标的 GUI 控件，不依赖 Layout 事件；仅关闭按钮可交互。
        /// </summary>
        private void DrawTexturePreviewOverlay(SceneView sceneView)
        {
            EnsureTexturePreviews();

            float imageSize = Mathf.Clamp(_texturePreviewImageSize, MinTexturePreviewSize, MaxTexturePreviewSize);
            float panelWidth = imageSize + TexturePreviewPadding * 2f;

            // 上下内边距独立计算，条目间距仅用于相邻贴图之间。
            float contentHeight = TexturePreviewPadding * 2f;
            if (_texturePreviews.Count == 0)
            {
                contentHeight += TexturePreviewLabelHeight * 2f;
            }
            else
            {
                for (int i = 0; i < _texturePreviews.Count; i++)
                {
                    contentHeight += TexturePreviewLabelHeight * 2f + 4f + imageSize;
                    if (i > 0) contentHeight += TexturePreviewSpacing;
                }
            }

            float panelHeight = TexturePreviewHeaderHeight + contentHeight;

            // 每次按当前 Scene 的实际 viewport 锚定，旧 EditorPrefs 坐标不参与定位。
            var viewport = GetSceneViewportSize(sceneView);
            var panel = new Rect(SceneOverlayMargin,
                Mathf.Max(SceneOverlayMargin, viewport.y - SceneOverlayMargin - panelHeight),
                panelWidth, panelHeight);
            _texturePreviewPanel = panel;

            var current = Event.current;
            if (current == null) return;

            var close = new Rect(panel.xMax - TexturePreviewHeaderHeight, panel.y,
                TexturePreviewHeaderHeight, TexturePreviewHeaderHeight);

            Handles.BeginGUI();
            var previousDepth = GUI.depth;
            var previousSkin = GUI.skin;
            try
            {
                GUI.depth = TexturePreviewGuiDepth;
                GUI.skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);

                DrawTexturePreviewPanelBackground(panel);
                GUI.Label(
                    new Rect(panel.x + TexturePreviewPadding, panel.y,
                        panel.width - TexturePreviewPadding - 24f, TexturePreviewHeaderHeight),
                    new GUIContent(NeckMaskLoc.T("贴图预览")), EditorStyles.boldLabel);

                // ToolbarButton 默认 padding / contentOffset 不适合方形图标按钮。
                var closeStyle = new GUIStyle(EditorStyles.toolbarButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(),
                    margin = new RectOffset(),
                    contentOffset = Vector2.zero,
                    fixedWidth = 0f,
                    fixedHeight = 0f,
                    imagePosition = ImagePosition.ImageOnly,
                };
                var closeIcon = EditorGUIUtility.IconContent("winbtn_win_close").image;
                var closeContent = closeIcon != null
                    ? new GUIContent(closeIcon, NeckMaskLoc.T("关闭贴图预览"))
                    : new GUIContent("×", NeckMaskLoc.T("关闭贴图预览"));
                if (closeIcon == null) closeStyle.imagePosition = ImagePosition.TextOnly;
                if (GUI.Button(close, closeContent, closeStyle))
                {
                    _showTexturePreview = false;
                    ReleaseTexturePreviews();
                    RememberTexturePreviewPanel();
                    Repaint();
                    SceneView.RepaintAll();
                }

                if (current.type == EventType.Repaint)
                {
                    DrawTexturePreviewContents(panel, imageSize);
                    // ToolbarButton 的背景会覆盖先绘制的 Toolbar 底线；最后统一绘制整条分隔线。
                    float lineHeight = 1f / EditorGUIUtility.pixelsPerPoint;
                    var previousColor = GUI.color;
                    GUI.color = Color.white; // 不继承 Scene Handles 的透明度，保证边线不透底。
                    EditorGUI.DrawRect(new Rect(panel.x, panel.y + TexturePreviewHeaderHeight - lineHeight,
                        panel.width, lineHeight), EditorGUIUtility.isProSkin
                        ? new Color(0.16f, 0.16f, 0.16f, 1f)
                        : new Color(0.5f, 0.5f, 0.5f, 1f));
                    GUI.color = previousColor;
                }
            }
            finally
            {
                GUI.skin = previousSkin;
                GUI.depth = previousDepth;
            }

            Handles.EndGUI();

            // 面板上的鼠标事件不再穿透到 Scene 相机操作。
            bool blocked = Array.IndexOf(TexturePreviewBlockedEvents, current.type) >= 0;
            if (blocked && panel.Contains(current.mousePosition))
            {
                current.Use();
            }
        }

        /// <summary>Unity 原生面板与工具栏样式，随编辑器主题切换。</summary>
        private static void DrawTexturePreviewPanelBackground(Rect panel)
        {
            // 原生 HelpBox 背景带透明度，垫不透明编辑器底色，避免场景透出。
            EditorGUI.DrawRect(panel, EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f));
            GUI.Box(panel, GUIContent.none, EditorStyles.helpBox);
            GUI.Box(new Rect(panel.x, panel.y, panel.width,
                TexturePreviewHeaderHeight), GUIContent.none, EditorStyles.toolbar);
        }

        private void DrawTexturePreviewContents(Rect panel, float imageSize)
        {
            float x = panel.x + TexturePreviewPadding;
            float y = panel.y + TexturePreviewHeaderHeight + TexturePreviewPadding;

            if (_texturePreviews.Count == 0)
            {
                EditorGUI.HelpBox(new Rect(x, y, imageSize, TexturePreviewLabelHeight * 2f),
                    NeckMaskLoc.T("没有可预览的网格。"), MessageType.Info);
                return;
            }

            for (int i = 0; i < _texturePreviews.Count; i++)
            {
                var entry = _texturePreviews[i];

                GUI.Label(new Rect(x, y, imageSize, TexturePreviewLabelHeight),
                    entry.role, EditorStyles.boldLabel);
                y += TexturePreviewLabelHeight + 4f;

                GUI.Box(new Rect(x, y, imageSize, imageSize), GUIContent.none, EditorStyles.helpBox);
                var image = new Rect(x + 1f, y + 1f, imageSize - 2f, imageSize - 2f);
                if (entry.texture != null)
                {
                    GUI.DrawTexture(image, entry.texture, ScaleMode.ScaleToFit, false);
                }
                else
                {
                    EditorGUI.HelpBox(image, entry.error ?? NeckMaskLoc.T("无法生成预览贴图，请检查网格与 UV。"), MessageType.Warning);
                }

                y += imageSize + TexturePreviewSpacing;
            }
        }

        /// <summary>按需为每个烘焙目标生成独立的预览贴图。</summary>
        private void EnsureTexturePreviews()
        {
            if (!_showTexturePreview) { ReleaseTexturePreviews(); return; }
            bool gpu = TryEnsureGpuTextures(out string gpuError);
            if (!_textureDirty && _texturePreviewsBuilt) return;
            _textureDirty = false;
            ReleaseTexturePreviews();
            foreach (var slot in GetSelectedMaterialSlots())
            {
                var entry = new TexturePreviewEntry { target = slot.target, role = slot.Label };
                if (gpu)
                {
                    if (_gpuTargetErrors.TryGetValue(slot.Key, out string error)) entry.error = error;
                    else entry.texture = _gpuBaker.Find(slot.Key)?.Texture;
                }
                else entry.error = gpuError;
                _texturePreviews.Add(entry);
            }
            _texturePreviewsBuilt = true;
        }

        private void ReleaseTexturePreviews()
        {
            // RT 由 GPU 烘焙器持有，关闭面板不销毁表面预览正在使用的资源。
            _texturePreviews.Clear();
            _texturePreviewsBuilt = false;
        }

        private void RememberTexturePreviewPanel()
        {
            EditorPrefs.SetFloat(PreviewPanelXKey, _texturePreviewPanel.x);
            EditorPrefs.SetFloat(PreviewPanelYKey, _texturePreviewPanel.y);
            EditorPrefs.SetFloat(PreviewPanelSizeKey, _texturePreviewImageSize);
        }

        #endregion

        #region 保存

        private void SaveMaskTexture()
        {
            var slots = GetSelectedMaterialSlots();
            if (slots.Count == 0) { SetStatus(NeckMaskLoc.T("请至少勾选一个有效目标材质槽。"), true); return; }
            string directory = string.IsNullOrEmpty(_lastSaveDirectory) || !Directory.Exists(_lastSaveDirectory)
                ? Application.dataPath : _lastSaveDirectory;
            string defaultName = $"{ResolveAvatarName()}_{DateTime.Now:yyyyMMdd_HHmmss}_neckmask";
            string path = EditorUtility.SaveFilePanel(NeckMaskLoc.T("选择保存目录与文件名前缀（每槽独立 PNG）"),
                directory, defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            directory = Path.GetDirectoryName(path);
            string prefix = SanitizeFileName(Path.GetFileNameWithoutExtension(path));
            var paths = slots.ConvertAll(slot => Path.Combine(directory, MaterialSlotFileName(prefix, slot)));
            if (paths.Exists(File.Exists) && !EditorUtility.DisplayDialog(NeckMaskLoc.T("覆盖已有遮罩？"),
                NeckMaskLoc.T("以下目标文件中有同名文件：\n") + string.Join("\n", paths.ToArray()),
                NeckMaskLoc.T("覆盖"), NeckMaskLoc.T("取消"))) return;

            int saved = 0;
            try
            {
                WriteMaterialSlotMasks(slots, paths, ref saved);
                _lastSaveDirectory = directory;
                EditorPrefs.SetString(LastSaveDirectoryKey, directory ?? string.Empty);
                SetStatus(NeckMaskLoc.F("已保存 {0} 张独立材质槽 Mask：\n", saved) + string.Join("\n", paths.ToArray()), false);
            }
            catch (Exception exception)
            {
                SetStatus(NeckMaskLoc.F("保存失败（已写入 {0} 张）：{1}", saved, exception.Message), true);
            }
        }

        /// <summary>如果保存位置在工程内，则顺手把导入设置改成适合 Mask 的形式。</summary>
        private static void ImportAsMaskAsset(string absolutePath)
        {
            string dataPath = Application.dataPath;
            string normalized = absolutePath.Replace('\\', '/');
            string dataPathNormalized = dataPath.Replace('\\', '/');

            if (!normalized.StartsWith(dataPathNormalized, StringComparison.OrdinalIgnoreCase)) return;

            string assetPath = "Assets" + normalized.Substring(dataPathNormalized.Length);
            AssetDatabase.Refresh();

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private string ResolveAvatarName()
        {
            var descriptor = FindAvatarDescriptor();
            string rawName = descriptor != null
                ? descriptor.gameObject.name
                : (_body != null ? _body.name : (_bodyBase != null ? _bodyBase.name : "Avatar"));

            return SanitizeFileName(rawName);
        }

        private VRCAvatarDescriptor FindAvatarDescriptor()
        {
            if (_body != null)
            {
                var descriptor = _body.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor;
            }

            if (_bodyBase != null)
            {
                var descriptor = _bodyBase.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor;
            }

            return null;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Avatar";

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);

            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            }

            string result = builder.ToString().Trim();
            return result.Length == 0 ? "Avatar" : result;
        }

        #endregion
    }
}
