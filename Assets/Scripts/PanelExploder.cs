using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>
/// Funcionalidad 2: Anatomía de la Celda Fotovoltaica (Modo de Inspección por Capas)
/// + Optimizador Geométrico URP para Android (Reduce la escena de ~200k triángulos a ~18k triángulos).
/// - Reemplaza en tiempo de ejecución las mallas subdivididas excesivas (MallaMetalicaPanel de 448k tris
///   y PanelCristal_PanelSolar de 14k tris) en los 13 paneles por mallas procedurales Low-Poly compartidas
///   respetando al milímetro sus Bounding Boxes originales.
/// - Desactiva Shadow Casters redundantes en capas superpuestas para eliminar el doble pase de sombras.
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

    // Mallas Low-Poly compartidas en memoria (0 duplicación, 100% compatibles con SRP Batcher)
    private static Mesh sharedLowPolyGlassMesh;
    private static Mesh sharedLowPolyGridMesh;
    private static Mesh sharedLowPolyChassisMesh;

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

        // Optimizar los 13 paneles solares y sombras de la escena una sola vez al arrancar
        OptimizeEntireSceneGeometry(targetRoot);

        mallaMetalica = targetRoot.Find("MallaMetalicaPanel");
        panelCristal = targetRoot.Find("PanelCristal_PanelSolar");
        if (panelCristal == null) panelCristal = targetRoot.Find("PanelSolar");
        celdaCaptadora = targetRoot.Find("CeldaSolar");
        baseChasis = targetRoot.Find("BaseChasisPanelSolar");

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

    /// <summary>
    /// Reduce drásticamente el conteo de triángulos de toda la escena (~200k -> ~18k)
    /// reemplazando las mallas subdivididas de Blender por geometría limpia Low-Poly con idéntico Bounding Box.
    /// </summary>
    private static void OptimizeEntireSceneGeometry(Transform inspectedPanelRoot)
    {
        MeshFilter[] allMeshFilters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (MeshFilter mf in allMeshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            string objName = mf.gameObject.name;
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();

            // 1. Optimizar PanelCristal_PanelSolar (De 14,084 tris -> 12 tris por panel)
            if (objName.StartsWith("PanelCristal_PanelSolar") || objName == "PanelSolar")
            {
                if (mf.sharedMesh.vertexCount > 100)
                {
                    if (sharedLowPolyGlassMesh == null)
                    {
                        sharedLowPolyGlassMesh = CreateBoxMeshFromBounds(mf.sharedMesh.bounds, "LowPoly_PanelCristal");
                    }
                    mf.sharedMesh = sharedLowPolyGlassMesh;
                }

                if (mr != null)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            // 2. Optimizar MallaMetalicaPanel (De 65k verts / >130k tris -> 144 tris por panel)
            else if (objName.StartsWith("MallaMetalicaPanel"))
            {
                if (mf.sharedMesh.vertexCount > 400)
                {
                    if (sharedLowPolyGridMesh == null)
                    {
                        sharedLowPolyGridMesh = CreateMetallicGridMeshFromBounds(mf.sharedMesh.bounds, 6, 6, "LowPoly_MallaMetalica");
                    }
                    mf.sharedMesh = sharedLowPolyGridMesh;
                }

                if (mr != null)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            // 3. Optimizar CeldaSolar (Evitar doble pase de sombras innecesario)
            else if (objName.StartsWith("CeldaSolar"))
            {
                if (mr != null)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            // 4. Optimizar BaseChasisPanelSolar en los 12 paneles secundarios (De 1,508 tris -> 12 tris)
            else if (objName.StartsWith("BaseChasisPanelSolar"))
            {
                if (inspectedPanelRoot == null || mf.transform.parent != inspectedPanelRoot)
                {
                    if (mf.sharedMesh.vertexCount > 100)
                    {
                        if (sharedLowPolyChassisMesh == null)
                        {
                            sharedLowPolyChassisMesh = CreateBoxMeshFromBounds(mf.sharedMesh.bounds, "LowPoly_BaseChasis");
                        }
                        mf.sharedMesh = sharedLowPolyChassisMesh;
                    }
                }
            }
            // 5. Desactivar proyección de sombras en cables a ras de suelo y caminos planos
            else if (objName.StartsWith("Cableado") || objName.StartsWith("Camino") || objName == "Plane")
            {
                if (mr != null)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }
    }

    /// <summary>
    /// Construye un prisma rectangular limpio de 12 triángulos (24 vértices con normales duras)
    /// que coincide exactamente con el Bounding Box local del FBX original.
    /// </summary>
    private static Mesh CreateBoxMeshFromBounds(Bounds b, string meshName)
    {
        Mesh m = new Mesh();
        m.name = meshName;

        List<Vector3> verts = new List<Vector3>(24);
        List<Vector3> norms = new List<Vector3>(24);
        List<Vector2> uvs = new List<Vector2>(24);
        List<int> tris = new List<int>(36);

        AppendBoxGeometry(verts, norms, uvs, tris, b.min, b.max);

        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Construye una rejilla metálica 3D limpia (barras longitudinales + transversales)
    /// dentro del Bounding Box exacto de MallaMetalicaPanel (~144 triángulos en total).
    /// </summary>
    private static Mesh CreateMetallicGridMeshFromBounds(Bounds b, int barsX, int barsZ, string meshName)
    {
        Mesh m = new Mesh();
        m.name = meshName;

        int totalBoxes = barsX + barsZ;
        List<Vector3> verts = new List<Vector3>(totalBoxes * 24);
        List<Vector3> norms = new List<Vector3>(totalBoxes * 24);
        List<Vector2> uvs = new List<Vector2>(totalBoxes * 24);
        List<int> tris = new List<int>(totalBoxes * 36);

        Vector3 min = b.min;
        Vector3 max = b.max;
        Vector3 size = b.size;

        // Grosor de cada barra proporcional al tamaño del Bounding Box
        float barWidthX = size.x * 0.018f;
        float barWidthZ = size.z * 0.018f;

        // Barras paralelas al eje Z (distribuidas a lo largo de X)
        for (int i = 0; i < barsX; i++)
        {
            float t = (barsX > 1) ? (float)i / (barsX - 1) : 0.5f;
            float cx = Mathf.Lerp(min.x + barWidthX * 0.5f, max.x - barWidthX * 0.5f, t);
            Vector3 bMin = new Vector3(cx - barWidthX * 0.5f, min.y, min.z);
            Vector3 bMax = new Vector3(cx + barWidthX * 0.5f, max.y, max.z);
            AppendBoxGeometry(verts, norms, uvs, tris, bMin, bMax);
        }

        // Barras paralelas al eje X (distribuidas a lo largo de Z)
        for (int j = 0; j < barsZ; j++)
        {
            float t = (barsZ > 1) ? (float)j / (barsZ - 1) : 0.5f;
            float cz = Mathf.Lerp(min.z + barWidthZ * 0.5f, max.z - barWidthZ * 0.5f, t);
            Vector3 bMin = new Vector3(min.x, min.y, cz - barWidthZ * 0.5f);
            Vector3 bMax = new Vector3(max.x, max.y, cz + barWidthZ * 0.5f);
            AppendBoxGeometry(verts, norms, uvs, tris, bMin, bMax);
        }

        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    private static void AppendBoxGeometry(
        List<Vector3> verts,
        List<Vector3> norms,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 min,
        Vector3 max)
    {
        Vector3 p000 = new Vector3(min.x, min.y, min.z);
        Vector3 p100 = new Vector3(max.x, min.y, min.z);
        Vector3 p110 = new Vector3(max.x, max.y, min.z);
        Vector3 p010 = new Vector3(min.x, max.y, min.z);

        Vector3 p001 = new Vector3(min.x, min.y, max.z);
        Vector3 p101 = new Vector3(max.x, min.y, max.z);
        Vector3 p111 = new Vector3(max.x, max.y, max.z);
        Vector3 p011 = new Vector3(min.x, max.y, max.z);

        // +Y (Arriba)
        AddQuadFace(verts, norms, uvs, tris, p010, p011, p111, p110, Vector3.up);
        // -Y (Abajo)
        AddQuadFace(verts, norms, uvs, tris, p001, p000, p100, p101, Vector3.down);
        // +X (Derecha)
        AddQuadFace(verts, norms, uvs, tris, p100, p110, p111, p101, Vector3.right);
        // -X (Izquierda)
        AddQuadFace(verts, norms, uvs, tris, p001, p011, p010, p000, Vector3.left);
        // +Z (Frente)
        AddQuadFace(verts, norms, uvs, tris, p101, p111, p011, p001, Vector3.forward);
        // -Z (Atrás)
        AddQuadFace(verts, norms, uvs, tris, p000, p010, p110, p100, Vector3.back);
    }

    private static void AddQuadFace(
        List<Vector3> verts,
        List<Vector3> norms,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        Vector3 normal)
    {
        int baseIdx = verts.Count;
        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);

        norms.Add(normal);
        norms.Add(normal);
        norms.Add(normal);
        norms.Add(normal);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        tris.Add(baseIdx + 0);
        tris.Add(baseIdx + 1);
        tris.Add(baseIdx + 2);
        tris.Add(baseIdx + 0);
        tris.Add(baseIdx + 2);
        tris.Add(baseIdx + 3);
    }

    private void Start()
    {
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

        Font font = AppFlowManager.ModernFontBold != null
            ? AppFlowManager.ModernFontBold
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
                badge.position = new Vector3(screenPt.x - 190f, screenPt.y + (1.5f - i) * 14f, 0f);
            }
            else
            {
                badge.gameObject.SetActive(false);
            }
        }
    }
}
