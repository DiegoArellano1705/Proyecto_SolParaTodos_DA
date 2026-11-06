using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Funcionalidad 3: Mapeo del Sistema Eléctrico Completo (Captación -> Inversor -> Almacenaje -> Casa).
/// 1. Genera una textura procedural con desplazamiento UV (UV Scrolling) sobre el material del cableado.
/// 2. Anima un pool ligero de pulsos lumínicos 3D (0 GC Alloc, sin sombras) recorriendo físicamente
///    los cables desde los Paneles Solares -> Inversor -> Banco de Baterías (12V) -> Casa Rural.
/// 3. Muestra tarjetas educativas AR flotantes sobre cada equipo 3D en el patio (Slide 5).
/// </summary>
public class EnergyFlowController : MonoBehaviour
{
    [Header("UI Toggle")]
    [Tooltip("Interruptor Maestro (UI Toggle). Si se asigna, se suscribe automáticamente.")]
    [SerializeField] private Toggle masterSwitchToggle;

    [Header("Material y Renderers del Cableado")]
    [Tooltip("Material compartido del cableado (PlasticCableMaterial).")]
    [SerializeField] private Material cableSharedMaterial;

    [Tooltip("Lista opcional de Renderers para control individual mediante MaterialPropertyBlock.")]
    [SerializeField] private Renderer[] cableRenderers;

    [Header("Parámetros Visuales de Corriente")]
    [SerializeField] private Color activeEmissionColor = new Color(1.0f, 0.78f, 0.15f, 1.0f);
    [SerializeField] private Color baseCableColorOff = new Color(0.08f, 0.15f, 0.45f, 1.0f);
    [SerializeField] private Color baseCableColorOn = new Color(0.20f, 0.85f, 1.0f, 1.0f);

    [SerializeField] private string intensityPropName = "_FlowIntensity";
    [SerializeField] private string speedPropName = "_FlowSpeed";
    [SerializeField] private float activeSpeed = 2.8f;
    [SerializeField] private float activeIntensity = 2.4f;
    [SerializeField] private float transitionDuration = 0.35f;

    [Header("Estado Inicial")]
    [SerializeField] private bool startActive = false;

    [Header("Eventos")]
    public UnityEvent<bool> onFlowStateChanged;

    private int intensityPropID;
    private int speedPropID;
    private int emissionColorPropID;
    private int baseColorPropID;
    private int baseMapSTPropID;

    private Coroutine activeTransitionRoutine;
    private bool isFlowActive;
    private float currentIntensity = 0f;
    private float uvScrollOffset = 0f;
    private Texture2D proceduralFlowTexture;

    // Pool de pulsos de energía 3D (Cero asignaciones de memoria por frame)
    private const int PULSE_COUNT = 24;
    private Transform[] pulseTransforms;
    private Renderer[] pulseRenderers;
    private Material sharedPulseMaterial;
    private GameObject pulsesRoot;

    // Waypoints reales del circuito eléctrico en SampleScene
    private readonly Vector3[] circuitWaypoints = new Vector3[]
    {
        new Vector3(-4.5f, 1.35f, 4.65f), // 0: Salida Fila Trasera de Paneles (Captación)
        new Vector3(-0.2f, 1.35f, 4.65f), // 1: Salida Fila Frontal de Paneles
        new Vector3(4.29f, 1.35f, 4.65f), // 2: Base del Inversor
        new Vector3(4.29f, 2.22f, 4.65f), // 3: Inversor y Conversión (DC -> AC)
        new Vector3(5.80f, 2.22f, 4.65f), // 4: Conector hacia Batería
        new Vector3(7.55f, 1.45f, 4.65f), // 5: Almacenaje (Banco de Baterías 12V)
        new Vector3(13.0f, 1.38f, 4.55f), // 6: Cableado principal hacia la vivienda
        new Vector3(18.5f, 1.32f, 4.45f)  // 7: Entrada eléctrica Casa del Pueblo
    };

    // Etiquetas educativas AR flotantes sobre los equipos 3D (Slide 5)
    private GameObject stageLabelsContainer;
    private RectTransform[] stageBadgeRects = new RectTransform[4];
    private readonly Vector3[] stageWorldPositions = new Vector3[]
    {
        new Vector3(-1.5f, 3.15f, 1.2f),  // 1. Captación (Paneles Solares)
        new Vector3(4.29f, 3.35f, 4.86f), // 2. Inversor y Conversión
        new Vector3(7.55f, 2.55f, 4.80f), // 3. Almacenaje (Banco de Baterías)
        new Vector3(19.2f, 3.80f, -1.0f)  // 4. Consumo (Casa Rural)
    };

