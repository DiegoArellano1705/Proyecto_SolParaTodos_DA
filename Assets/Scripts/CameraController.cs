using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controlador de cámara 360° interactivo para SolParaTodos (URP / Android).
/// 1. En el Patio: Permite girar libremente 360° (arrastrando con Mouse/Touch o con teclas WASD/Flechas)
///    para ver todos los paneles solares, los cables, el inversor, la batería y la casa rural alrededor.
/// 2. En Modo 'Ver Capas' (Despiece): Se acerca al panel solar, muestra el Slider Vertical de Capas
///    y permite orbitar 3D alrededor de las capas separadas arrastrando en pantalla.
/// 3. Incluye puente directo de entrada táctil/mouse para Sliders, Botones y Toggles para garantizar
///    respuesta instantánea al 100% sin bloqueos de EventSystem.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform defaultInspectTarget;

    [Header("Posición Inmersiva en el Centro del Patio (Vista 360°)")]
    [Tooltip("Ubicado en el corazón del patio para rodearse de los paneles, inversor, batería y casa.")]
    [SerializeField] private Vector3 courtyardPosition = new Vector3(9.5f, 4.2f, -10.5f);

    [Tooltip("Orientación inicial panorámica viendo paneles e inversor.")]
    [SerializeField] private Vector3 courtyardEuler = new Vector3(8f, -32f, 0f);

    [Tooltip("Campo de visión amplio (FOV) para sensación inmersiva dentro de la escena.")]
    [SerializeField] private float defaultFOV = 68f;

    [Header("Parámetros de Inspección de Capas (Bosquejo AR 4)")]
    [SerializeField] private float inspectDistance = 3.1f;
    [SerializeField] private float transitionDuration = 1.0f;

    [Header("Vista del Pasillo de Paneles y Cableado")]
    [Tooltip("Inicio del pasillo amarillo (Camino 2) viendo las filas de paneles y cables.")]
    [SerializeField] private Vector3 walkwayStartPos = new Vector3(4.8f, 3.15f, -10.5f);
    [Tooltip("Final del pasillo amarillo al fondo de la tercera fila de paneles.")]
    [SerializeField] private Vector3 walkwayEndPos = new Vector3(-8.2f, 2.75f, -10.5f);
    [Tooltip("Ángulo inicial en el pasillo (encuadre hacia las filas y el pasillo amarillo).")]
    [SerializeField] private Vector3 walkwayStartEuler = new Vector3(15f, -78f, 0f);
    [SerializeField] private float walkwayFOV = 60f;

    [Header("Sensibilidad de Giro 360°")]
    [SerializeField] private bool enableLookAround = true;
    [SerializeField] private float dragSensitivity = 0.25f;
    [SerializeField] private float keyboardRotSpeed = 80f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 55f;
    [SerializeField] private float rotationDamping = 14f;

    [Header("UI Feedback")]
    [SerializeField] private Text toggleButtonText;

    [Header("Eventos")]
    public UnityEvent onTransitionStarted;
    public UnityEvent onTransitionFinished;
    public UnityEvent<bool> onZoomStateChanged;

    private Coroutine activeTransitionRoutine;
    private bool isZoomedIn = false;
    private bool isInWalkwayMode = false;
    private bool isTransitioning = false;
    private float walkwayProgress = 0f;

    private bool wasPressedLastFrame = false;
    private bool isInteractingWithUI = false;
    private Slider activeDraggedSlider = null;

    private Slider[] cachedSliders;
    private Button modeToggleButton;
    private Button walkwayToggleButton;
    private Text walkwayButtonText;
    private GameObject walkwayDock;
    private Slider walkwaySlider;
    private Toggle energyFlowToggle;
    private GameObject layersPanelRight;
    private EnergyFlowController energyController;
    private PanelExploder panelExploder;

    private float lastModeToggleTime = -10f;
    private float lastWalkwayToggleTime = -10f;
    private float lastEnergyToggleTime = -10f;

    // Ángulos en modo Patio 360°
    private float currentYaw;
    private float currentPitch;
    private float targetYaw;
    private float targetPitch;
    private float currentFOV;
    private float targetFOV;

    // Ángulos de órbita en modo Inspección de Capas
    private Vector3 inspectCenter;
    private float orbitYaw = -50f;
    private float orbitPitch = 25f;
    private float targetOrbitYaw = -50f;
    private float targetOrbitPitch = 25f;

    public bool IsZoomedIn => isZoomedIn;
    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        AppFlowManager.EnsureNewInputSystemModule();

        if (targetCamera == null) targetCamera = Camera.main;

        courtyardPosition = new Vector3(9.5f, 4.2f, -10.5f);
        courtyardEuler = new Vector3(8f, -32f, 0f);

        GameObject frontalPanel = GameObject.Find("CeldaFotovoltaica (3)");
        if (frontalPanel != null) defaultInspectTarget = frontalPanel.transform;

        if (targetCamera != null)
        {
            targetCamera.allowMSAA = true;
            targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
            targetCamera.ResetAspect();
            targetCamera.transform.position = courtyardPosition;
            targetCamera.transform.rotation = Quaternion.Euler(courtyardEuler);
            targetCamera.fieldOfView = defaultFOV;

            UniversalAdditionalCameraData camData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                camData.antialiasingQuality = AntialiasingQuality.High;
            }

            currentYaw = courtyardEuler.y;
            currentPitch = courtyardEuler.x;
            targetYaw = currentYaw;
            targetPitch = currentPitch;
            currentFOV = defaultFOV;
            targetFOV = defaultFOV;
        }

        // Configurar Anti-Aliasing seguro para GPU móvil (2x MSAA + FXAA sin HDR para evitar corrupción de tile stride) y 4x en PC
