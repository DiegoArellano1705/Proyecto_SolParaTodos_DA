using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;

/// <summary>
/// Asegura automáticamente en memoria de Unity Editor (al compilar scripts y justo antes de crear el APK)
/// que Android use OpenGLES3 (sin el bug de Vulkan Pre-Transform en horizontal), orientación Landscape fija,
/// Resizeable Activity desactivado y arquitecturas universales ARMv7 + ARM64.
/// </summary>
public class SolParaTodosAndroidBuildPreprocess : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        SolParaTodosAutoSetup.EnforceAndroidLandscapeAndOpenGLES3();
    }
}

/// <summary>
/// Montaje visual avanzado basado en los bosquejos AR de diseño:
/// 1. Posiciona la Cámara sobre el Camino Central del Patio (X=10.5, Y=3.2, Z=-14.5) para ver paneles, inversor y casa.
/// 2. Dial Solar Inferior en Arco (Bosquejo AR 2) con icono del Sol arrastrable y marcas horarias.
/// 3. Modo 'Ver Capas' con acercamiento al centro geométrico real de 'CeldaFotovoltaica (3)' y Slider Vertical a la derecha (Bosquejo AR 4).
/// 4. Vincula 'MallaMetalicaPanel', 'PanelCristal_PanelSolar' y 'CeldaSolar' con eje perpendicular real al panel.
/// </summary>
[InitializeOnLoad]
public static class SolParaTodosAutoSetup
{
    static SolParaTodosAutoSetup()
    {
        EditorApplication.delayCall += EnforceAndroidLandscapeAndOpenGLES3;
    }

    [MenuItem("Sol Para Todos/Aplicar Fix de Pantalla Horizontal Android (OpenGLES3)")]
    public static void EnforceAndroidLandscapeAndOpenGLES3()
    {
        // 1. Forzar OpenGLES3 y quitar Vulkan (corrige el bug donde la pantalla se aplasta al 45% izquierdo en horizontal)
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.vulkanEnablePreTransform = false;

        // 2. Bloquear orientación en Landscape (Horizontal) y desactivar Resizeable Activity
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.Android.resizeableActivity = false;
        PlayerSettings.Android.renderOutsideSafeArea = false;
        PlayerSettings.Android.startInFullscreen = true;

        // 3. Compatibilidad universal ARMv7 (32 bits) + ARM64 (64 bits) y Android 6.0+ (API 23)
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
        PlayerSettings.Android.bundleVersionCode = 3;

        AssetDatabase.SaveAssets();
        Debug.Log("<color=green><b>[SolParaTodos]</b> Configuración Android aplicada en memoria: OpenGLES3 (sin bug de Vulkan), Landscape fijo, ResizeableActivity=OFF, ARMv7+ARM64.</color>");
    }

    [MenuItem("Sol Para Todos/¡Montar Todo en la Escena Automáticamente!")]
    public static void SetupEntireScene()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("SolParaTodos AR-Style Setup");
        int undoGroup = Undo.GetCurrentGroup();

        Debug.Log("<color=cyan><b>[SolParaTodos]</b> Calibrando cámara en el patio central y montando HUD AR...</color>");

        // 1. Generar texturas de interfaz con estilo AR Frosted Glass
        Sprite glassPillSprite = GetOrCreateGlassPillSprite();
        Sprite circleKnobSprite = GetOrCreateCircleKnobSprite();
        Sprite sunIconSprite = GetOrCreateSunIconSprite();
        Sprite sunArcSprite = GetOrCreateSunArcSprite();

        // 2. Configurar logos existentes y material de cristal
        ConfigureLogosAsSprites();
        OptimizeGlassMaterial();

        // 3. Ajuste cinematográfico de iluminación y Post-Processing
        UpgradeVisualEnvironment();

        // 4. Asegurar EventSystem
        EnsureEventSystem();

        // 5. Configurar Canvas
        Canvas canvas = EnsureCanvas();

        Transform oldSplash = canvas.transform.Find("SplashPanel");
        if (oldSplash != null) Object.DestroyImmediate(oldSplash.gameObject);

        Transform oldMainUI = canvas.transform.Find("MainUIPanel");
        if (oldMainUI != null) Object.DestroyImmediate(oldMainUI.gameObject);

        // 6. Crear Splash Screen con logos y autor
        GameObject splashPanel = CreateSplashPanel(canvas.transform, glassPillSprite);