    private CameraController cameraController;

    public bool IsFlowActive => isFlowActive;

    private void Awake()
    {
        intensityPropID = Shader.PropertyToID(intensityPropName);
        speedPropID = Shader.PropertyToID(speedPropName);
        emissionColorPropID = Shader.PropertyToID("_EmissionColor");
        baseColorPropID = Shader.PropertyToID("_BaseColor");
        baseMapSTPropID = Shader.PropertyToID("_BaseMap_ST");

        cameraController = FindFirstObjectByType<CameraController>();

        // Buscar el material compartido de los cables si no estaba asignado
        if (cableSharedMaterial == null)
        {
            GameObject sampleCable = GameObject.Find("Cableado2");
            if (sampleCable == null) sampleCable = GameObject.Find("CableadoRecto");
            if (sampleCable != null)
            {
                Renderer r = sampleCable.GetComponentInChildren<Renderer>();
                if (r != null) cableSharedMaterial = r.sharedMaterial;
            }
        }

        BuildProceduralFlowTexture();
        CreateEnergyPulsesPool();

        isFlowActive = startActive;
        currentIntensity = startActive ? activeIntensity : 0f;
        ApplyMaterialProperties(currentIntensity, startActive ? activeSpeed : 0f, 0f);
        if (pulsesRoot != null) pulsesRoot.SetActive(isFlowActive);
    }

    private void Start()
    {
        if (masterSwitchToggle == null)
        {
            Toggle[] toggles = FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Toggle t in toggles)
            {
                if (t != null && t.gameObject.name == "Toggle_FlujoElectrico")
                {
                    masterSwitchToggle = t;
                    break;
                }
            }
        }

        if (masterSwitchToggle != null)
        {
            masterSwitchToggle.SetIsOnWithoutNotify(isFlowActive);
            masterSwitchToggle.onValueChanged.RemoveListener(SetEnergyFlow);
            masterSwitchToggle.onValueChanged.AddListener(SetEnergyFlow);
        }

