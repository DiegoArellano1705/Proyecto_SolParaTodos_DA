using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.InputSystem;

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
    private bool isTransitioning = false;

    private bool wasPressedLastFrame = false;
    private bool isInteractingWithUI = false;
    private Slider activeDraggedSlider = null;

    private Slider[] cachedSliders;
    private Button modeToggleButton;
    private Toggle energyFlowToggle;
    private GameObject layersPanelRight;
    private EnergyFlowController energyController;
    private PanelExploder panelExploder;

    private float lastModeToggleTime = -10f;
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
            targetCamera.transform.position = courtyardPosition;
            targetCamera.transform.rotation = Quaternion.Euler(courtyardEuler);
            targetCamera.fieldOfView = defaultFOV;

            currentYaw = courtyardEuler.y;
            currentPitch = courtyardEuler.x;
            targetYaw = currentYaw;
            targetPitch = currentPitch;
            currentFOV = defaultFOV;
            targetFOV = defaultFOV;
        }
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
        foreach (RectTransform rt in allRects)
        {
            if (rt != null && rt.gameObject.name == "LayersPanel_Right")
            {
                layersPanelRight = rt.gameObject;
                break;
            }
        }
    }

    private void Update()
    {
        if (AppFlowManager.IsIntroActive || !enableLookAround || isTransitioning || targetCamera == null) return;

        Vector2 delta = ReadRotationAndUIInput(out float keyYaw, out float keyPitch, out float scrollDelta);

        if (!isZoomedIn)
        {
            // MODO PATIO: Giro libre 360° sin límites horizontales + Zoom con rueda del mouse
            targetYaw += delta.x * dragSensitivity + keyYaw * keyboardRotSpeed * Time.deltaTime;
            targetPitch = Mathf.Clamp(targetPitch - (delta.y * dragSensitivity) + keyPitch * (keyboardRotSpeed * 0.7f) * Time.deltaTime, minPitch, maxPitch);

            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                targetFOV = Mathf.Clamp(targetFOV - scrollDelta * 3f, 35f, 80f);
            }

            currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.deltaTime * rotationDamping);
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * rotationDamping);
            currentFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime * rotationDamping);

            targetCamera.transform.position = courtyardPosition;
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
        if (cachedSliders == null || cachedSliders.Length < 2)
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

        StartCameraTransition(destPos, destRot, 54f, true);
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

        Quaternion destRot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        StartCameraTransition(courtyardPosition, destRot, defaultFOV, false);
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

    private void StartCameraTransition(Vector3 targetPos, Quaternion targetRot, float destFOV, bool zoomedState)
    {
        if (activeTransitionRoutine != null) StopCoroutine(activeTransitionRoutine);
        activeTransitionRoutine = StartCoroutine(TransitionRoutine(targetPos, targetRot, destFOV, zoomedState));
    }

    private IEnumerator TransitionRoutine(Vector3 endPos, Quaternion endRot, float endFOV, bool targetZoomState)
    {
        isTransitioning = true;
        activeDraggedSlider = null;
        isInteractingWithUI = false;

        // Mostrar el panel de capas inmediatamente al entrar a inspección
        if (layersPanelRight != null)
        {
            layersPanelRight.SetActive(targetZoomState);
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
        isTransitioning = false;
        activeTransitionRoutine = null;

        if (toggleButtonText != null)
        {
            toggleButtonText.text = isZoomedIn ? "↩ Vista Patio 360°" : "🧩 Ver Capas (Despiece)";
        }

        if (layersPanelRight != null)
        {
            layersPanelRight.SetActive(isZoomedIn);
        }

        onZoomStateChanged?.Invoke(isZoomedIn);
        onTransitionFinished?.Invoke();
    }
}
