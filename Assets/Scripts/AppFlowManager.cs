using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Gestiona la secuencia de introducción en 2 etapas para "Sol Para Todos":
/// 1. Pantalla Splash Institucional (Logos SolParaTodos + Universidad Marista + Autor)
///    con marco perimetral brillante simulando una celda solar cargándose de luz (0% -> 100%).
/// 2. Pantalla de Objetivos e Impacto Social (ONG Yucatán, 3 pilares interactivos y accesibilidad)
///    con estética Blueprint Fotovoltaico, tipografía moderna y marco solar pulsante.
/// 3. Transición suave hacia el HUD AR interactivo a 60 FPS en Android.
/// </summary>
public class AppFlowManager : MonoBehaviour
{
    [Header("Configuración de Rendimiento Móvil")]
    [Tooltip("Frecuencia de cuadros objetivo para Android (60 FPS para fluidez).")]
    [SerializeField] private int targetFrameRate = 60;

    [Header("Referencias de UI")]
    [SerializeField] private CanvasGroup splashCanvasGroup;
    [SerializeField] private GameObject splashPanel;
    [SerializeField] private GameObject mainUIPanel;

    [Header("Tiempos de Transición")]
    [Tooltip("Tiempo en segundos que dura la carga lumínica de la Pantalla 1 (Splash).")]
    [SerializeField] private float displayDuration = 3.2f;

    [Tooltip("Duración del Fade entre pantallas en segundos.")]
    [SerializeField] private float fadeDuration = 0.55f;

    [Header("Eventos")]
    public UnityEvent onSplashCompleted;

    public static bool IsIntroActive { get; private set; } = true;

    public static Sprite RuntimeGlassPillSprite { get; private set; }
    public static Sprite RuntimeCircleKnobSprite { get; private set; }
    public static Sprite RuntimeSunIconSprite { get; private set; }
    public static Sprite RuntimeSunArcSprite { get; private set; }
    public static Font ModernFontRegular { get; private set; }
    public static Font ModernFontBold { get; private set; }

    private Sprite blueprintBgSprite;
    private Sprite solarFrameGlowSprite;
    private Sprite solarCellBarSprite;

    // Referencias dinámicas de animación de carga solar (Etapa 1)
    private GameObject splashVisualRoot;
    private CanvasGroup splashVisualCanvasGroup;
    private Image splashRadialBorderFill;
    private Image splashOuterGlowBorder;
    private Image splashProgressFill;
    private Text splashChargeStatusText;

    // Etapa 2: Funcionamiento del Sistema de Celdas Solares
    private GameObject solarCyclePanel;
    private CanvasGroup solarCycleCanvasGroup;
    private Image solarCycleRadialBorder;
    private Image solarCycleOuterGlow;
    private RectTransform nextToObjectivesButtonRect;
    private bool waitingForSolarCycleContinue = false;
    private bool isTransitioningToObjectives = false;

    // Etapa 3: Objetivos e Impacto Social de la Aplicación
    private GameObject objectivesPanel;
    private CanvasGroup objectivesCanvasGroup;
    private Image objectivesRadialBorder;
    private Image objectivesOuterGlow;
    private RectTransform startButtonRect;
    private bool waitingForObjectivesContinue = false;
    private bool isExitingObjectives = false;
    private bool wasPointerPressed = false;

    private void Awake()
    {
        IsIntroActive = true;
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;

        InitializeModernTypography();
        EnsureNewInputSystemModule();
        BuildAndApplyRuntimeUISprites();
        BuildModernSplashAndObjectivesScreens();

        if (mainUIPanel != null)
        {
            mainUIPanel.SetActive(false);
        }
    }

    private void InitializeModernTypography()
    {
        if (ModernFontBold == null)
        {
            ModernFontBold = Font.CreateDynamicFontFromOSFont(
                new string[] { "Segoe UI Semibold", "Segoe UI Bold", "Roboto Medium", "sans-serif-medium", "Calibri Bold", "Arial" }, 28);
        }

        if (ModernFontRegular == null)
        {
            ModernFontRegular = Font.CreateDynamicFontFromOSFont(
                new string[] { "Segoe UI", "Roboto", "sans-serif", "Calibri", "Arial" }, 20);
        }

        Font fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (ModernFontBold == null) ModernFontBold = fallback;
        if (ModernFontRegular == null) ModernFontRegular = fallback;
    }

    public static void EnsureNewInputSystemModule()
    {
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            es = esGO.AddComponent<EventSystem>();
        }

        StandaloneInputModule legacyModule = es.GetComponent<StandaloneInputModule>();
        if (legacyModule != null)
        {
            DestroyImmediate(legacyModule);
        }

        InputSystemUIInputModule uiModule = es.GetComponent<InputSystemUIInputModule>();
        if (uiModule == null)
        {
            uiModule = es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        uiModule.AssignDefaultActions();

        es.enabled = false;
        es.enabled = true;
    }