        // 7. Crear el nuevo HUD interactivo basado en los bosquejos AR
        GameObject mainUIPanel = CreateARHUD(canvas.transform, glassPillSprite, circleKnobSprite, sunIconSprite, sunArcSprite,
            out Slider sunSlider, out Text sunHourText,
            out GameObject layersPanel, out Slider layersSlider, out Text layersPercentText,
            out Button modeBtn, out Text modeBtnText,
            out Toggle energyToggle);

        // 8. Configurar AppFlowManager
        AppFlowManager flowManager = splashPanel.GetComponent<AppFlowManager>();
        if (flowManager == null) flowManager = splashPanel.AddComponent<AppFlowManager>();

        SerializedObject soFlow = new SerializedObject(flowManager);
        soFlow.FindProperty("splashCanvasGroup").objectReferenceValue = splashPanel.GetComponent<CanvasGroup>();
        soFlow.FindProperty("splashPanel").objectReferenceValue = splashPanel;
        soFlow.FindProperty("mainUIPanel").objectReferenceValue = mainUIPanel;
        soFlow.FindProperty("displayDuration").floatValue = 2.2f;
        soFlow.FindProperty("fadeDuration").floatValue = 0.7f;
        soFlow.ApplyModifiedProperties();

        // 9. Seleccionar el panel frontal junto al camino amarillo: CeldaFotovoltaica (3)
        GameObject targetPanel = GameObject.Find("CeldaFotovoltaica (3)");
        if (targetPanel == null) targetPanel = GameObject.Find("CeldaFotovoltaica (4)");
        if (targetPanel == null) targetPanel = GameObject.Find("CeldaFotovoltaica");

        // 10. Configurar CameraController Inmersivo en el Camino Central del Patio
        Camera mainCam = Camera.main;
        CameraController camController = null;
        if (mainCam != null)
        {
            Vector3 patioPos = new Vector3(9.5f, 4.2f, -10.5f);
            Vector3 patioEuler = new Vector3(8f, -32f, 0f);

            // Posicionar la cámara inmediatamente en Edit Mode para que se vea desde ya
            Undo.RecordObject(mainCam.transform, "Move Camera to Courtyard");
            mainCam.transform.position = patioPos;
            mainCam.transform.rotation = Quaternion.Euler(patioEuler);
            mainCam.fieldOfView = 68f;

            camController = mainCam.GetComponent<CameraController>();
            if (camController == null) camController = mainCam.gameObject.AddComponent<CameraController>();

            SerializedObject soCam = new SerializedObject(camController);
            soCam.FindProperty("targetCamera").objectReferenceValue = mainCam;
            if (targetPanel != null)
            {
                soCam.FindProperty("defaultInspectTarget").objectReferenceValue = targetPanel.transform;
            }

            soCam.FindProperty("courtyardPosition").vector3Value = patioPos;
            soCam.FindProperty("courtyardEuler").vector3Value = patioEuler;
            soCam.FindProperty("transitionDuration").floatValue = 1.15f;
            if (modeBtnText != null) soCam.FindProperty("toggleButtonText").objectReferenceValue = modeBtnText;
            soCam.ApplyModifiedProperties();

            if (modeBtn != null)
            {
                modeBtn.onClick.RemoveAllListeners();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(modeBtn.onClick, camController.ToggleInspectMode);
            }

            if (layersPanel != null)
            {
                camController.onZoomStateChanged.RemoveAllListeners();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(camController.onZoomStateChanged, layersPanel.SetActive);
            }
        }

        // 11. Configurar SunlightController con el Dial Solar
        Light dirLight = FindDirectionalLight();
        if (dirLight != null)
        {
            SunlightController sunCtrl = dirLight.GetComponent<SunlightController>();
            if (sunCtrl == null) sunCtrl = dirLight.gameObject.AddComponent<SunlightController>();

            SerializedObject soSun = new SerializedObject(sunCtrl);
            soSun.FindProperty("sunLight").objectReferenceValue = dirLight;
            if (sunHourText != null) soSun.FindProperty("hourDisplayText").objectReferenceValue = sunHourText;
            soSun.ApplyModifiedProperties();

            if (sunSlider != null)
            {
                sunSlider.onValueChanged.RemoveAllListeners();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(sunSlider.onValueChanged, sunCtrl.SetTimeOfDay);
            }
        }