        CreateStageCalloutBadges();
    }

    private void Update()
    {
        if (!isFlowActive) return;

        // 1. Desplazamiento continuo de coordenadas UV (UV Scrolling) en los cables + pulso HDR
        uvScrollOffset -= Time.deltaTime * activeSpeed;
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * activeSpeed * 3.0f);
        ApplyMaterialProperties(currentIntensity * pulse, activeSpeed, pulse);

        // 2. Animar los pulsos lumínicos 3D a lo largo del circuito (Paneles -> Inversor -> Batería -> Casa)
        Animate3DEnergyPulses();
    }

    private void LateUpdate()
    {
        UpdateStageCalloutBadges();
    }

    private void OnDestroy()
    {
        if (cableSharedMaterial != null)
        {
            cableSharedMaterial.SetTexture("_BaseMap", null);
            cableSharedMaterial.SetTexture("_EmissionMap", null);
        }
        ApplyMaterialProperties(0f, 0f, 0f);

        if (masterSwitchToggle != null)
        {
            masterSwitchToggle.onValueChanged.RemoveListener(SetEnergyFlow);
        }
    }

    public void SetEnergyFlow(bool enable)
    {
        if (cameraController != null)
        {
            cameraController.NotifyEnergyToggleChanged();
        }

        isFlowActive = enable;

        if (pulsesRoot != null)
        {
            pulsesRoot.SetActive(enable);
        }

        if (stageLabelsContainer != null)
        {
            bool showBadges = enable && (cameraController == null || !cameraController.IsZoomedIn);
            stageLabelsContainer.SetActive(showBadges);
        }

        if (cableSharedMaterial != null && proceduralFlowTexture != null)
        {
            if (enable)
            {
                cableSharedMaterial.SetTexture("_BaseMap", proceduralFlowTexture);
                cableSharedMaterial.SetTexture("_EmissionMap", proceduralFlowTexture);
            }
            else
            {
                cableSharedMaterial.SetTexture("_BaseMap", null);
                cableSharedMaterial.SetTexture("_EmissionMap", null);
            }
        }

        if (activeTransitionRoutine != null)
        {
            StopCoroutine(activeTransitionRoutine);
        }

        activeTransitionRoutine = StartCoroutine(TransitionFlowRoutine(enable));
        onFlowStateChanged?.Invoke(enable);
    }

    public void ToggleEnergyFlow()
    {
        SetEnergyFlow(!isFlowActive);
    }

    private IEnumerator TransitionFlowRoutine(bool turningOn)
    {
        float targetIntensity = turningOn ? activeIntensity : 0f;
        float targetSpeed = turningOn ? activeSpeed : 0f;

        float startIntensity = currentIntensity;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            currentIntensity = Mathf.Lerp(startIntensity, targetIntensity, smoothT);
            float currentSpeed = Mathf.Lerp(0f, targetSpeed, smoothT);

            ApplyMaterialProperties(currentIntensity, currentSpeed, smoothT);

            yield return null;
        }

        currentIntensity = targetIntensity;
        ApplyMaterialProperties(currentIntensity, targetSpeed, turningOn ? 1f : 0f);
        activeTransitionRoutine = null;
    }

    private void ApplyMaterialProperties(float intensity, float speed, float normalizedBlend)
    {
        if (cableSharedMaterial == null) return;

        if (cableSharedMaterial.HasProperty(intensityPropID))
            cableSharedMaterial.SetFloat(intensityPropID, intensity);

        if (cableSharedMaterial.HasProperty(speedPropID))
            cableSharedMaterial.SetFloat(speedPropID, speed);

        if (cableSharedMaterial.HasProperty(baseMapSTPropID))
        {
            cableSharedMaterial.SetVector(baseMapSTPropID, new Vector4(1f, 10f, 0f, uvScrollOffset));
        }

        if (cableSharedMaterial.HasProperty(emissionColorPropID))
        {
            if (intensity > 0.01f)
            {
                cableSharedMaterial.EnableKeyword("_EMISSION");
                cableSharedMaterial.SetColor(emissionColorPropID, activeEmissionColor * intensity);
            }
            else
            {
                cableSharedMaterial.SetColor(emissionColorPropID, Color.black);
            }
        }

        if (cableSharedMaterial.HasProperty(baseColorPropID))
        {
            Color c = Color.Lerp(baseCableColorOff, baseCableColorOn, Mathf.Clamp01(normalizedBlend));
            cableSharedMaterial.SetColor(baseColorPropID, c);
        }
    }

    private void BuildProceduralFlowTexture()
    {
        int size = 64;
        proceduralFlowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        proceduralFlowTexture.wrapMode = TextureWrapMode.Repeat;
        proceduralFlowTexture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Color darkBlue = new Color(0.05f, 0.22f, 0.55f, 1f);
        Color brightGold = new Color(1.0f, 0.90f, 0.25f, 1f);

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            // Pulso concentrado brillante para simular paquetes de corriente eléctrica
            float wave = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f), 4f);
            Color rowColor = Color.Lerp(darkBlue, brightGold, wave);
            for (int x = 0; x < size; x++)
            {
                pixels[y * size + x] = rowColor;
            }
        }

        proceduralFlowTexture.SetPixels(pixels);
        proceduralFlowTexture.Apply();
    }

    private void CreateEnergyPulsesPool()
    {
        pulsesRoot = new GameObject("EnergyFlow_3DPulses");
        pulsesRoot.transform.SetParent(transform, false);

        Shader urpLit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Lit");
        sharedPulseMaterial = new Material(urpLit);
        if (sharedPulseMaterial.HasProperty("_BaseColor"))
        {
            sharedPulseMaterial.SetColor("_BaseColor", new Color(1.0f, 0.88f, 0.20f, 1.0f));
        }
        if (sharedPulseMaterial.HasProperty("_EmissionColor"))
        {
            sharedPulseMaterial.EnableKeyword("_EMISSION");
            sharedPulseMaterial.SetColor("_EmissionColor", new Color(1.0f, 0.85f, 0.15f, 1.0f) * 2.5f);
        }

        pulseTransforms = new Transform[PULSE_COUNT];
        pulseRenderers = new Renderer[PULSE_COUNT];

        for (int i = 0; i < PULSE_COUNT; i++)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"EnergyPulse_{i}";
            sphere.transform.SetParent(pulsesRoot.transform, false);
            sphere.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);

            // Eliminar Collider para cero costo físico en Android
            Collider col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Renderer r = sphere.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = sharedPulseMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            pulseTransforms[i] = sphere.transform;
            pulseRenderers[i] = r;
        }
    }

    private void Animate3DEnergyPulses()
    {
        if (pulseTransforms == null || circuitWaypoints.Length < 2) return;

        int segCount = circuitWaypoints.Length - 1;
        float baseTime = Time.time * 0.45f;

        for (int i = 0; i < PULSE_COUNT; i++)
        {
            Transform t = pulseTransforms[i];
            if (t == null) continue;

            float normPos = Mathf.Repeat(baseTime + (float)i / PULSE_COUNT, 1f);
            float scaled = normPos * segCount;
            int segIndex = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, segCount - 1);
            float segT = scaled - segIndex;

            t.position = Vector3.Lerp(circuitWaypoints[segIndex], circuitWaypoints[segIndex + 1], segT);

            // Pequeña pulsación de escala para mayor impacto visual
            float scalePulse = 0.19f + 0.06f * Mathf.Sin(Time.time * 8f + i);
            t.localScale = new Vector3(scalePulse, scalePulse, scalePulse);
        }
    }

    private void CreateStageCalloutBadges()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        Transform mainUI = canvas.transform.Find("MainUIPanel");
        Transform parentTransform = mainUI != null ? mainUI : canvas.transform;

        Transform existing = parentTransform.Find("AR_EnergyStages_Container");
        if (existing != null) Destroy(existing.gameObject);

        stageLabelsContainer = new GameObject("AR_EnergyStages_Container");
        stageLabelsContainer.transform.SetParent(parentTransform, false);
        RectTransform rtContainer = stageLabelsContainer.AddComponent<RectTransform>();
        rtContainer.anchorMin = Vector2.zero;
        rtContainer.anchorMax = Vector2.one;
        rtContainer.sizeDelta = Vector2.zero;

        string[] stageTitles = new string[]
        {
            "☀️ 1. CAPTACIÓN SOLAR\nPaneles generan corriente continua (DC)",
            "⚙️ 2. INVERSOR Y CONVERSIÓN\nTransforma energía DC ➔ AC utilizable",
            "🔋 3. ALMACENAJE (BATERÍA 12V)\nGuarda energía para uso nocturno",
            "🏠 4. CONSUMO EN VIVIENDA\nElectricidad limpia y sustentable"
        };

        Color[] borderColors = new Color[]
        {
            new Color(1.0f, 0.85f, 0.25f, 1f),
            new Color(0.25f, 0.90f, 1.00f, 1f),
            new Color(0.30f, 0.95f, 0.55f, 1f),
            new Color(1.0f, 0.78f, 0.40f, 1f)
        };

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < 4; i++)
        {
            GameObject card = new GameObject($"StageCard_{i + 1}");
            card.transform.SetParent(stageLabelsContainer.transform, false);
            RectTransform rt = card.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(255f, 48f);

            Image bg = card.AddComponent<Image>();
            if (AppFlowManager.RuntimeGlassPillSprite != null)
            {
                bg.sprite = AppFlowManager.RuntimeGlassPillSprite;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.05f, 0.09f, 0.16f, 0.88f);
            bg.raycastTarget = false;

            GameObject txtGO = new GameObject("Text");
            txtGO.transform.SetParent(card.transform, false);
            RectTransform rtTxt = txtGO.AddComponent<RectTransform>();
            rtTxt.sizeDelta = new Vector2(240f, 44f);

            Text txt = txtGO.AddComponent<Text>();
            txt.text = stageTitles[i];
            txt.font = font;
            txt.fontSize = 12;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = borderColors[i];
            txt.raycastTarget = false;

            stageBadgeRects[i] = rt;
        }

        stageLabelsContainer.SetActive(isFlowActive);
    }

    private void UpdateStageCalloutBadges()
    {
        if (stageLabelsContainer == null) return;

        bool shouldShow = isFlowActive && (cameraController == null || !cameraController.IsZoomedIn);
        if (stageLabelsContainer.activeSelf != shouldShow)
        {
            stageLabelsContainer.SetActive(shouldShow);
        }

        if (!shouldShow) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        for (int i = 0; i < 4; i++)
        {
            RectTransform badge = stageBadgeRects[i];
            if (badge == null) continue;

            Vector3 screenPt = cam.WorldToScreenPoint(stageWorldPositions[i]);
            if (screenPt.z > 0.5f)
            {
                badge.gameObject.SetActive(true);
                badge.position = new Vector3(screenPt.x, screenPt.y, 0f);
            }
            else
            {
                badge.gameObject.SetActive(false);
            }
        }
    }
}