#if UNITY_ANDROID && !UNITY_EDITOR
        QualitySettings.antiAliasing = 2;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
        {
            urpAsset.msaaSampleCount = 2;
            urpAsset.supportsHDR = false;
            urpAsset.renderScale = 1.0f;
        }
#else
        QualitySettings.antiAliasing = 4;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
        {
            urpAsset.msaaSampleCount = 4;
            urpAsset.renderScale = 1.0f;
        }
#endif
    }

    private void Start()
    {
        CacheAndWireUIElements();
    }

    private void CacheAndWireUIElements()
    {
        // Incluir objetos inactivos (como VerticalSlider_Layers dentro de LayersPanel_Right)
        cachedSliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        energyController = FindFirstObjectByType<EnergyFlowController>();
        panelExploder = FindFirstObjectByType<PanelExploder>();

        Button[] allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Button b in allButtons)
        {
            if (b != null && b.gameObject.name == "Btn_ToggleMode")
            {
                modeToggleButton = b;
                b.onClick.RemoveListener(ToggleInspectMode);
                b.onClick.AddListener(ToggleInspectMode);

                if (toggleButtonText == null)
                {
                    toggleButtonText = b.GetComponentInChildren<Text>(true);
                }
            }
        }

        Toggle[] allToggles = FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Toggle t in allToggles)
        {
            if (t != null && t.gameObject.name == "Toggle_FlujoElectrico")
            {
                energyFlowToggle = t;
            }
        }

        RectTransform[] allRects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform mainUITransform = null;
        foreach (RectTransform rt in allRects)
        {
            if (rt == null) continue;
            if (rt.gameObject.name == "LayersPanel_Right")
            {
                layersPanelRight = rt.gameObject;
            }
            else if (rt.gameObject.name == "MainUIPanel")
            {
                mainUITransform = rt;
            }
        }

        if (mainUITransform != null)
        {
            EnsureWalkwayUIElements(mainUITransform);
            // Actualizar lista de sliders para incluir el deslizador del pasillo
            cachedSliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }

    private void EnsureWalkwayUIElements(Transform mainUI)
    {
        Font boldFont = AppFlowManager.ModernFontBold;
        Font regFont = AppFlowManager.ModernFontRegular;
        Sprite pillSprite = AppFlowManager.RuntimeGlassPillSprite;
        Sprite knobSprite = AppFlowManager.RuntimeCircleKnobSprite;

        // 1. Botón "🚶 Pasillo y Cableado" junto a "🧩 Ver Capas (Despiece)"
        Transform existingBtn = mainUI.Find("Btn_WalkwayMode");
        if (existingBtn == null)
        {
            GameObject btnGO = new GameObject("Btn_WalkwayMode");
            btnGO.transform.SetParent(mainUI, false);
            RectTransform rt = btnGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-288f, -22f);
            rt.sizeDelta = new Vector2(248f, 48f);

            Image img = btnGO.AddComponent<Image>();
            img.sprite = pillSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.08f, 0.24f, 0.36f, 0.92f);

            Outline outline = btnGO.AddComponent<Outline>();
            outline.effectColor = new Color(1.0f, 0.78f, 0.18f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            walkwayToggleButton = btnGO.AddComponent<Button>();
            walkwayToggleButton.targetGraphic = img;

            GameObject txtGO = new GameObject("Text");
            txtGO.transform.SetParent(btnGO.transform, false);
            RectTransform rtTxt = txtGO.AddComponent<RectTransform>();
            rtTxt.anchorMin = Vector2.zero;
            rtTxt.anchorMax = Vector2.one;
            rtTxt.offsetMin = new Vector2(8f, 2f);
            rtTxt.offsetMax = new Vector2(-8f, -2f);

            walkwayButtonText = txtGO.AddComponent<Text>();
            walkwayButtonText.text = "🚶 Pasillo y Cableado";
            walkwayButtonText.font = boldFont;
            walkwayButtonText.fontSize = 15;
            walkwayButtonText.fontStyle = FontStyle.Bold;
            walkwayButtonText.alignment = TextAnchor.MiddleCenter;
            walkwayButtonText.color = new Color(1.0f, 0.92f, 0.55f, 1f);
            walkwayButtonText.raycastTarget = false;
        }
        else
        {
            walkwayToggleButton = existingBtn.GetComponent<Button>();
            walkwayButtonText = existingBtn.GetComponentInChildren<Text>(true);
        }

        if (walkwayToggleButton != null)
        {
            walkwayToggleButton.onClick.RemoveListener(ToggleWalkwayMode);
            walkwayToggleButton.onClick.AddListener(ToggleWalkwayMode);
        }

        // 2. Barra flotante para desplazarse a lo largo del pasillo amarillo
        Transform existingDock = mainUI.Find("WalkwayControlDock");
        if (existingDock == null)
        {
            walkwayDock = new GameObject("WalkwayControlDock");
            walkwayDock.transform.SetParent(mainUI, false);
            RectTransform rtDock = walkwayDock.AddComponent<RectTransform>();
            rtDock.anchorMin = new Vector2(0.5f, 1f);
            rtDock.anchorMax = new Vector2(0.5f, 1f);
            rtDock.pivot = new Vector2(0.5f, 1f);
            rtDock.anchoredPosition = new Vector2(0f, -80f);
            rtDock.sizeDelta = new Vector2(470f, 70f);

            Image dockBg = walkwayDock.AddComponent<Image>();
            dockBg.sprite = pillSprite;
            dockBg.type = Image.Type.Sliced;
            dockBg.color = new Color(0.05f, 0.14f, 0.24f, 0.90f);

            Outline dockOutline = walkwayDock.AddComponent<Outline>();
            dockOutline.effectColor = new Color(0.15f, 0.82f, 0.98f, 0.65f);
            dockOutline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject lblGO = new GameObject("Lbl_WalkwayInfo");
            lblGO.transform.SetParent(walkwayDock.transform, false);
            RectTransform rtLbl = lblGO.AddComponent<RectTransform>();
            rtLbl.anchorMin = new Vector2(0f, 0.52f);
            rtLbl.anchorMax = new Vector2(1f, 1f);
            rtLbl.offsetMin = new Vector2(14f, 0f);
            rtLbl.offsetMax = new Vector2(-14f, -4f);

            Text lblTxt = lblGO.AddComponent<Text>();
            lblTxt.text = "🚶 RECORRER PASILLO LATERAL  •  FILAS DE PANELES Y CABLEADO";
            lblTxt.font = boldFont;
            lblTxt.fontSize = 12;
            lblTxt.fontStyle = FontStyle.Bold;
            lblTxt.alignment = TextAnchor.MiddleCenter;
            lblTxt.color = new Color(0.45f, 0.90f, 1.0f, 1f);
            lblTxt.raycastTarget = false;

            // Slider horizontal de recorrido por el pasillo
            GameObject sliderGO = new GameObject("Slider_WalkwayPos");
            sliderGO.transform.SetParent(walkwayDock.transform, false);
            RectTransform rtSlider = sliderGO.AddComponent<RectTransform>();
            rtSlider.anchorMin = new Vector2(0f, 0f);
            rtSlider.anchorMax = new Vector2(1f, 0.55f);
            rtSlider.offsetMin = new Vector2(68f, 8f);
            rtSlider.offsetMax = new Vector2(-68f, -2f);

            GameObject bgTrack = new GameObject("Background");
            bgTrack.transform.SetParent(sliderGO.transform, false);
            RectTransform rtBg = bgTrack.AddComponent<RectTransform>();
            rtBg.anchorMin = new Vector2(0f, 0.35f);
            rtBg.anchorMax = new Vector2(1f, 0.65f);
            rtBg.offsetMin = Vector2.zero;
            rtBg.offsetMax = Vector2.zero;
            Image imgBg = bgTrack.AddComponent<Image>();
            imgBg.sprite = pillSprite;
            imgBg.type = Image.Type.Sliced;
            imgBg.color = new Color(0.15f, 0.28f, 0.40f, 0.95f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform rtFillArea = fillArea.AddComponent<RectTransform>();
            rtFillArea.anchorMin = new Vector2(0f, 0.35f);
            rtFillArea.anchorMax = new Vector2(1f, 0.65f);
            rtFillArea.offsetMin = Vector2.zero;
            rtFillArea.offsetMax = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform rtFill = fill.AddComponent<RectTransform>();
            rtFill.anchorMin = Vector2.zero;
            rtFill.anchorMax = Vector2.one;
            rtFill.offsetMin = Vector2.zero;
            rtFill.offsetMax = Vector2.zero;
            Image imgFill = fill.AddComponent<Image>();
            imgFill.sprite = pillSprite;
            imgFill.type = Image.Type.Sliced;
            imgFill.color = new Color(1.0f, 0.78f, 0.15f, 0.95f);

            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform rtHandleArea = handleArea.AddComponent<RectTransform>();
            rtHandleArea.anchorMin = Vector2.zero;
            rtHandleArea.anchorMax = Vector2.one;
            rtHandleArea.offsetMin = new Vector2(10f, 0f);
            rtHandleArea.offsetMax = new Vector2(-10f, 0f);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform rtHandle = handle.AddComponent<RectTransform>();
            rtHandle.sizeDelta = new Vector2(24f, 24f);
            Image imgHandle = handle.AddComponent<Image>();
            imgHandle.sprite = knobSprite;
            imgHandle.color = new Color(1.0f, 0.86f, 0.25f, 1f);

            walkwaySlider = sliderGO.AddComponent<Slider>();
            walkwaySlider.direction = Slider.Direction.LeftToRight;
            walkwaySlider.minValue = 0f;
            walkwaySlider.maxValue = 1f;
            walkwaySlider.value = 0f;
            walkwaySlider.fillRect = rtFill;
            walkwaySlider.handleRect = rtHandle;
            walkwaySlider.targetGraphic = imgHandle;
            walkwaySlider.onValueChanged.AddListener(SetWalkwayProgress);

            // Etiquetas laterales "Inicio" y "Fondo"
            CreateSideTag(walkwayDock.transform, "Lbl_Start", "Inicio", new Vector2(0f, 0f), new Vector2(0f, 0.55f), new Vector2(10f, 6f), new Vector2(64f, -2f), regFont);
            CreateSideTag(walkwayDock.transform, "Lbl_End", "Fondo", new Vector2(1f, 0f), new Vector2(1f, 0.55f), new Vector2(-64f, 6f), new Vector2(-10f, -2f), regFont);
        }
        else
        {
            walkwayDock = existingDock.gameObject;
            walkwaySlider = existingDock.GetComponentInChildren<Slider>(true);
            if (walkwaySlider != null)
            {
                walkwaySlider.onValueChanged.RemoveListener(SetWalkwayProgress);
                walkwaySlider.onValueChanged.AddListener(SetWalkwayProgress);
            }
        }

        if (walkwayDock != null)
        {
            walkwayDock.SetActive(isInWalkwayMode);
        }
    }

    private void CreateSideTag(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Font font)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        Text t = go.AddComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = 11;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.85f, 0.92f, 1f, 0.85f);
        t.raycastTarget = false;
    }

    private void Update()
    {
        if (AppFlowManager.IsIntroActive || !enableLookAround || isTransitioning || targetCamera == null) return;

        Vector2 delta = ReadRotationAndUIInput(out float keyYaw, out float keyPitch, out float scrollDelta);

        if (!isZoomedIn)
        {
            // MODO PATIO GENERAL O MODO PASILLO DE PANELES: Giro libre 360° + Zoom
            targetYaw += delta.x * dragSensitivity + keyYaw * keyboardRotSpeed * Time.deltaTime;
            targetPitch = Mathf.Clamp(targetPitch - (delta.y * dragSensitivity) + keyPitch * (keyboardRotSpeed * 0.7f) * Time.deltaTime, minPitch, maxPitch);

            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                targetFOV = Mathf.Clamp(targetFOV - scrollDelta * 3f, 35f, 80f);
            }

            currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * rotationDamping);
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * rotationDamping);
            currentFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * rotationDamping);

            Vector3 desiredPos = isInWalkwayMode
                ? Vector3.Lerp(walkwayStartPos, walkwayEndPos, walkwayProgress)
                : courtyardPosition;

            targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, desiredPos, Time.deltaTime * rotationDamping);
            targetCamera.transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            targetCamera.fieldOfView = currentFOV;
        }
        else
        {
            // MODO INSPECCIÓN DE CAPAS: Órbita 3D alrededor del panel despiezado
            targetOrbitYaw += delta.x * dragSensitivity + keyYaw * keyboardRotSpeed * Time.deltaTime;
            targetOrbitPitch = Mathf.Clamp(targetOrbitPitch - (delta.y * dragSensitivity) + keyPitch * (keyboardRotSpeed * 0.7f) * Time.deltaTime, 5f, 75f);

            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                inspectDistance = Mathf.Clamp(inspectDistance - scrollDelta * 0.25f, 1.8f, 5.5f);
            }

            orbitYaw = Mathf.Lerp(orbitYaw, targetOrbitYaw, Time.deltaTime * rotationDamping);
            orbitPitch = Mathf.Lerp(orbitPitch, targetOrbitPitch, Time.deltaTime * rotationDamping);

            Quaternion orbitRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 orbitPos = inspectCenter - (orbitRot * Vector3.forward * inspectDistance);

            targetCamera.transform.position = orbitPos;
            targetCamera.transform.rotation = orbitRot;
        }
    }

    private Vector2 ReadRotationAndUIInput(out float keyYaw, out float keyPitch, out float scrollDelta)
    {
        keyYaw = 0f;
        keyPitch = 0f;
        scrollDelta = 0f;

        Vector2 delta = Vector2.zero;
        Vector2 pointerPos = Vector2.zero;
        bool isPressed = false;
        bool isRightClick = false;

        // 1. Touch en dispositivos móviles Android
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            isPressed = true;
            pointerPos = Touchscreen.current.primaryTouch.position.ReadValue();
            delta = Touchscreen.current.primaryTouch.delta.ReadValue();
        }
        // 2. Mouse (Clic Izquierdo o Clic Derecho)
        else if (Mouse.current != null)
        {
            isRightClick = Mouse.current.rightButton.isPressed;
            if (Mouse.current.leftButton.isPressed || isRightClick)
            {
                isPressed = true;
                pointerPos = Mouse.current.position.ReadValue();
                delta = Mouse.current.delta.ReadValue();
            }

            scrollDelta = Mouse.current.scroll.ReadValue().y * 0.02f;
        }

        // Inicio de toque / clic
        if (isPressed && !wasPressedLastFrame && !isRightClick)
        {
            activeDraggedSlider = FindHoveredActiveSlider(pointerPos);
            if (activeDraggedSlider != null)
            {
                isInteractingWithUI = true;
                UpdateSliderFromScreenPoint(activeDraggedSlider, pointerPos);
            }
            else if (IsPointInsideElement(modeToggleButton != null ? modeToggleButton.GetComponent<RectTransform>() : null, pointerPos, 8f))
            {
                isInteractingWithUI = true;
                ToggleInspectMode();
            }
            else if (IsPointInsideElement(walkwayToggleButton != null ? walkwayToggleButton.GetComponent<RectTransform>() : null, pointerPos, 8f))
            {
                isInteractingWithUI = true;
                ToggleWalkwayMode();
            }
            else if (IsPointInsideElement(energyFlowToggle != null ? energyFlowToggle.GetComponent<RectTransform>() : null, pointerPos, 8f))
            {
                isInteractingWithUI = true;
                TriggerEnergyToggleSafe();
            }
            else
            {
                isInteractingWithUI = false;
            }
        }
        else if (isPressed && activeDraggedSlider != null)
        {
            UpdateSliderFromScreenPoint(activeDraggedSlider, pointerPos);
        }

        if (!isPressed)
        {
            activeDraggedSlider = null;
            isInteractingWithUI = false;
        }

        wasPressedLastFrame = isPressed;

        if (!isPressed || isInteractingWithUI)
        {
            delta = Vector2.zero;
        }

        // 3. Soporte adicional de Teclado (WASD / Flechas)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) keyYaw -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) keyYaw += 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) keyPitch -= 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) keyPitch += 1f;
        }

        return delta;
    }

    private void TriggerEnergyToggleSafe()
    {
        if (Time.unscaledTime - lastEnergyToggleTime < 0.25f) return;
        lastEnergyToggleTime = Time.unscaledTime;

        if (energyFlowToggle != null)
        {
            bool nextState = !energyFlowToggle.isOn;
            energyFlowToggle.SetIsOnWithoutNotify(nextState);
            if (energyController != null)
            {
                energyController.SetEnergyFlow(nextState);
            }
        }
        else if (energyController != null)
        {
            energyController.ToggleEnergyFlow();
        }
    }

    public void NotifyEnergyToggleChanged()
    {
        lastEnergyToggleTime = Time.unscaledTime;
    }

    private Slider FindHoveredActiveSlider(Vector2 screenPos)
    {
        if (cachedSliders == null || cachedSliders.Length < 3)
        {
            cachedSliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        for (int i = 0; i < cachedSliders.Length; i++)
        {
            Slider s = cachedSliders[i];
            if (s != null && s.gameObject.activeInHierarchy)
            {
                RectTransform rt = s.GetComponent<RectTransform>();
                // Padding generoso para facilitar el toque táctil en el slider vertical y horizontal
                float padX = (s.direction == Slider.Direction.BottomToTop || s.direction == Slider.Direction.TopToBottom) ? 38f : 16f;
                float padY = 16f;
                if (IsPointInsideElement(rt, screenPos, padX, padY))
                {
                    return s;
                }
            }
        }
        return null;
    }

    private void UpdateSliderFromScreenPoint(Slider s, Vector2 screenPos)
    {
        if (s == null) return;
        RectTransform rt = s.GetComponent<RectTransform>();
        if (rt == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out Vector2 localPt))
        {
            Rect r = rt.rect;
            float t = 0f;
            if (s.direction == Slider.Direction.LeftToRight)
            {
                t = Mathf.InverseLerp(r.xMin, r.xMax, localPt.x);
            }
            else if (s.direction == Slider.Direction.RightToLeft)
            {
                t = 1f - Mathf.InverseLerp(r.xMin, r.xMax, localPt.x);
            }
            else if (s.direction == Slider.Direction.BottomToTop)
            {
                t = Mathf.InverseLerp(r.yMin, r.yMax, localPt.y);
            }
            else if (s.direction == Slider.Direction.TopToBottom)
            {
                t = 1f - Mathf.InverseLerp(r.yMin, r.yMax, localPt.y);
            }

            float newVal = Mathf.Lerp(s.minValue, s.maxValue, Mathf.Clamp01(t));
            s.value = newVal;

            // Garantizar que el controlador correspondiente reciba el valor inmediatamente
            if (s.gameObject.name.Contains("Layers") || s.direction == Slider.Direction.BottomToTop)
            {
                if (panelExploder == null) panelExploder = FindFirstObjectByType<PanelExploder>();
                if (panelExploder != null) panelExploder.SetExplodeProgress(newVal);
            }
            else if (s.gameObject.name.Contains("Walkway"))
            {
                SetWalkwayProgress(newVal);
            }
        }
    }

    public void SetWalkwayProgress(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        float deltaProgress = clamped - walkwayProgress;
        walkwayProgress = clamped;

        if (walkwaySlider != null && Mathf.Abs(walkwaySlider.value - clamped) > 0.001f)
        {
            walkwaySlider.SetValueWithoutNotify(clamped);
        }

        // Al avanzar por el pasillo, orientar sutilmente la vista hacia las filas y cableado
        if (isInWalkwayMode && Mathf.Abs(deltaProgress) > 0.0001f)
        {
            targetYaw += deltaProgress * 28f;
        }
    }

    private bool IsPointInsideElement(RectTransform rt, Vector2 screenPos, float padX, float padY = -1f)
    {
        if (rt == null || !rt.gameObject.activeInHierarchy) return false;
        if (padY < 0f) padY = padX;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out Vector2 localPt))
        {
            Rect r = rt.rect;
            r.xMin -= padX;
            r.xMax += padX;
            r.yMin -= padY;
            r.yMax += padY;
            return r.Contains(localPt);
        }
        return false;
    }

    public void RotateByDegrees(float degrees)
    {
        if (!isZoomedIn)
            targetYaw += degrees;
        else
            targetOrbitYaw += degrees;
    }

    public void FocusOnDefaultTarget()
    {
        if (defaultInspectTarget != null)
        {
            FocusOnTarget(defaultInspectTarget);
        }
    }

    public void FocusOnTarget(Transform target)
    {
        if (target == null || targetCamera == null || isTransitioning) return;

        inspectCenter = GetActualMeshCenter(target);

        // Posicionar cámara frente a la cara azul inclinada del panel solar
        Vector3 destPos = inspectCenter + new Vector3(2.25f, 1.80f, -1.35f);
        Quaternion destRot = Quaternion.LookRotation(inspectCenter - destPos, Vector3.up);

        Vector3 euler = destRot.eulerAngles;
        orbitPitch = euler.x;
        orbitYaw = euler.y;
        targetOrbitPitch = orbitPitch;
        targetOrbitYaw = orbitYaw;
        inspectDistance = Vector3.Distance(destPos, inspectCenter);

        StartCameraTransition(destPos, destRot, 54f, true, false);
    }

    private Vector3 GetActualMeshCenter(Transform target)
    {
        Transform celda = target.Find("CeldaSolar");
        if (celda != null)
        {
            Renderer r = celda.GetComponent<Renderer>();
            if (r != null) return r.bounds.center;
            return celda.position;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            return b.center;
        }

        return target.position;
    }

    public void ResetCamera()
    {
        if (targetCamera == null || isTransitioning) return;

        currentPitch = courtyardEuler.x;
        currentYaw = courtyardEuler.y;
        targetPitch = courtyardEuler.x;
        targetYaw = courtyardEuler.y;

        Quaternion destRot = Quaternion.Euler(courtyardEuler.x, courtyardEuler.y, 0f);
        StartCameraTransition(courtyardPosition, destRot, defaultFOV, false, false);
    }

    public void EnterWalkwayMode()
    {
        if (targetCamera == null || isTransitioning) return;

        // Si el panel estaba despiezado, cerrarlo suavemente para apreciar las filas limpias
        if (panelExploder == null) panelExploder = FindFirstObjectByType<PanelExploder>();
        if (panelExploder != null && panelExploder.CurrentProgress > 0.01f)
        {
            panelExploder.SetExplodeProgress(0f);
        }

        walkwayProgress = 0f;
        if (walkwaySlider != null) walkwaySlider.SetValueWithoutNotify(0f);

        currentPitch = walkwayStartEuler.x;
        currentYaw = walkwayStartEuler.y;
        targetPitch = walkwayStartEuler.x;
        targetYaw = walkwayStartEuler.y;

        Vector3 destPos = Vector3.Lerp(walkwayStartPos, walkwayEndPos, walkwayProgress);
        Quaternion destRot = Quaternion.Euler(walkwayStartEuler.x, walkwayStartEuler.y, 0f);
        StartCameraTransition(destPos, destRot, walkwayFOV, false, true);
    }

    public void ToggleWalkwayMode()
    {
        if (Time.unscaledTime - lastWalkwayToggleTime < 0.25f) return;
        lastWalkwayToggleTime = Time.unscaledTime;

        if (isInWalkwayMode)
            ResetCamera();
        else
            EnterWalkwayMode();
    }

    public void ToggleInspectMode()
    {
        if (Time.unscaledTime - lastModeToggleTime < 0.25f) return;
        lastModeToggleTime = Time.unscaledTime;

        if (isZoomedIn)
            ResetCamera();
        else
            FocusOnDefaultTarget();
    }

    private void StartCameraTransition(Vector3 targetPos, Quaternion targetRot, float destFOV, bool zoomedState, bool walkwayState)
    {
        if (activeTransitionRoutine != null) StopCoroutine(activeTransitionRoutine);
        activeTransitionRoutine = StartCoroutine(TransitionRoutine(targetPos, targetRot, destFOV, zoomedState, walkwayState));
    }

    private IEnumerator TransitionRoutine(Vector3 endPos, Quaternion endRot, float endFOV, bool targetZoomState, bool targetWalkwayState)
    {
        isTransitioning = true;
        activeDraggedSlider = null;
        isInteractingWithUI = false;

        // Mostrar u ocultar paneles contextuales según el modo destino
        if (layersPanelRight != null)
        {
            layersPanelRight.SetActive(targetZoomState);
        }
        if (walkwayDock != null)
        {
            walkwayDock.SetActive(targetWalkwayState);
        }

        onTransitionStarted?.Invoke();

        Transform camTransform = targetCamera.transform;
        Vector3 startPos = camTransform.position;
        Quaternion startRot = camTransform.rotation;
        float startFOV = targetCamera.fieldOfView;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));

            camTransform.position = Vector3.Lerp(startPos, endPos, t);
            camTransform.rotation = Quaternion.Slerp(startRot, endRot, t);
            targetCamera.fieldOfView = Mathf.Lerp(startFOV, endFOV, t);

            yield return null;
        }

        camTransform.position = endPos;
        camTransform.rotation = endRot;
        targetCamera.fieldOfView = endFOV;
        currentFOV = endFOV;
        targetFOV = endFOV;

        isZoomedIn = targetZoomState;
        isInWalkwayMode = targetWalkwayState;
        isTransitioning = false;
        activeTransitionRoutine = null;

        if (toggleButtonText != null)
        {
            toggleButtonText.text = isZoomedIn ? "↩ Vista Patio 360°" : "🧩 Ver Capas (Despiece)";
        }

        if (walkwayButtonText != null)
        {
            walkwayButtonText.text = isInWalkwayMode ? "↩ Vista Patio 360°" : "🚶 Pasillo y Cableado";
        }

        if (layersPanelRight != null)
        {
            layersPanelRight.SetActive(isZoomedIn);
        }

        if (walkwayDock != null)
        {
            walkwayDock.SetActive(isInWalkwayMode);
        }

        onZoomStateChanged?.Invoke(isZoomedIn);
        onTransitionFinished?.Invoke();
    }
}
