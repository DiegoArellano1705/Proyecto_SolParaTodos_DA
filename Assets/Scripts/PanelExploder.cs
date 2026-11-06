using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>
/// Funcionalidad 2: Anatomía de la Celda Fotovoltaica (Modo de Inspección por Capas).
/// - Separa las 3 capas móviles (Malla Metálica, Panel de Cristal y Celda Solar Captadora)
///   sobre la Base/Chasis estático usando Vector3.Lerp en el eje normal inclinado del panel.
/// - Muestra etiquetas flotantes AR en tiempo real apuntando a cada una de las 4 capas.
/// - Hace una demostración automática al entrar al modo 'Ver Capas' y permite control total (0% a 100%).
/// </summary>
public class PanelExploder : MonoBehaviour
{
    [Header("Referencias de la Malla y Capas")]
    [Tooltip("Capa 1 (Superior): MallaMetalicaPanel.")]
    [SerializeField] private Transform mallaMetalica;

    [Tooltip("Capa 2 (Intermedia): PanelCristal_PanelSolar.")]
    [SerializeField] private Transform panelCristal;

    [Tooltip("Capa 3 (Inferior): CeldaSolar.")]
    [SerializeField] private Transform celdaCaptadora;

    [Tooltip("Capa 4 (Base Estática): BaseChasisPanelSolar.")]
    [SerializeField] private Transform baseChasis;

    [Header("Distancias Máximas de Despiece (Perpendicular al Panel)")]
    [SerializeField] private float topDisplacement = 0.85f;
    [SerializeField] private float midDisplacement = 0.52f;
    [SerializeField] private float botDisplacement = 0.24f;

    [Tooltip("Vector normal a la inclinación de 38° del panel solar.")]
    [SerializeField] private Vector3 localSeparationAxis = new Vector3(0.63f, 0.77f, 0f);

    [Header("Integración UI y Cámara")]
    [SerializeField] private Slider explodeSlider;
    [SerializeField] private Text explodePercentText;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private bool requireCameraZoom = false;

    private Vector3 initialTopPos;
    private Vector3 initialMidPos;
    private Vector3 initialBotPos;

    private bool canExplode = true;
    private float currentProgress = 0f;
    private Coroutine previewRoutine;

    // Etiquetas flotantes AR para las 4 capas (Slide 3)
    private GameObject labelsContainer;
    private RectTransform[] layerBadgeRects = new RectTransform[4];
    private Renderer[] layerRenderers = new Renderer[4];