    private void BuildAndApplyRuntimeUISprites()
    {
        if (RuntimeGlassPillSprite == null) RuntimeGlassPillSprite = CreateRoundedPillSprite();
        if (RuntimeCircleKnobSprite == null) RuntimeCircleKnobSprite = CreateCircleSprite();
        if (RuntimeSunIconSprite == null) RuntimeSunIconSprite = CreateSunIconSprite();
        if (RuntimeSunArcSprite == null) RuntimeSunArcSprite = CreateSunArcSprite();
        if (blueprintBgSprite == null) blueprintBgSprite = CreateBlueprintGridSprite();
        if (solarFrameGlowSprite == null) solarFrameGlowSprite = CreateSolarCellFrameSprite();
        if (solarCellBarSprite == null) solarCellBarSprite = CreateSolarCellBarSprite();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        Transform mainUI = canvas.transform.Find("MainUIPanel");
        if (mainUI != null)
        {
            mainUIPanel = mainUI.gameObject;
            ApplyMobileSafeArea(mainUI.GetComponent<RectTransform>());

            Transform btnToggle = mainUI.Find("Btn_ToggleMode");
            if (btnToggle != null)
            {
                RectTransform rt = btnToggle.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-28f, -22f);
                rt.sizeDelta = new Vector2(248f, 48f);
            }

            Transform toggleFlujo = mainUI.Find("Toggle_FlujoElectrico");
            if (toggleFlujo != null)
            {
                RectTransform rt = toggleFlujo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(28f, 25f);
                rt.sizeDelta = new Vector2(218f, 48f);
            }

            Transform sunDock = mainUI.Find("SunDialDock");
            if (sunDock != null)
            {
                RectTransform rt = sunDock.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 22f);
            }
        }

        Image[] images = canvas.GetComponentsInChildren<Image>(true);
        foreach (Image img in images)
        {
            if (img == null) continue;
            string n = img.gameObject.name;

            if (n == "SunArcGraphic")
            {
                img.sprite = RuntimeSunArcSprite;
                img.type = Image.Type.Simple;
                img.color = new Color(1f, 0.88f, 0.45f, 0.78f);
                img.raycastTarget = false;
            }
            else if (n == "Handle_Sun")
            {
                img.sprite = RuntimeSunIconSprite;
                img.type = Image.Type.Simple;
                img.color = Color.white;
            }
            else if (n == "Handle" || n == "Checkmark")
            {
                img.sprite = RuntimeCircleKnobSprite;
                img.type = Image.Type.Simple;
            }
            else if (img.sprite == null && !n.StartsWith("Logo_") && n != "SplashPanel")
            {
                img.sprite = RuntimeGlassPillSprite;
                img.type = Image.Type.Sliced;
            }
        }

        Text[] allTexts = canvas.GetComponentsInChildren<Text>(true);
        foreach (Text t in allTexts)
        {
            if (t == null) continue;
            t.font = (t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic) ? ModernFontBold : ModernFontRegular;
        }
    }

    private void BuildModernSplashAndObjectivesScreens()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        if (splashPanel == null) splashPanel = gameObject;
        splashPanel.SetActive(true);

        splashCanvasGroup = splashPanel.GetComponent<CanvasGroup>();
        if (splashCanvasGroup == null) splashCanvasGroup = splashPanel.AddComponent<CanvasGroup>();
        splashCanvasGroup.alpha = 1f;
        splashCanvasGroup.blocksRaycasts = true;
        splashCanvasGroup.interactable = true;

        Sprite spSol = FindOrLoadLogoSprite("Logo_SolParaTodos", "Assets/Logo/SolParaTodos.jpg");
        Sprite spMarista = FindOrLoadLogoSprite("Logo_UMarista", "Assets/Logo/logo_umarista.png");

        for (int i = splashPanel.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(splashPanel.transform.GetChild(i).gameObject);
        }

        // Hacer que el contenedor raíz SplashPanel sea transparente y mantenerlo ACTIVO durante ambas pantallas
        // (ya que AppFlowManager vive en este GameObject)
        Image rootImg = splashPanel.GetComponent<Image>();
        if (rootImg != null)
        {
            rootImg.color = new Color(0f, 0f, 0f, 0f);
            rootImg.raycastTarget = false;
        }

        // ====================================================================
        // PANTALLA 1: SPLASH SCREEN CON MARCO DE CELDA SOLAR CARGÁNDOSE DE LUZ
        // ====================================================================
        splashVisualRoot = new GameObject("SplashScreen_Stage1");
        splashVisualRoot.transform.SetParent(splashPanel.transform, false);
        RectTransform rtSplashStage = splashVisualRoot.AddComponent<RectTransform>();
        rtSplashStage.anchorMin = Vector2.zero;
        rtSplashStage.anchorMax = Vector2.one;
        rtSplashStage.sizeDelta = Vector2.zero;

        Image splashBg = splashVisualRoot.AddComponent<Image>();
        splashBg.sprite = blueprintBgSprite;
        splashBg.type = Image.Type.Simple;
        splashBg.color = Color.white;
        splashBg.raycastTarget = true;

        splashVisualCanvasGroup = splashVisualRoot.AddComponent<CanvasGroup>();
        splashVisualCanvasGroup.alpha = 1f;
        splashVisualCanvasGroup.blocksRaycasts = true;

        GameObject splashContainer = CreateSolarFramedCard(
            splashVisualRoot.transform,
            "SplashSolarCard",
            new Vector2(880f, 620f),
            out splashOuterGlowBorder,
            out splashRadialBorderFill);

        CreateStyledText(
            splashContainer.transform,
            "Tag_Institucional",
            "☀️  ENERGÍA RENOVABLE Y CAPACITACIÓN COMUNITARIA  •  MÉRIDA, YUCATÁN",
            new Vector2(0f, 248f),
            new Vector2(780f, 32f),
            15,
            FontStyle.Bold,
            new Color(0.0f, 0.90f, 1.0f, 1f));

        GameObject logoPlate = new GameObject("InstitutionalLogoPlate");
        logoPlate.transform.SetParent(splashContainer.transform, false);
        RectTransform rtPlate = logoPlate.AddComponent<RectTransform>();
        rtPlate.anchoredPosition = new Vector2(0f, 82f);
        rtPlate.sizeDelta = new Vector2(720f, 235f);

        Image imgPlate = logoPlate.AddComponent<Image>();
        imgPlate.sprite = RuntimeGlassPillSprite;
        imgPlate.type = Image.Type.Sliced;
        imgPlate.color = new Color(0.97f, 0.96f, 0.93f, 0.98f);

        Outline plateOutline = logoPlate.AddComponent<Outline>();
        plateOutline.effectColor = new Color(0.0f, 0.82f, 1.0f, 0.55f);
        plateOutline.effectDistance = new Vector2(2f, -2f);

        if (spSol != null)
        {
            GameObject goSol = new GameObject("Logo_SolParaTodos");
            goSol.transform.SetParent(logoPlate.transform, false);
            RectTransform rtSol = goSol.AddComponent<RectTransform>();
            rtSol.anchoredPosition = new Vector2(spMarista != null ? -165f : 0f, 0f);
            rtSol.sizeDelta = new Vector2(310f, 185f);
            Image imgSol = goSol.AddComponent<Image>();
            imgSol.sprite = spSol;
            imgSol.preserveAspect = true;
        }

        if (spSol != null && spMarista != null)
        {
            GameObject div = new GameObject("Divider");
            div.transform.SetParent(logoPlate.transform, false);
            RectTransform rtDiv = div.AddComponent<RectTransform>();
            rtDiv.anchoredPosition = Vector2.zero;
            rtDiv.sizeDelta = new Vector2(2f, 150f);
            Image imgDiv = div.AddComponent<Image>();
            imgDiv.color = new Color(0.08f, 0.20f, 0.38f, 0.22f);
        }

        if (spMarista != null)
        {
            GameObject goMar = new GameObject("Logo_UMarista");
            goMar.transform.SetParent(logoPlate.transform, false);
            RectTransform rtMar = goMar.AddComponent<RectTransform>();
            rtMar.anchoredPosition = new Vector2(spSol != null ? 175f : 0f, 0f);
            rtMar.sizeDelta = new Vector2(290f, 135f);
            Image imgMar = goMar.AddComponent<Image>();
            imgMar.sprite = spMarista;
            imgMar.preserveAspect = true;
        }

        Text titleMain = CreateStyledText(
            splashContainer.transform,
            "Title_SolParaTodos",
            "S O L   P A R A   T O D O S",
            new Vector2(0f, -72f),
            new Vector2(780f, 52f),
            36,
            FontStyle.Bold,
            new Color(1.0f, 0.88f, 0.32f, 1f));
        AddGlowShadow(titleMain.gameObject, new Color(1.0f, 0.65f, 0.0f, 0.45f), new Vector2(0f, -2f));

        CreateStyledText(
            splashContainer.transform,
            "Subtitle_Project",
            "Simulador Interactivo 3D/AR de Sistemas Fotovoltaicos Comunitarios",
            new Vector2(0f, -116f),
            new Vector2(780f, 32f),
            19,
            FontStyle.Normal,
            new Color(0.88f, 0.95f, 1.0f, 0.95f));

        CreateStyledText(
            splashContainer.transform,
            "Author_Credits",
            "Desarrollado por:  Diego Arellano Gordillo   |   Universidad Marista de Mérida",
            new Vector2(0f, -162f),
            new Vector2(780f, 30f),
            17,
            FontStyle.Bold,
            new Color(0.45f, 0.88f, 1.0f, 1f));

        GameObject chargeBarBg = new GameObject("SolarChargeBar_Bg");
        chargeBarBg.transform.SetParent(splashContainer.transform, false);
        RectTransform rtBarBg = chargeBarBg.AddComponent<RectTransform>();
        rtBarBg.anchoredPosition = new Vector2(0f, -222f);
        rtBarBg.sizeDelta = new Vector2(560f, 22f);
        Image imgBarBg = chargeBarBg.AddComponent<Image>();
        imgBarBg.sprite = RuntimeGlassPillSprite;
        imgBarBg.type = Image.Type.Sliced;
        imgBarBg.color = new Color(0.03f, 0.08f, 0.15f, 0.95f);

        GameObject chargeBarFill = new GameObject("SolarChargeBar_Fill");
        chargeBarFill.transform.SetParent(chargeBarBg.transform, false);
        RectTransform rtBarFill = chargeBarFill.AddComponent<RectTransform>();
        rtBarFill.anchorMin = Vector2.zero;
        rtBarFill.anchorMax = Vector2.one;
        rtBarFill.sizeDelta = new Vector2(-6f, -6f);
        splashProgressFill = chargeBarFill.AddComponent<Image>();
        splashProgressFill.sprite = solarCellBarSprite;
        splashProgressFill.type = Image.Type.Filled;
        splashProgressFill.fillMethod = Image.FillMethod.Horizontal;
        splashProgressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        splashProgressFill.fillAmount = 0f;
        splashProgressFill.color = new Color(1.0f, 0.85f, 0.20f, 1f);

        splashChargeStatusText = CreateStyledText(
            splashContainer.transform,
            "Text_ChargeStatus",
            "⚡ CAPTANDO RADIACIÓN SOLAR EN CELDAS FOTOVOLTAICAS... 0%",
            new Vector2(0f, -254f),
            new Vector2(680f, 26f),
            13,
            FontStyle.Bold,
            new Color(1.0f, 0.84f, 0.32f, 0.95f));

        // ====================================================================
        // PANTALLA 2: ¿CÓMO FUNCIONA UN SISTEMA DE CELDAS SOLARES? (5 ETAPAS)
        // ====================================================================
        solarCyclePanel = new GameObject("SolarCycleScreen_Stage2");
        solarCyclePanel.transform.SetParent(splashPanel.transform, false);

        RectTransform rtCyclePanel = solarCyclePanel.AddComponent<RectTransform>();
        rtCyclePanel.anchorMin = Vector2.zero;
        rtCyclePanel.anchorMax = Vector2.one;
        rtCyclePanel.sizeDelta = Vector2.zero;

        Image cycleBg = solarCyclePanel.AddComponent<Image>();
        cycleBg.sprite = blueprintBgSprite;
        cycleBg.type = Image.Type.Simple;
        cycleBg.color = Color.white;
        cycleBg.raycastTarget = true;

        solarCycleCanvasGroup = solarCyclePanel.AddComponent<CanvasGroup>();
        solarCycleCanvasGroup.alpha = 0f;
        solarCycleCanvasGroup.blocksRaycasts = true;
        solarCycleCanvasGroup.interactable = true;

        GameObject cycleCard = CreateSolarFramedCard(
            solarCyclePanel.transform,
            "SolarCycleCard",
            new Vector2(1060f, 680f),
            out solarCycleOuterGlow,
            out solarCycleRadialBorder);
        solarCycleRadialBorder.fillAmount = 1f;

        CreateStyledText(
            cycleCard.transform,
            "Tag_Educativo",
            "⚡  FUNDAMENTOS DE ENERGÍA LIMPIA  •  CICLO SOLAR FOTOVOLTAICO",
            new Vector2(0f, 292f),
            new Vector2(920f, 26f),
            14,
            FontStyle.Bold,
            new Color(0.0f, 0.92f, 1.0f, 1f));

        Text cycleTitle = CreateStyledText(
            cycleCard.transform,
            "Title_SolarCycle",
            "¿CÓMO FUNCIONA UN SISTEMA DE CELDAS SOLARES?",
            new Vector2(0f, 252f),
            new Vector2(960f, 42f),
            29,
            FontStyle.Bold,
            new Color(1.0f, 0.88f, 0.30f, 1f));
        AddGlowShadow(cycleTitle.gameObject, new Color(1.0f, 0.65f, 0.0f, 0.4f), new Vector2(0f, -2f));

        CreateStyledText(
            cycleCard.transform,
            "Subtitle_CycleIntro",
            "Un ciclo continuo que transforma la radiación del sol en electricidad limpia y segura para el hogar:",
            new Vector2(0f, 212f),
            new Vector2(940f, 28f),
            16,
            FontStyle.Normal,
            new Color(0.88f, 0.95f, 1.0f, 0.95f));

        // Fila Superior: Etapas 1, 2 y 3
        CreateObjectivePillarCard(
            cycleCard.transform,
            new Vector2(-325f, 92f),
            new Vector2(305f, 178f),
            new Color(1.0f, 0.82f, 0.22f, 1f),
            "☀️ 1. CAPTACIÓN SOLAR",
            "Efecto Fotovoltaico (Silicio)",
            "La luz solar incide sobre las celdas de silicio (unión P-N), donde los fotones liberan electrones y los ponen en movimiento.");

        CreateObjectivePillarCard(
            cycleCard.transform,
            new Vector2(0f, 92f),
            new Vector2(305f, 178f),
            new Color(0.0f, 0.90f, 1.0f, 1f),
            "⚡ 2. CORRIENTE DC",
            "Generación Continua",
            "El flujo de electrones libres crea Corriente Continua (DC) de forma constante mientras exista radiación solar.");

        CreateObjectivePillarCard(
            cycleCard.transform,
            new Vector2(325f, 92f),
            new Vector2(305f, 178f),
            new Color(0.25f, 0.95f, 0.60f, 1f),
            "🔄 3. INVERSOR (DC ➔ AC)",
            "Conversión de Energía",
            "La energía viaja al inversor, que transforma la Corriente Continua (DC) en Corriente Alterna (AC) apta para el hogar.");

        // Fila Inferior: Etapas 4 y 5 (Centradas)
        CreateObjectivePillarCard(
            cycleCard.transform,
            new Vector2(-175f, -104f),
            new Vector2(330f, 178f),
            new Color(0.45f, 0.82f, 1.0f, 1f),
            "🏠 4. DISTRIBUCIÓN Y USO",
            "Consumo en Tiempo Real",
            "La Corriente Alterna (AC) llega al cuadro eléctrico de la vivienda para alimentar focos y aparatos al instante.");

        CreateObjectivePillarCard(
            cycleCard.transform,
            new Vector2(175f, -104f),
            new Vector2(330f, 178f),
            new Color(1.0f, 0.68f, 0.25f, 1f),
            "🔋 5. EXCEDENTES Y BATERÍA",
            "Respaldo Nocturno y Medición",
            "La energía sobrante se guarda en baterías de 12V (o red) para usarse de noche o en días nublados.");

        GameObject btnNextGO = new GameObject("Btn_NextToObjectives");
        btnNextGO.transform.SetParent(cycleCard.transform, false);
        nextToObjectivesButtonRect = btnNextGO.AddComponent<RectTransform>();
        nextToObjectivesButtonRect.anchoredPosition = new Vector2(0f, -252f);
        nextToObjectivesButtonRect.sizeDelta = new Vector2(460f, 60f);

        Image imgBtnNext = btnNextGO.AddComponent<Image>();
        imgBtnNext.sprite = RuntimeGlassPillSprite;
        imgBtnNext.type = Image.Type.Sliced;
        imgBtnNext.color = new Color(0.0f, 0.78f, 0.96f, 1.0f);

        Outline btnNextOutline = btnNextGO.AddComponent<Outline>();
        btnNextOutline.effectColor = new Color(1.0f, 0.88f, 0.32f, 0.90f);
        btnNextOutline.effectDistance = new Vector2(2.5f, -2.5f);

        Button btnNext = btnNextGO.AddComponent<Button>();
        btnNext.targetGraphic = imgBtnNext;
        btnNext.onClick.AddListener(OnUserClickedContinueToObjectives);

        Text txtBtnNext = CreateStyledText(
            btnNextGO.transform,
            "Label_BtnNext",
            "SIGUIENTE: OBJETIVOS DE LA APP   ▶",
            Vector2.zero,
            new Vector2(440f, 54f),
            20,
            FontStyle.Bold,
            new Color(0.02f, 0.09f, 0.16f, 1f));
        txtBtnNext.raycastTarget = false;

        solarCyclePanel.SetActive(false);

        // ====================================================================
        // PANTALLA 3: OBJETIVOS E IMPACTO SOCIAL DEL PROYECTO
        // ====================================================================
        objectivesPanel = new GameObject("ObjectivesScreen_Stage3");
        objectivesPanel.transform.SetParent(splashPanel.transform, false);

        RectTransform rtObjPanel = objectivesPanel.AddComponent<RectTransform>();
        rtObjPanel.anchorMin = Vector2.zero;
        rtObjPanel.anchorMax = Vector2.one;
        rtObjPanel.sizeDelta = Vector2.zero;

        Image objBg = objectivesPanel.AddComponent<Image>();
        objBg.sprite = blueprintBgSprite;
        objBg.type = Image.Type.Simple;
        objBg.color = Color.white;
        objBg.raycastTarget = true;

        objectivesCanvasGroup = objectivesPanel.AddComponent<CanvasGroup>();
        objectivesCanvasGroup.alpha = 0f;
        objectivesCanvasGroup.blocksRaycasts = true;
        objectivesCanvasGroup.interactable = true;

        GameObject objCard = CreateSolarFramedCard(
            objectivesPanel.transform,
            "ObjectivesSolarCard",
            new Vector2(1060f, 680f),
            out objectivesOuterGlow,
            out objectivesRadialBorder);
        objectivesRadialBorder.fillAmount = 1f;

        CreateStyledText(
            objCard.transform,
            "Tag_ContextoONG",
            "🌱  ONG DE ENERGÍAS RENOVABLES  •  COMUNIDADES RURALES DE YUCATÁN",
            new Vector2(0f, 286f),
            new Vector2(920f, 28f),
            14,
            FontStyle.Bold,
            new Color(0.0f, 0.92f, 1.0f, 1f));

        Text objTitle = CreateStyledText(
            objCard.transform,
            "Title_Objetivos",
            "PROPÓSITO Y OBJETIVOS DE LA APLICACIÓN",
            new Vector2(0f, 244f),
            new Vector2(940f, 44f),
            30,
            FontStyle.Bold,
            new Color(1.0f, 0.88f, 0.30f, 1f));
        AddGlowShadow(objTitle.gameObject, new Color(1.0f, 0.65f, 0.0f, 0.4f), new Vector2(0f, -2f));

        GameObject missionBox = new GameObject("MissionBanner");
        missionBox.transform.SetParent(objCard.transform, false);
        RectTransform rtMission = missionBox.AddComponent<RectTransform>();
        rtMission.anchoredPosition = new Vector2(0f, 162f);
        rtMission.sizeDelta = new Vector2(960f, 88f);
        Image imgMission = missionBox.AddComponent<Image>();
        imgMission.sprite = RuntimeGlassPillSprite;
        imgMission.type = Image.Type.Sliced;
        imgMission.color = new Color(0.06f, 0.16f, 0.28f, 0.96f);
        Outline missionOutline = missionBox.AddComponent<Outline>();
        missionOutline.effectColor = new Color(0.0f, 0.82f, 1.0f, 0.45f);
        missionOutline.effectDistance = new Vector2(1.5f, -1.5f);

        CreateStyledText(
            missionBox.transform,
            "Text_Mission",
            "Reducir la brecha energética y tecnológica en comunidades vulnerables de Yucatán.\n" +
            "Esta herramienta impulsa el ahorro económico familiar mediante energía limpia y capacita de forma práctica\n" +
            "a futuros técnicos instaladores locales para fomentar el empleo verde autónomo.",
            Vector2.zero,
            new Vector2(920f, 78f),
            16,
            FontStyle.Normal,
            new Color(0.92f, 0.97f, 1.0f, 1f));

        CreateObjectivePillarCard(
            objCard.transform,
            new Vector2(-325f, -20f),
            new Vector2(305f, 225f),
            new Color(1.0f, 0.80f, 0.20f, 1f),
            "☀️  1. SIMULADOR SOLAR",
            "Control de Horario (6 AM - 6 PM)",
            "Arrastra el dial interactivo para simular el paso del sol (amanecer, cenit y atardecer) y analizar cómo las sombras afectan la eficiencia del panel.");

        CreateObjectivePillarCard(
            objCard.transform,
            new Vector2(0f, -20f),
            new Vector2(305f, 225f),
            new Color(0.0f, 0.88f, 1.0f, 1f),
            "🧩  2. ANATOMÍA 3D",
            "Inspección por Capas (0% - 100%)",
            "Separa virtualmente la Malla Metálica, el Cristal Protector, la Celda Captadora y el Chasis para aprender su estructura sin desmontar equipo real.");

        CreateObjectivePillarCard(
            objCard.transform,
            new Vector2(325f, -20f),
            new Vector2(305f, 225f),
            new Color(0.20f, 0.95f, 0.55f, 1f),
            "⚡  3. FLUJO ELÉCTRICO",
            "Mapeo del Circuito Completo",
            "Activa el interruptor maestro para visualizar cómo viaja la energía desde los Paneles (DC), pasando por el Inversor (AC) y la Batería de 12V hasta el hogar.");

        CreateStyledText(
            objCard.transform,
            "Text_Accesibilidad",
            "📱  Diseño Accesible: Controles táctiles intuitivos, cámara suave 360° sin mareo visual y optimización ligera para Android.",
            new Vector2(0f, -168f),
            new Vector2(940f, 28f),
            14,
            FontStyle.Italic,
            new Color(0.68f, 0.84f, 0.96f, 0.95f));

        GameObject btnStartGO = new GameObject("Btn_StartExperience");
        btnStartGO.transform.SetParent(objCard.transform, false);
        startButtonRect = btnStartGO.AddComponent<RectTransform>();
        startButtonRect.anchoredPosition = new Vector2(0f, -242f);
        startButtonRect.sizeDelta = new Vector2(460f, 64f);

        Image imgBtn = btnStartGO.AddComponent<Image>();
        imgBtn.sprite = RuntimeGlassPillSprite;
        imgBtn.type = Image.Type.Sliced;
        imgBtn.color = new Color(0.12f, 0.86f, 0.46f, 1.0f);

        Outline btnOutline = btnStartGO.AddComponent<Outline>();
        btnOutline.effectColor = new Color(1.0f, 0.90f, 0.35f, 0.90f);
        btnOutline.effectDistance = new Vector2(2.5f, -2.5f);

        Button btnStart = btnStartGO.AddComponent<Button>();
        btnStart.targetGraphic = imgBtn;
        btnStart.onClick.AddListener(OnUserClickedStartExperience);

        Text txtBtn = CreateStyledText(
            btnStartGO.transform,
            "Label_Btn",
            "▶   EXPLORAR SISTEMA SOLAR 3D",
            Vector2.zero,
            new Vector2(440f, 58f),
            21,
            FontStyle.Bold,
            new Color(0.03f, 0.12f, 0.08f, 1f));
        txtBtn.raycastTarget = false;

        objectivesPanel.SetActive(false);
    }

    private GameObject CreateSolarFramedCard(
        Transform parent,
        string name,
        Vector2 size,
        out Image outerGlowImg,
        out Image radialChargeBorderImg)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rtRoot = root.AddComponent<RectTransform>();
        rtRoot.anchoredPosition = Vector2.zero;
        rtRoot.sizeDelta = size;

        // 1. Marco exterior base en Cian Fotovoltaico (Sliced nítido)
        GameObject outerGlowGO = new GameObject("SolarFrame_OuterGlow");
        outerGlowGO.transform.SetParent(root.transform, false);
        RectTransform rtGlow = outerGlowGO.AddComponent<RectTransform>();
        rtGlow.anchorMin = Vector2.zero;
        rtGlow.anchorMax = Vector2.one;
        rtGlow.sizeDelta = new Vector2(12f, 12f);
        outerGlowImg = outerGlowGO.AddComponent<Image>();
        outerGlowImg.sprite = RuntimeGlassPillSprite;
        outerGlowImg.type = Image.Type.Sliced;
        outerGlowImg.color = new Color(0.0f, 0.85f, 1.0f, 0.85f);
        outerGlowImg.raycastTarget = false;

        // 2. Capa Perimetral Radial que se carga de luz dorada (0% -> 100%)
        GameObject radialGO = new GameObject("SolarFrame_RadialCharge");
        radialGO.transform.SetParent(root.transform, false);
        RectTransform rtRadial = radialGO.AddComponent<RectTransform>();
        rtRadial.anchorMin = Vector2.zero;
        rtRadial.anchorMax = Vector2.one;
        rtRadial.sizeDelta = new Vector2(10f, 10f);
        radialChargeBorderImg = radialGO.AddComponent<Image>();
        radialChargeBorderImg.sprite = solarFrameGlowSprite;
        radialChargeBorderImg.type = Image.Type.Filled;
        radialChargeBorderImg.fillMethod = Image.FillMethod.Radial360;
        radialChargeBorderImg.fillOrigin = (int)Image.Origin360.Top;
        radialChargeBorderImg.fillClockwise = true;
        radialChargeBorderImg.fillAmount = 0f;
        radialChargeBorderImg.color = new Color(1.0f, 0.86f, 0.22f, 1.0f);
        radialChargeBorderImg.raycastTarget = false;

        // 3. Fondo interior oscuro OPACO tipo Celda de Silicio Fotovoltaico
        // Cubre todo el interior dejando visible únicamente el borde brillante de 6px alrededor
        GameObject innerCard = new GameObject("InnerSiliconPanel");
        innerCard.transform.SetParent(root.transform, false);
        RectTransform rtInner = innerCard.AddComponent<RectTransform>();
        rtInner.anchorMin = Vector2.zero;
        rtInner.anchorMax = Vector2.one;
        rtInner.sizeDelta = new Vector2(-4f, -4f);
        Image imgInner = innerCard.AddComponent<Image>();
        imgInner.sprite = RuntimeGlassPillSprite;
        imgInner.type = Image.Type.Sliced;
        imgInner.color = new Color(0.04f, 0.10f, 0.19f, 1.0f);
        imgInner.raycastTarget = false;

        return root;
    }

    private void CreateObjectivePillarCard(
        Transform parent,
        Vector2 pos,
        Vector2 size,
        Color accentColor,
        string title,
        string subtitle,
        string body)
    {
        GameObject card = new GameObject("Pillar_" + title);
        card.transform.SetParent(parent, false);
        RectTransform rt = card.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image bg = card.AddComponent<Image>();
        bg.sprite = RuntimeGlassPillSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.15f, 0.26f, 0.96f);

        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.65f);
        outline.effectDistance = new Vector2(2f, -2f);

        float halfH = size.y * 0.5f;

        GameObject topAccent = new GameObject("TopAccent");
        topAccent.transform.SetParent(card.transform, false);
        RectTransform rtAcc = topAccent.AddComponent<RectTransform>();
        rtAcc.anchoredPosition = new Vector2(0f, halfH - 8f);
        rtAcc.sizeDelta = new Vector2(size.x - 28f, 5f);
        Image imgAcc = topAccent.AddComponent<Image>();
        imgAcc.color = accentColor;

        CreateStyledText(
            card.transform,
            "Title",
            title,
            new Vector2(0f, halfH - 32f),
            new Vector2(size.x - 20f, 28f),
            size.y < 195f ? 16 : 18,
            FontStyle.Bold,
            accentColor);

        CreateStyledText(
            card.transform,
            "Subtitle",
            subtitle,
            new Vector2(0f, halfH - 58f),
            new Vector2(size.x - 20f, 22f),
            12,
            FontStyle.Bold,
            new Color(0.95f, 0.98f, 1.0f, 0.95f));

        CreateStyledText(
            card.transform,
            "Body",
            body,
            new Vector2(0f, -24f),
            new Vector2(size.x - 26f, size.y - 84f),
            size.y < 195f ? 13 : 14,
            FontStyle.Normal,
            new Color(0.80f, 0.89f, 0.97f, 0.95f));
    }

    private Text CreateStyledText(
        Transform parent,
        string name,
        string content,
        Vector2 anchoredPos,
        Vector2 size,
        int fontSize,
        FontStyle style,
        Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Text txt = go.AddComponent<Text>();
        txt.text = content;
        txt.font = (style == FontStyle.Bold || style == FontStyle.BoldAndItalic) ? ModernFontBold : ModernFontRegular;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = color;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        return txt;
    }

    private void AddGlowShadow(GameObject go, Color shadowCol, Vector2 dist)
    {
        Shadow s = go.AddComponent<Shadow>();
        s.effectColor = shadowCol;
        s.effectDistance = dist;
    }

    private Sprite FindOrLoadLogoSprite(string childName, string assetPath)
    {
        Image[] imgs = GetComponentsInChildren<Image>(true);
        foreach (Image i in imgs)
        {
            if (i != null && i.gameObject.name == childName && i.sprite != null)
            {
                return i.sprite;
            }
        }

        string resourceName = Path.GetFileNameWithoutExtension(assetPath);
        Sprite resSprite = Resources.Load<Sprite>(resourceName);
        if (resSprite != null) return resSprite;

        Texture2D resTex = Resources.Load<Texture2D>(resourceName);
        if (resTex != null)
        {
            return Sprite.Create(resTex, new Rect(0, 0, resTex.width, resTex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        if (File.Exists(assetPath))
        {
            byte[] bytes = File.ReadAllBytes(assetPath);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        return null;
    }

    private void Start()
    {
        StartCoroutine(IntroSequenceRoutine());
    }

    private void Update()
    {
        if ((!waitingForSolarCycleContinue && !waitingForObjectivesContinue) || isTransitioningToObjectives || isExitingObjectives)
            return;

        // Animación continua del Marco de Celda Solar en las pantallas 2 y 3
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
        Color glowA = new Color(0.0f, 0.85f, 1.0f, 0.75f);
        Color glowB = new Color(1.0f, 0.85f, 0.20f, 0.95f);
        Color radA = new Color(1.0f, 0.86f, 0.18f, 1.0f);
        Color radB = new Color(0.15f, 0.95f, 1.0f, 1.0f);

        if (solarCycleOuterGlow != null) solarCycleOuterGlow.color = Color.Lerp(glowA, glowB, pulse);
        if (solarCycleRadialBorder != null) solarCycleRadialBorder.color = Color.Lerp(radA, radB, 1f - pulse);
        if (objectivesOuterGlow != null) objectivesOuterGlow.color = Color.Lerp(glowA, glowB, pulse);
        if (objectivesRadialBorder != null) objectivesRadialBorder.color = Color.Lerp(radA, radB, 1f - pulse);

        // Detección directa táctil / mouse / teclado para los botones de avance
        bool isPressed = false;
        Vector2 pointerPos = Vector2.zero;

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            isPressed = true;
            pointerPos = Touchscreen.current.primaryTouch.position.ReadValue();
        }
        else if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            isPressed = true;
            pointerPos = Mouse.current.position.ReadValue();
        }

        if (isPressed && !wasPointerPressed)
        {
            if (waitingForSolarCycleContinue && nextToObjectivesButtonRect != null)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(nextToObjectivesButtonRect, pointerPos, null, out Vector2 localPt))
                {
                    Rect r = nextToObjectivesButtonRect.rect;
                    r.xMin -= 24f;
                    r.xMax += 24f;
                    r.yMin -= 16f;
                    r.yMax += 16f;
                    if (r.Contains(localPt))
                    {
                        wasPointerPressed = true;
                        OnUserClickedContinueToObjectives();
                        return;
                    }
                }
            }
            else if (waitingForObjectivesContinue && startButtonRect != null)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(startButtonRect, pointerPos, null, out Vector2 localPt))
                {
                    Rect r = startButtonRect.rect;
                    r.xMin -= 24f;
                    r.xMax += 24f;
                    r.yMin -= 16f;
                    r.yMax += 16f;
                    if (r.Contains(localPt))
                    {
                        wasPointerPressed = true;
                        OnUserClickedStartExperience();
                        return;
                    }
                }
            }
        }

        wasPointerPressed = isPressed;

        if (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
        {
            if (waitingForSolarCycleContinue)
                OnUserClickedContinueToObjectives();
            else if (waitingForObjectivesContinue)
                OnUserClickedStartExperience();
        }
    }

    public void OnUserClickedContinueToObjectives()
    {
        if (isTransitioningToObjectives || !waitingForSolarCycleContinue) return;
        waitingForSolarCycleContinue = false;
        isTransitioningToObjectives = true;
        StartCoroutine(TransitionSolarCycleToObjectivesRoutine());
    }

    public void OnUserClickedStartExperience()
    {
        if (isExitingObjectives || !waitingForObjectivesContinue) return;
        waitingForObjectivesContinue = false;
        isExitingObjectives = true;
        StartCoroutine(ExitObjectivesToMainHUDRoutine());
    }

    private IEnumerator IntroSequenceRoutine()
    {
        // --------------------------------------------------------------------
        // ETAPA 1: PANTALLA SPLASH CON CELDA SOLAR CARGÁNDOSE DE LUZ (0% -> 100%)
        // --------------------------------------------------------------------
        float elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / displayDuration);
            float smoothCharge = Mathf.SmoothStep(0f, 1f, progress);

            if (splashRadialBorderFill != null)
            {
                splashRadialBorderFill.fillAmount = smoothCharge;
                splashRadialBorderFill.color = Color.Lerp(
                    new Color(0.0f, 0.90f, 1.0f, 1f),
                    new Color(1.0f, 0.86f, 0.22f, 1f),
                    smoothCharge);
            }

            if (splashOuterGlowBorder != null)
            {
                float glowPulse = 0.55f + 0.45f * Mathf.Sin(elapsed * 6.0f);
                splashOuterGlowBorder.color = Color.Lerp(
                    new Color(0.0f, 0.78f, 1.0f, 0.55f),
                    new Color(1.0f, 0.84f, 0.20f, 0.95f),
                    smoothCharge * glowPulse);
            }

            if (splashProgressFill != null)
            {
                splashProgressFill.fillAmount = smoothCharge;
            }

            if (splashChargeStatusText != null)
            {
                int pct = Mathf.RoundToInt(smoothCharge * 100f);
                splashChargeStatusText.text = pct < 100
                    ? $"⚡ CAPTANDO RADIACIÓN SOLAR EN CELDAS FOTOVOLTAICAS... {pct}%"
                    : "☀️ ¡CELDA FOTOVOLTAICA CARGADA AL 100%! INICIANDO SISTEMA...";
            }

            yield return null;
        }

        // Cross-fade suave de Pantalla 1 (SplashVisualRoot) -> Pantalla 2 (SolarCyclePanel)
        if (solarCyclePanel != null)
        {
            solarCyclePanel.SetActive(true);
            solarCycleCanvasGroup.alpha = 0f;
            solarCycleCanvasGroup.blocksRaycasts = true;
        }

        float fadeElapsed = 0f;
        while (fadeElapsed < fadeDuration)
        {
            fadeElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeElapsed / fadeDuration));

            if (splashVisualCanvasGroup != null) splashVisualCanvasGroup.alpha = 1f - t;
            if (solarCycleCanvasGroup != null) solarCycleCanvasGroup.alpha = t;

            yield return null;
        }

        if (splashVisualRoot != null)
        {
            splashVisualRoot.SetActive(false);
        }

        if (solarCycleCanvasGroup != null)
        {
            solarCycleCanvasGroup.alpha = 1f;
            solarCycleCanvasGroup.blocksRaycasts = true;
        }

        waitingForSolarCycleContinue = true;
        wasPointerPressed = false;
    }

    private IEnumerator TransitionSolarCycleToObjectivesRoutine()
    {
        if (objectivesPanel != null)
        {
            objectivesPanel.SetActive(true);
            objectivesCanvasGroup.alpha = 0f;
            objectivesCanvasGroup.blocksRaycasts = true;
        }

        float fadeElapsed = 0f;
        while (fadeElapsed < fadeDuration)
        {
            fadeElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeElapsed / fadeDuration));

            if (solarCycleCanvasGroup != null) solarCycleCanvasGroup.alpha = 1f - t;
            if (objectivesCanvasGroup != null) objectivesCanvasGroup.alpha = t;

            yield return null;
        }

        if (solarCyclePanel != null)
        {
            solarCyclePanel.SetActive(false);
        }

        if (objectivesCanvasGroup != null)
        {
            objectivesCanvasGroup.alpha = 1f;
            objectivesCanvasGroup.blocksRaycasts = true;
        }

        isTransitioningToObjectives = false;
        waitingForObjectivesContinue = true;
        wasPointerPressed = false;
    }

    private IEnumerator ExitObjectivesToMainHUDRoutine()
    {
        if (mainUIPanel != null)
        {
            mainUIPanel.SetActive(true);
        }

        float outElapsed = 0f;
        while (outElapsed < fadeDuration)
        {
            outElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outElapsed / fadeDuration));

            if (objectivesCanvasGroup != null)
            {
                objectivesCanvasGroup.alpha = 1f - t;
            }

            yield return null;
        }

        if (objectivesCanvasGroup != null)
        {
            objectivesCanvasGroup.alpha = 0f;
            objectivesCanvasGroup.blocksRaycasts = false;
        }
        if (objectivesPanel != null)
        {
            objectivesPanel.SetActive(false);
        }

        if (splashCanvasGroup != null)
        {
            splashCanvasGroup.alpha = 0f;
            splashCanvasGroup.blocksRaycasts = false;
        }

        IsIntroActive = false;
        onSplashCompleted?.Invoke();

        // Ahora sí podemos desactivar el contenedor raíz tras completar las 2 pantallas
        if (splashPanel != null)
        {
            splashPanel.SetActive(false);
        }
    }

    private void ApplyMobileSafeArea(RectTransform panel)
    {
        if (panel == null || Screen.width <= 0 || Screen.height <= 0) return;

        // Evitar aplicar un SafeArea vertical transitorio si el dispositivo aún está rotando a horizontal
        if (Screen.width < Screen.height)
        {
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            return;
        }

        Rect safe = Screen.safeArea;
        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;

        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        if (min.x >= 0f && min.y >= 0f && max.x <= 1f && max.y <= 1f && (max.x - min.x) > 0.7f)
        {
            panel.anchorMin = min;
            panel.anchorMax = max;
        }
    }

    // ========================================================================
    // GENERADORES DE SPRITES PROCEDURALES (BLUEPRINT + CELDA SOLAR CARGÁNDOSE)
    // ========================================================================

    private static Sprite CreateBlueprintGridSprite()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[size * size];

        Color deepNavy = new Color(0.025f, 0.065f, 0.125f, 1f);
        Color blueprintBlue = new Color(0.055f, 0.145f, 0.265f, 1f);
        Color cyanGrid = new Color(0.0f, 0.78f, 1.0f, 1f);

        for (int y = 0; y < size; y++)
        {
            float vy = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float vx = (float)x / size;
                float radial = Vector2.Distance(new Vector2(vx, vy), new Vector2(0.5f, 0.5f));
                Color bg = Color.Lerp(blueprintBlue, deepNavy, Mathf.Clamp01(radial * 1.35f));

                bool isMajorGrid = (x % 32 == 0) || (y % 32 == 0);
                bool isMinorGrid = (x % 8 == 0) || (y % 8 == 0);

                if (isMajorGrid)
                {
                    bg = Color.Lerp(bg, cyanGrid, 0.14f);
                }
                else if (isMinorGrid)
                {
                    bg = Color.Lerp(bg, cyanGrid, 0.045f);
                }

                colors[y * size + x] = bg;
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateSolarCellFrameSprite()
    {
        int w = 256;
        int h = 168;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Pistas conductoras de celda solar (busbars) a lo largo del borde exterior
                bool isBusbar = (x % 16 == 0) || (y % 16 == 0);
                float intensity = isBusbar ? 1.0f : 0.85f;
                colors[y * w + x] = new Color(1f, 1f, 1f, intensity);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateSolarCellBarSprite()
    {
        int w = 256;
        int h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool isCellGap = (x % 16 == 0) || (x % 16 == 1);
                bool isFingerLine = (y % 8 == 0);
                float brightness = isCellGap ? 0.15f : (isFingerLine ? 0.82f : 1.0f);
                colors[y * w + x] = new Color(brightness, brightness, brightness, 1f);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateRoundedPillSprite()
    {
        int size = 64;
        int radius = 18;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Max(0, Mathf.Max(radius - x, x - (size - 1 - radius)));
                int dy = Mathf.Max(0, Mathf.Max(radius - y, y - (size - 1 - radius)));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private static Sprite CreateCircleSprite()
    {
        int size = 64;
        float radius = size * 0.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - radius + 0.5f;
                float dy = y - radius + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateSunIconSprite()
    {
        int size = 64;
        float center = size * 0.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float discAlpha = Mathf.Clamp01(14f - dist);
                float angle = Mathf.Atan2(dy, dx);
                float rayPattern = Mathf.Pow(Mathf.Cos(angle * 4f), 6f);
                float rayAlpha = Mathf.Clamp01(28f - dist) * Mathf.Clamp01(dist - 11f) * rayPattern;

                float finalAlpha = Mathf.Max(discAlpha, rayAlpha);
                colors[y * size + x] = new Color(1.0f, 0.84f, 0.22f, finalAlpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateSunArcSprite()
    {
        int w = 512;
        int h = 180;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] colors = new Color[w * h];

        Vector2 arcCenter = new Vector2(w * 0.5f, 18f);
        float arcRadius = 135f;
        float thickness = 4.0f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 pt = new Vector2(x, y);
                float dist = Vector2.Distance(pt, arcCenter);
                float distToRing = Mathf.Abs(dist - arcRadius);
                float alpha = Mathf.Clamp01(thickness - distToRing);

                float angle = Mathf.Atan2(y - arcCenter.y, x - arcCenter.x);
                float dash = Mathf.Sin(angle * 36f) > -0.2f ? 1f : 0.15f;

                if (y < 18) alpha = 0f;
                colors[y * w + x] = new Color(1.0f, 0.82f, 0.35f, alpha * dash * 0.75f);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