        // 12. Configurar PanelExploder con las 3 capas exactas del prefab
        if (targetPanel != null)
        {
            PanelExploder exploder = targetPanel.GetComponent<PanelExploder>();
            if (exploder == null) exploder = targetPanel.AddComponent<PanelExploder>();

            Transform meshMetal = targetPanel.transform.Find("MallaMetalicaPanel");
            Transform glassPanel = targetPanel.transform.Find("PanelCristal_PanelSolar");
            if (glassPanel == null) glassPanel = targetPanel.transform.Find("PanelSolar");
            Transform solarCell = targetPanel.transform.Find("CeldaSolar");

            // Optimizar MeshRenderer del cristal (Sin Cast Shadows para Android)
            if (glassPanel != null)
            {
                MeshRenderer mr = glassPanel.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            SerializedObject soExp = new SerializedObject(exploder);
            if (meshMetal != null) soExp.FindProperty("mallaMetalica").objectReferenceValue = meshMetal;
            if (glassPanel != null) soExp.FindProperty("panelCristal").objectReferenceValue = glassPanel;
            if (solarCell != null) soExp.FindProperty("celdaCaptadora").objectReferenceValue = solarCell;

            soExp.FindProperty("explodeSlider").objectReferenceValue = layersSlider;
            if (layersPercentText != null) soExp.FindProperty("explodePercentText").objectReferenceValue = layersPercentText;
            if (camController != null) soExp.FindProperty("cameraController").objectReferenceValue = camController;

            // Eje perpendicular a la inclinación de 38° del panel solar
            soExp.FindProperty("localSeparationAxis").vector3Value = new Vector3(0.63f, 0.77f, 0f);
            soExp.FindProperty("topDisplacement").floatValue = 0.75f;
            soExp.FindProperty("midDisplacement").floatValue = 0.45f;
            soExp.FindProperty("botDisplacement").floatValue = 0.20f;
            soExp.ApplyModifiedProperties();

            if (layersSlider != null)
            {
                layersSlider.onValueChanged.RemoveAllListeners();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(layersSlider.onValueChanged, exploder.SetExplodeProgress);
            }
        }

        // 13. Configurar EnergyFlowController
        GameObject managerGO = GameObject.Find("SolParaTodos_Manager");
        if (managerGO == null) managerGO = new GameObject("SolParaTodos_Manager");

        EnergyFlowController flowCtrl = managerGO.GetComponent<EnergyFlowController>();
        if (flowCtrl == null) flowCtrl = managerGO.AddComponent<EnergyFlowController>();

        Material cableMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlasticCableMaterial.mat");
        SerializedObject soFlowCtrl = new SerializedObject(flowCtrl);
        if (energyToggle != null) soFlowCtrl.FindProperty("masterSwitchToggle").objectReferenceValue = energyToggle;
        if (cableMat != null) soFlowCtrl.FindProperty("cableSharedMaterial").objectReferenceValue = cableMat;
        soFlowCtrl.ApplyModifiedProperties();

        if (energyToggle != null)
        {
            energyToggle.onValueChanged.RemoveAllListeners();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(energyToggle.onValueChanged, flowCtrl.SetEnergyFlow);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = mainCam != null ? mainCam.gameObject : canvas.gameObject;
        Debug.Log("<color=green><b>[SolParaTodos] ¡Cámara posicionada en el camino del patio y capas vinculadas con éxito!</b></color>");
    }

    private static void OptimizeGlassMaterial()
    {
        Material glassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Glass.mat");
        if (glassMat != null)
        {
            if (glassMat.HasProperty("_BaseColor"))
            {
                Color c = glassMat.GetColor("_BaseColor");
                c.a = 0.22f; // Translúcido limpio para ver la celda debajo
                glassMat.SetColor("_BaseColor", c);
            }
            if (glassMat.HasProperty("_Smoothness"))
            {
                glassMat.SetFloat("_Smoothness", 0.94f);
            }
            EditorUtility.SetDirty(glassMat);
        }
    }

    private static GameObject CreateARHUD(Transform parent, Sprite glassPill, Sprite circleKnob, Sprite sunIcon, Sprite sunArc,
        out Slider sunSlider, out Text sunHourText,
        out GameObject layersPanel, out Slider layersSlider, out Text layersPercentText,
        out Button modeBtn, out Text modeBtnText,
        out Toggle energyToggle)
    {
        GameObject hud = new GameObject("MainUIPanel");
        hud.transform.SetParent(parent, false);
        RectTransform rtHUD = hud.AddComponent<RectTransform>();
        rtHUD.anchorMin = Vector2.zero;
        rtHUD.anchorMax = Vector2.one;
        rtHUD.sizeDelta = Vector2.zero;

        // 1. HEADER MINIMALISTA SUPERIOR
        GameObject header = new GameObject("TopBadge");
        header.transform.SetParent(hud.transform, false);
        RectTransform rtHead = header.AddComponent<RectTransform>();
        rtHead.anchorMin = new Vector2(0.5f, 1f);
        rtHead.anchorMax = new Vector2(0.5f, 1f);
        rtHead.pivot = new Vector2(0.5f, 1f);
        rtHead.anchoredPosition = new Vector2(0, -14);
        rtHead.sizeDelta = new Vector2(420, 40);

        Image imgHead = header.AddComponent<Image>();
        imgHead.sprite = glassPill;
        imgHead.type = Image.Type.Sliced;
        imgHead.color = new Color(0.06f, 0.10f, 0.16f, 0.85f);
        imgHead.raycastTarget = false;

        GameObject titleTxtGO = new GameObject("Title");
        titleTxtGO.transform.SetParent(header.transform, false);
        RectTransform rtT = titleTxtGO.AddComponent<RectTransform>();
        rtT.sizeDelta = rtHead.sizeDelta;
        Text txtT = titleTxtGO.AddComponent<Text>();
        txtT.text = "☀️ Sol Para Todos  |  Diego Arellano";
        txtT.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtT.fontSize = 16;
        txtT.fontStyle = FontStyle.Bold;
        txtT.alignment = TextAnchor.MiddleCenter;
        txtT.color = new Color(0.95f, 0.95f, 1.0f);
        txtT.raycastTarget = false;

        // 2. BOTÓN DE NAVEGACIÓN DE MODO (Arriba a la Derecha)
        modeBtn = CreateFloatingButton(hud.transform, glassPill, "Btn_ToggleMode", "🧩 Ver Capas (Despiece)",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -14), new Vector2(220, 42), out modeBtnText);

        // 3. DIAL SOLAR EN ARCO INFERIOR (BOSQUEJO AR 2)
        GameObject sunDock = new GameObject("SunDialDock");
        sunDock.transform.SetParent(hud.transform, false);
        RectTransform rtSunDock = sunDock.AddComponent<RectTransform>();
        rtSunDock.anchorMin = new Vector2(0.5f, 0f);
        rtSunDock.anchorMax = new Vector2(0.5f, 0f);
        rtSunDock.pivot = new Vector2(0.5f, 0f);
        rtSunDock.anchoredPosition = new Vector2(0, 14);
        rtSunDock.sizeDelta = new Vector2(460, 130);

        Image imgSunDock = sunDock.AddComponent<Image>();
        imgSunDock.sprite = glassPill;
        imgSunDock.type = Image.Type.Sliced;
        imgSunDock.color = new Color(0.06f, 0.10f, 0.16f, 0.82f);
        imgSunDock.raycastTarget = false;

        // Gráfico de Arco Solar
        GameObject arcGO = new GameObject("SunArcGraphic");
        arcGO.transform.SetParent(sunDock.transform, false);
        RectTransform rtArc = arcGO.AddComponent<RectTransform>();
        rtArc.anchoredPosition = new Vector2(0, 12);
        rtArc.sizeDelta = new Vector2(400, 90);
        Image imgArc = arcGO.AddComponent<Image>();
        imgArc.sprite = sunArc;
        imgArc.color = new Color(1f, 1f, 1f, 0.65f);
        imgArc.raycastTarget = false;

        // Marcas 6 AM, 12 PM, 6 PM
        CreateSmallTag(sunDock.transform, "6 AM", new Vector2(-175, -12));
        CreateSmallTag(sunDock.transform, "6 PM", new Vector2(175, -12));

        // Texto digital de la hora destacada
        GameObject hourGO = new GameObject("Text_HoraSolar");
        hourGO.transform.SetParent(sunDock.transform, false);
        RectTransform rtHour = hourGO.AddComponent<RectTransform>();
        rtHour.anchoredPosition = new Vector2(0, -8);
        rtHour.sizeDelta = new Vector2(180, 32);
        sunHourText = hourGO.AddComponent<Text>();
        sunHourText.text = "01:00 PM";
        sunHourText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sunHourText.fontSize = 22;
        sunHourText.fontStyle = FontStyle.Bold;
        sunHourText.alignment = TextAnchor.MiddleCenter;
        sunHourText.color = new Color(1.0f, 0.88f, 0.40f);
        sunHourText.raycastTarget = false;

        // Slider del Sol
        sunSlider = CreateSunDialSlider(sunDock.transform, sunIcon, circleKnob, new Vector2(0, 22), new Vector2(360, 36));

        // Subtítulo del Dial
        GameObject subDialGO = new GameObject("SubLabel");
        subDialGO.transform.SetParent(sunDock.transform, false);
        RectTransform rtSub = subDialGO.AddComponent<RectTransform>();
        rtSub.anchoredPosition = new Vector2(0, -40);
        rtSub.sizeDelta = new Vector2(380, 18);
        Text txtSub = subDialGO.AddComponent<Text>();
        txtSub.text = "Arrastra el Sol para simular las sombras del día";
        txtSub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtSub.fontSize = 11;
        txtSub.alignment = TextAnchor.MiddleCenter;
        txtSub.color = new Color(0.70f, 0.78f, 0.88f);
        txtSub.raycastTarget = false;

        // 4. MODO 'VER CAPAS': SLIDER VERTICAL A LA DERECHA (BOSQUEJO AR 4)
        layersPanel = new GameObject("LayersPanel_Right");
        layersPanel.transform.SetParent(hud.transform, false);
        RectTransform rtLP = layersPanel.AddComponent<RectTransform>();
        rtLP.anchorMin = new Vector2(1f, 0.5f);
        rtLP.anchorMax = new Vector2(1f, 0.5f);
        rtLP.pivot = new Vector2(1f, 0.5f);
        rtLP.anchoredPosition = new Vector2(-20, 0);
        rtLP.sizeDelta = new Vector2(105, 340);

        Image imgLP = layersPanel.AddComponent<Image>();
        imgLP.sprite = glassPill;
        imgLP.type = Image.Type.Sliced;
        imgLP.color = new Color(0.06f, 0.10f, 0.16f, 0.88f);

        GameObject titleCapas = new GameObject("Label_VerCapas");
        titleCapas.transform.SetParent(layersPanel.transform, false);
        RectTransform rtTC = titleCapas.AddComponent<RectTransform>();
        rtTC.anchoredPosition = new Vector2(0, 142);
        rtTC.sizeDelta = new Vector2(95, 24);
        Text txtCapas = titleCapas.AddComponent<Text>();
        txtCapas.text = "Ver Capas";
        txtCapas.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txtCapas.fontSize = 14;
        txtCapas.fontStyle = FontStyle.Bold;
        txtCapas.alignment = TextAnchor.MiddleCenter;
        txtCapas.color = Color.white;
        txtCapas.raycastTarget = false;

        GameObject pctGO = new GameObject("Text_Percent");
        pctGO.transform.SetParent(layersPanel.transform, false);
        RectTransform rtPct = pctGO.AddComponent<RectTransform>();
        rtPct.anchoredPosition = new Vector2(0, 118);
        rtPct.sizeDelta = new Vector2(95, 22);
        layersPercentText = pctGO.AddComponent<Text>();
        layersPercentText.text = "0%";
        layersPercentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        layersPercentText.fontSize = 16;
        layersPercentText.fontStyle = FontStyle.Bold;
        layersPercentText.alignment = TextAnchor.MiddleCenter;
        layersPercentText.color = new Color(0.35f, 0.85f, 1.0f);
        layersPercentText.raycastTarget = false;

        layersSlider = CreateVerticalSlider(layersPanel.transform, glassPill, circleKnob, new Vector2(0, -15), new Vector2(24, 215));
        layersPanel.SetActive(false);

        // 5. INTERRUPTOR DE FLUJO ELÉCTRICO (Abajo a la Izquierda)
        energyToggle = CreateFloatingToggle(hud.transform, glassPill, circleKnob, "Toggle_FlujoElectrico",
            "⚡ Flujo Eléctrico", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20, 14), new Vector2(200, 44));