    public bool CanExplode => canExplode;
    public float CurrentProgress => currentProgress;

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }

        // Asegurar que despiezamos exactamente el panel frontal al que hace zoom la cámara
        Transform targetRoot = transform;
        GameObject frontalPanel = GameObject.Find("CeldaFotovoltaica (3)");
        if (frontalPanel != null)
        {
            targetRoot = frontalPanel.transform;
        }

        mallaMetalica = targetRoot.Find("MallaMetalicaPanel");
        panelCristal = targetRoot.Find("PanelCristal_PanelSolar");
        if (panelCristal == null) panelCristal = targetRoot.Find("PanelSolar");
        celdaCaptadora = targetRoot.Find("CeldaSolar");
        baseChasis = targetRoot.Find("BaseChasisPanelSolar");

        // Configurar el cristal en modo transparente limpio y sin proyectar sombras (ahorro GPU Android)
        if (panelCristal != null)
        {
            MeshRenderer mr = panelCristal.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = ShadowCastingMode.Off;
                if (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor"))
                {
                    Color glassCol = new Color(0.55f, 0.88f, 1.0f, 0.28f);
                    mr.sharedMaterial.SetColor("_BaseColor", glassCol);
                }
            }
        }

        // Forzar desplazamientos amplios y claros para que el despiece sea muy vistoso
        localSeparationAxis = new Vector3(0.63f, 0.77f, 0f);
        topDisplacement = 0.85f;
        midDisplacement = 0.52f;
        botDisplacement = 0.24f;
        requireCameraZoom = false;
        canExplode = true;

        if (mallaMetalica != null)
        {
            initialTopPos = mallaMetalica.localPosition;
            layerRenderers[0] = mallaMetalica.GetComponent<Renderer>();
        }
        if (panelCristal != null)
        {
            initialMidPos = panelCristal.localPosition;
            layerRenderers[1] = panelCristal.GetComponent<Renderer>();
        }
        if (celdaCaptadora != null)
        {
            initialBotPos = celdaCaptadora.localPosition;
            layerRenderers[2] = celdaCaptadora.GetComponent<Renderer>();
        }
        if (baseChasis != null)
        {
            layerRenderers[3] = baseChasis.GetComponent<Renderer>();
        }
    }

    private void Start()
    {
        // Auto-vincular Slider Vertical y Texto de Porcentaje aunque inicien desactivados
        if (explodeSlider == null)
        {
            Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Slider s in sliders)
            {
                if (s != null && (s.gameObject.name == "VerticalSlider_Layers" || s.gameObject.name == "Slider_Despiece"))
                {
                    explodeSlider = s;
                    break;
                }
            }
        }

        if (explodePercentText == null)
        {
            Text[] texts = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Text t in texts)
            {
                if (t != null && t.gameObject.name == "Text_Percent")
                {
                    explodePercentText = t;
                    break;
                }
            }
        }

        if (explodeSlider != null)
        {
            explodeSlider.minValue = 0f;
            explodeSlider.maxValue = 1f;
            explodeSlider.value = 0f;
            explodeSlider.interactable = true;
            explodeSlider.onValueChanged.RemoveListener(SetExplodeProgress);
            explodeSlider.onValueChanged.AddListener(SetExplodeProgress);
        }

        CreateARLayerLabels();
    }

    private void OnEnable()
    {
        if (cameraController == null) cameraController = FindFirstObjectByType<CameraController>();
        if (cameraController != null)
        {
            cameraController.onTransitionStarted.RemoveListener(OnCameraTransitionStarted);
            cameraController.onTransitionFinished.RemoveListener(OnCameraTransitionFinished);
            cameraController.onTransitionStarted.AddListener(OnCameraTransitionStarted);
            cameraController.onTransitionFinished.AddListener(OnCameraTransitionFinished);
        }
    }

    private void OnDisable()
    {
        if (cameraController != null)
        {
            cameraController.onTransitionStarted.RemoveListener(OnCameraTransitionStarted);
            cameraController.onTransitionFinished.RemoveListener(OnCameraTransitionFinished);
        }

        if (explodeSlider != null)
        {
            explodeSlider.onValueChanged.RemoveListener(SetExplodeProgress);
        }
    }

    private void LateUpdate()
    {
        UpdateARLayerLabelsPositions();
    }

    public void OnCameraTransitionStarted()
    {
        // Si estamos regresando a la vista del patio, ensamblar de nuevo las capas
        if (cameraController != null && cameraController.IsZoomedIn)
        {
            if (previewRoutine != null) StopCoroutine(previewRoutine);
            ResetLayers();
            if (labelsContainer != null) labelsContainer.SetActive(false);
        }
    }

    public void OnCameraTransitionFinished()
    {
        canExplode = true;
        if (explodeSlider != null) explodeSlider.interactable = true;

        bool inInspectMode = cameraController != null && cameraController.IsZoomedIn;
        if (labelsContainer != null)
        {
            labelsContainer.SetActive(inInspectMode);
        }

        // Al entrar al modo 'Ver Capas', abrir suavemente al 50% como demostración inicial (Bosquejo AR 4)
        if (inInspectMode && currentProgress < 0.05f)
        {
            if (previewRoutine != null) StopCoroutine(previewRoutine);
            previewRoutine = StartCoroutine(AnimateExplodePreview(0.50f, 0.55f));
        }
    }

    private IEnumerator AnimateExplodePreview(float targetVal, float duration)
    {
        float startVal = currentProgress;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float val = Mathf.Lerp(startVal, targetVal, t);
            if (explodeSlider != null) explodeSlider.SetValueWithoutNotify(val);
            ApplyLayerPositions(val);
            yield return null;
        }
        if (explodeSlider != null) explodeSlider.SetValueWithoutNotify(targetVal);
        ApplyLayerPositions(targetVal);
        previewRoutine = null;
    }

    public void SetExplodeProgress(float progress)
    {
        if (previewRoutine != null)
        {
            StopCoroutine(previewRoutine);
            previewRoutine = null;
        }
        ApplyLayerPositions(progress);
    }

    private void ApplyLayerPositions(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        if (explodePercentText != null)
        {
            explodePercentText.text = $"{Mathf.RoundToInt(currentProgress * 100f)}%";
        }

        Vector3 axis = localSeparationAxis.normalized;

        if (mallaMetalica != null)
        {
            Vector3 targetPos = initialTopPos + (axis * topDisplacement);
            mallaMetalica.localPosition = Vector3.Lerp(initialTopPos, targetPos, currentProgress);
        }

        if (panelCristal != null)
        {
            Vector3 targetPos = initialMidPos + (axis * midDisplacement);
            panelCristal.localPosition = Vector3.Lerp(initialMidPos, targetPos, currentProgress);
        }

        if (celdaCaptadora != null)
        {
            Vector3 targetPos = initialBotPos + (axis * botDisplacement);
            celdaCaptadora.localPosition = Vector3.Lerp(initialBotPos, targetPos, currentProgress);
        }
    }

    public void ResetLayers()
    {
        currentProgress = 0f;
        if (explodePercentText != null)
        {
            explodePercentText.text = "0%";
        }

        if (explodeSlider != null)
        {
            explodeSlider.SetValueWithoutNotify(0f);
        }

        if (mallaMetalica != null) mallaMetalica.localPosition = initialTopPos;
        if (panelCristal != null) panelCristal.localPosition = initialMidPos;
        if (celdaCaptadora != null) celdaCaptadora.localPosition = initialBotPos;
    }

    private void CreateARLayerLabels()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        Transform mainUI = canvas.transform.Find("MainUIPanel");
        Transform parentTransform = mainUI != null ? mainUI : canvas.transform;

        Transform existing = parentTransform.Find("AR_LayerLabels_Container");
        if (existing != null) Destroy(existing.gameObject);

        labelsContainer = new GameObject("AR_LayerLabels_Container");
        labelsContainer.transform.SetParent(parentTransform, false);
        RectTransform rtContainer = labelsContainer.AddComponent<RectTransform>();
        rtContainer.anchorMin = Vector2.zero;
        rtContainer.anchorMax = Vector2.one;
        rtContainer.sizeDelta = Vector2.zero;

        string[] titles = new string[]
        {
            "1. Malla Metálica  —  Protección Superior",
            "2. Panel de Cristal  —  Vidrio Templado Translúcido",
            "3. Celda Solar Captadora  —  Silicio Fotovoltaico",
            "4. Base / Chasis del Panel  —  Soporte Estructural"
        };

        Color[] accentColors = new Color[]
        {
            new Color(0.25f, 0.92f, 0.95f, 1f),
            new Color(0.55f, 0.88f, 1.00f, 1f),
            new Color(1.00f, 0.80f, 0.25f, 1f),
            new Color(0.75f, 0.82f, 0.90f, 1f)
        };

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < 4; i++)
        {
            GameObject badge = new GameObject($"LayerBadge_{i + 1}");
            badge.transform.SetParent(labelsContainer.transform, false);
            RectTransform rt = badge.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(310f, 32f);

            Image bg = badge.AddComponent<Image>();
            if (AppFlowManager.RuntimeGlassPillSprite != null)
            {
                bg.sprite = AppFlowManager.RuntimeGlassPillSprite;
                bg.type = Image.Type.Sliced;
            }
            bg.color = new Color(0.05f, 0.09f, 0.15f, 0.86f);
            bg.raycastTarget = false;

            GameObject txtGO = new GameObject("Label");
            txtGO.transform.SetParent(badge.transform, false);
            RectTransform rtTxt = txtGO.AddComponent<RectTransform>();
            rtTxt.sizeDelta = new Vector2(294f, 30f);

            Text txt = txtGO.AddComponent<Text>();
            txt.text = titles[i];
            txt.font = font;
            txt.fontSize = 12;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = accentColors[i];
            txt.raycastTarget = false;

            layerBadgeRects[i] = rt;
        }

        labelsContainer.SetActive(false);
    }

    private void UpdateARLayerLabelsPositions()
    {
        if (labelsContainer == null || !labelsContainer.activeSelf) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        for (int i = 0; i < 4; i++)
        {
            Renderer r = layerRenderers[i];
            RectTransform badge = layerBadgeRects[i];
            if (r == null || badge == null) continue;

            Vector3 worldPt = r.bounds.center;
            Vector3 screenPt = cam.WorldToScreenPoint(worldPt);

            if (screenPt.z > 0.1f)
            {
                badge.gameObject.SetActive(true);
                // Desplazar ligeramente a la izquierda del panel para lectura limpia estilo AR
                badge.position = new Vector3(screenPt.x - 190f, screenPt.y + (1.5f - i) * 14f, 0f);
            }
            else
            {
                badge.gameObject.SetActive(false);
            }
        }
    }
}