        return hud;
    }

    private static void CreateSmallTag(Transform parent, string text, Vector2 pos)
    {
        GameObject go = new GameObject("Tag_" + text);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(50, 20);
        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 11;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.75f, 0.82f, 0.92f);
        txt.raycastTarget = false;
    }

    private static Button CreateFloatingButton(Transform parent, Sprite pillSprite, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, out Text btnText)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMax;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.sprite = pillSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.08f, 0.48f, 0.85f, 0.95f);

        Button btn = go.AddComponent<Button>();

        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        RectTransform rtT = txtGO.AddComponent<RectTransform>();
        rtT.sizeDelta = rt.sizeDelta;
        btnText = txtGO.AddComponent<Text>();
        btnText.text = label;
        btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.fontSize = 14;
        btnText.fontStyle = FontStyle.Bold;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;
        btnText.raycastTarget = false;

        return btn;
    }

    private static Slider CreateSunDialSlider(Transform parent, Sprite sunIcon, Sprite circleSprite, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject("Slider_SunControl");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        RectTransform rtBg = bgGO.AddComponent<RectTransform>();
        rtBg.sizeDelta = new Vector2(size.x, 8);
        Image imgBg = bgGO.AddComponent<Image>();
        imgBg.sprite = circleSprite;
        imgBg.color = new Color(1f, 1f, 1f, 0.20f);

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        RectTransform rtFillArea = fillArea.AddComponent<RectTransform>();
        rtFillArea.sizeDelta = new Vector2(size.x - 20, 8);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform rtFill = fill.AddComponent<RectTransform>();
        rtFill.sizeDelta = Vector2.zero;
        Image imgFill = fill.AddComponent<Image>();
        imgFill.sprite = circleSprite;
        imgFill.color = new Color(1.0f, 0.75f, 0.25f, 0.70f);

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        RectTransform rtHandleArea = handleArea.AddComponent<RectTransform>();
        rtHandleArea.sizeDelta = new Vector2(size.x - 20, 36);

        GameObject handle = new GameObject("Handle_Sun");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform rtHandle = handle.AddComponent<RectTransform>();
        rtHandle.sizeDelta = new Vector2(36, 36);
        Image imgHandle = handle.AddComponent<Image>();
        imgHandle.sprite = sunIcon;
        imgHandle.color = Color.white;

        Slider slider = go.AddComponent<Slider>();
        slider.fillRect = rtFill;
        slider.handleRect = rtHandle;
        slider.targetGraphic = imgHandle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.5833f;

        return slider;
    }

    private static Slider CreateVerticalSlider(Transform parent, Sprite trackSprite, Sprite circleSprite, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject("VerticalSlider_Layers");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        RectTransform rtBg = bgGO.AddComponent<RectTransform>();
        rtBg.sizeDelta = new Vector2(8, size.y);
        Image imgBg = bgGO.AddComponent<Image>();
        imgBg.sprite = trackSprite;
        imgBg.type = Image.Type.Sliced;
        imgBg.color = new Color(0.25f, 0.32f, 0.42f, 0.8f);

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        RectTransform rtFillArea = fillArea.AddComponent<RectTransform>();
        rtFillArea.sizeDelta = new Vector2(8, size.y - 20);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform rtFill = fill.AddComponent<RectTransform>();
        rtFill.sizeDelta = Vector2.zero;
        Image imgFill = fill.AddComponent<Image>();
        imgFill.sprite = trackSprite;
        imgFill.type = Image.Type.Sliced;
        imgFill.color = new Color(0.98f, 0.65f, 0.20f, 1f);

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        RectTransform rtHandleArea = handleArea.AddComponent<RectTransform>();
        rtHandleArea.sizeDelta = new Vector2(28, size.y - 20);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform rtHandle = handle.AddComponent<RectTransform>();
        rtHandle.sizeDelta = new Vector2(26, 26);
        Image imgHandle = handle.AddComponent<Image>();
        imgHandle.sprite = circleSprite;
        imgHandle.color = Color.white;

        Slider slider = go.AddComponent<Slider>();
        slider.direction = Slider.Direction.BottomToTop;
        slider.fillRect = rtFill;
        slider.handleRect = rtHandle;
        slider.targetGraphic = imgHandle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        return slider;
    }

    private static Toggle CreateFloatingToggle(Transform parent, Sprite pillSprite, Sprite circleSprite, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image imgBase = go.AddComponent<Image>();
        imgBase.sprite = pillSprite;
        imgBase.type = Image.Type.Sliced;
        imgBase.color = new Color(0.06f, 0.10f, 0.16f, 0.88f);

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        RectTransform rtBg = bgGO.AddComponent<RectTransform>();
        rtBg.anchoredPosition = new Vector2(65, 0);
        rtBg.sizeDelta = new Vector2(44, 22);
        Image imgBg = bgGO.AddComponent<Image>();
        imgBg.sprite = pillSprite;
        imgBg.type = Image.Type.Sliced;
        imgBg.color = new Color(0.20f, 0.26f, 0.35f, 1f);

        GameObject checkGO = new GameObject("Checkmark");
        checkGO.transform.SetParent(bgGO.transform, false);
        RectTransform rtCheck = checkGO.AddComponent<RectTransform>();
        rtCheck.sizeDelta = new Vector2(18, 18);
        Image imgCheck = checkGO.AddComponent<Image>();
        imgCheck.sprite = circleSprite;
        imgCheck.color = new Color(0.18f, 0.90f, 0.45f, 1f);

        GameObject lblGO = new GameObject("Label");
        lblGO.transform.SetParent(go.transform, false);
        RectTransform rtLbl = lblGO.AddComponent<RectTransform>();
        rtLbl.anchoredPosition = new Vector2(-24, 0);
        rtLbl.sizeDelta = new Vector2(125, 28);
        Text lbl = lblGO.AddComponent<Text>();
        lbl.text = label;
        lbl.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lbl.fontSize = 13;
        lbl.fontStyle = FontStyle.Bold;
        lbl.alignment = TextAnchor.MiddleLeft;
        lbl.color = Color.white;
        lbl.raycastTarget = false;

        Toggle toggle = go.AddComponent<Toggle>();
        toggle.graphic = imgCheck;
        toggle.targetGraphic = imgBg;
        toggle.isOn = false;

        return toggle;
    }

    private static void UpgradeVisualEnvironment()
    {
        Volume volume = Object.FindFirstObjectByType<Volume>();
        if (volume != null && volume.profile != null)
        {
            VolumeProfile profile = volume.profile;

            ColorAdjustments colorAdj;
            if (!profile.TryGet(out colorAdj)) colorAdj = profile.Add<ColorAdjustments>(true);
            colorAdj.active = true;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 18f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 18f;
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.18f;

            Bloom bloom;
            if (profile.TryGet(out bloom))
            {
                bloom.active = true;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.50f;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 0.95f;
            }

            Tonemapping tone;
            if (profile.TryGet(out tone))
            {
                tone.active = true;
                tone.mode.overrideState = true;
                tone.mode.value = TonemappingMode.Neutral;
            }

            EditorUtility.SetDirty(profile);
        }

        Light dirLight = FindDirectionalLight();
        if (dirLight != null)
        {
            dirLight.intensity = 1.15f;
            dirLight.shadows = LightShadows.Soft;
            dirLight.color = new Color(1.0f, 0.98f, 0.92f);
        }
    }

    private static Canvas EnsureCanvas()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("MainCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create MainCanvas");
        }
        return canvas;
    }

    private static void EnsureEventSystem()
    {
        EventSystem es = Object.FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            es = esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
            return;
        }

        StandaloneInputModule legacy = es.GetComponent<StandaloneInputModule>();
        if (legacy != null)
        {
            Object.DestroyImmediate(legacy);
        }

        InputSystemUIInputModule uiMod = es.GetComponent<InputSystemUIInputModule>();
        if (uiMod == null)
        {
            uiMod = es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
        uiMod.AssignDefaultActions();
    }

    private static void ConfigureLogosAsSprites()
    {
        string[] paths = new string[] { "Assets/Logo/logo_umarista.png", "Assets/Logo/SolParaTodos.jpg" };
        foreach (string p in paths)
        {
            TextureImporter importer = AssetImporter.GetAtPath(p) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
        }
    }

    private static GameObject CreateSplashPanel(Transform parent, Sprite glassPill)
    {
        GameObject splash = new GameObject("SplashPanel");
        splash.transform.SetParent(parent, false);

        RectTransform rt = splash.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        Image bg = splash.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.07f, 0.11f, 1.0f);
        bg.raycastTarget = false;

        CanvasGroup cg = splash.AddComponent<CanvasGroup>();
        cg.alpha = 1.0f;
        cg.blocksRaycasts = true;

        GameObject card = new GameObject("SplashCard");
        card.transform.SetParent(splash.transform, false);
        RectTransform rtCard = card.AddComponent<RectTransform>();
        rtCard.anchorMin = new Vector2(0.5f, 0.5f);
        rtCard.anchorMax = new Vector2(0.5f, 0.5f);
        rtCard.sizeDelta = new Vector2(560, 460);

        Image imgCard = card.AddComponent<Image>();
        imgCard.sprite = glassPill;
        imgCard.type = Image.Type.Sliced;
        imgCard.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
        imgCard.raycastTarget = false;

        Sprite spSol = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Logo/SolParaTodos.jpg");
        if (spSol != null)
        {
            GameObject logoSol = new GameObject("Logo_SolParaTodos");
            logoSol.transform.SetParent(card.transform, false);
            RectTransform rtSol = logoSol.AddComponent<RectTransform>();
            rtSol.anchorMin = new Vector2(0.5f, 0.72f);
            rtSol.anchorMax = new Vector2(0.5f, 0.72f);
            rtSol.sizeDelta = new Vector2(340, 170);

            Image imgSol = logoSol.AddComponent<Image>();
            imgSol.sprite = spSol;
            imgSol.preserveAspect = true;
            imgSol.raycastTarget = false;
        }

        Sprite spMarista = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Logo/logo_umarista.png");
        if (spMarista != null)
        {
            GameObject logoMar = new GameObject("Logo_UMarista");
            logoMar.transform.SetParent(card.transform, false);
            RectTransform rtMar = logoMar.AddComponent<RectTransform>();
            rtMar.anchorMin = new Vector2(0.5f, 0.38f);
            rtMar.anchorMax = new Vector2(0.5f, 0.38f);
            rtMar.sizeDelta = new Vector2(230, 75);

            Image imgMar = logoMar.AddComponent<Image>();
            imgMar.sprite = spMarista;
            imgMar.preserveAspect = true;
            imgMar.raycastTarget = false;
        }

        GameObject textGO = new GameObject("Text_Autor");
        textGO.transform.SetParent(card.transform, false);
        RectTransform rtText = textGO.AddComponent<RectTransform>();
        rtText.anchorMin = new Vector2(0.5f, 0.14f);
        rtText.anchorMax = new Vector2(0.5f, 0.14f);
        rtText.sizeDelta = new Vector2(500, 55);

        Text txt = textGO.AddComponent<Text>();
        txt.text = "Desarrollado por: Diego Arellano Gordillo\nProyecto SolParaTodos - Gráficos en AR";
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 18;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.85f, 0.90f, 0.98f, 1f);
        txt.raycastTarget = false;

        return splash;
    }

    private static Sprite GetOrCreateGlassPillSprite()
    {
        string path = "Assets/Textures/UI_GlassPill.png";
        if (File.Exists(path))
        {
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp != null) return sp;
        }

        int size = 64;
        int radius = 18;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(radius, radius, radius, radius);
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateCircleKnobSprite()
    {
        string path = "Assets/Textures/UI_CircleKnob.png";
        if (File.Exists(path))
        {
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp != null) return sp;
        }

        int size = 64;
        float radius = size * 0.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateSunIconSprite()
    {
        string path = "Assets/Textures/UI_SunIcon.png";
        if (File.Exists(path))
        {
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp != null) return sp;
        }

        int size = 64;
        float center = size * 0.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float discAlpha = Mathf.Clamp01(12f - dist);
                float angle = Mathf.Atan2(dy, dx);
                float rayPattern = Mathf.Pow(Mathf.Cos(angle * 4f), 8f);
                float rayAlpha = Mathf.Clamp01(26f - dist) * Mathf.Clamp01(dist - 10f) * rayPattern;

                float finalAlpha = Mathf.Max(discAlpha, rayAlpha);
                colors[y * size + x] = new Color(1.0f, 0.82f, 0.25f, finalAlpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateSunArcSprite()
    {
        string path = "Assets/Textures/UI_SunArc.png";
        if (File.Exists(path))
        {
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp != null) return sp;
        }

        int w = 512;
        int h = 180;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] colors = new Color[w * h];

        Vector2 arcCenter = new Vector2(w * 0.5f, 20f);
        float arcRadius = 135f;
        float thickness = 4.5f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 pt = new Vector2(x, y);
                float dist = Vector2.Distance(pt, arcCenter);

                float distToRing = Mathf.Abs(dist - arcRadius);
                float alpha = Mathf.Clamp01(thickness - distToRing);

                if (y < 20) alpha = 0f;

                colors[y * w + x] = new Color(0.95f, 0.80f, 0.35f, alpha * 0.75f);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Light FindDirectionalLight()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in lights)
        {
            if (l.type == LightType.Directional) return l;
        }
        return null;
    }
}
