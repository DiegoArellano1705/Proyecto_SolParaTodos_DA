using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Simula la trayectoria solar diaria de 6:00 AM a 6:00 PM mediante un valor normalizado (0 a 1).
/// Ajusta la rotación, intensidad y temperatura cromática de la Directional Light.
/// Se auto-vincula en tiempo de ejecución al Dial Solar (Slider_SunControl).
/// </summary>
public class SunlightController : MonoBehaviour
{
    [Header("Referencias de Iluminación")]
    [Tooltip("Luz direccional que simula el Sol. Si está vacía, se busca automáticamente.")]
    [SerializeField] private Light sunLight;

    [Header("Horario Solar (0.0 = 6:00 AM, 1.0 = 6:00 PM)")]
    [Range(0f, 1f)]
    [Tooltip("Tiempo normalizado del día. 0 = 6:00 AM, ~0.58 = 1:00 PM (Cenit), 1.0 = 6:00 PM.")]
    [SerializeField] private float timeOfDay = 0.5833f;

    [Header("Parámetros de Rotación Solar")]
    [Tooltip("Ángulo Azimut al amanecer (6:00 AM - Este).")]
    [SerializeField] private float sunriseAzimuth = 70f;

    [Tooltip("Ángulo Azimut al atardecer (6:00 PM - Oeste).")]
    [SerializeField] private float sunsetAzimuth = 270f;

    [Tooltip("Inclinación solar mínima en horizonte (6 AM y 6 PM).")]
    [SerializeField] private float horizonElevation = 10f;

    [Tooltip("Inclinación solar en Cenit (1:00 PM).")]
    [SerializeField] private float cenitElevation = 82f;

    [Tooltip("Hora normalizada correspondiente al Cenit (1:00 PM = 7 horas transcurridas de 12 = 0.5833).")]
    [SerializeField] private float cenitNormalizedTime = 0.5833f;

    [Header("Gradientes de Iluminación")]
    [SerializeField] private Gradient sunColorGradient;
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0.75f, 1f, 0.75f);

    [Header("Eventos y UI")]
    [Tooltip("Texto UI opcional para mostrar la hora directamente.")]
    [SerializeField] private Text hourDisplayText;

    [Tooltip("Devuelve el texto formateado de la hora (ej: 01:00 PM) para TextMeshPro.")]
    public UnityEvent<string> onTimeTextChanged;

    private Slider sunSlider;

    private void Awake()
    {
        if (sunLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sunLight = l;
                    break;
                }
            }
        }

        if (sunColorGradient == null || sunColorGradient.colorKeys.Length == 0)
        {
            SetupDefaultGradient();
        }

        // Configurar curva de intensidad con pico en el cenit (1:00 PM)
        Keyframe[] keys = new Keyframe[3];
        keys[0] = new Keyframe(0.0f, 0.65f);
        keys[1] = new Keyframe(0.5833f, 1.30f);
        keys[2] = new Keyframe(1.0f, 0.65f);
        intensityCurve = new AnimationCurve(keys);

        UpdateSunPosition();
    }

    private void Start()
    {
        Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Slider s in sliders)
        {
            if (s != null && (s.gameObject.name == "Slider_SunControl" || s.gameObject.name == "Slider_HoraSolar"))
            {
                sunSlider = s;
                sunSlider.onValueChanged.RemoveListener(SetTimeOfDay);
                sunSlider.onValueChanged.AddListener(SetTimeOfDay);
                break;
            }
        }

        if (hourDisplayText == null)
        {
            Text[] texts = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Text t in texts)
            {
                if (t != null && t.gameObject.name == "Text_HoraSolar")
                {
                    hourDisplayText = t;
                    break;
                }
            }
        }

        UpdateSunPosition();
    }

    /// <summary>
    /// Conexión directa para Slider UI (0 a 1).
    /// </summary>
    public void SetTimeOfDay(float normalizedValue)
    {
        timeOfDay = Mathf.Clamp01(normalizedValue);
        UpdateSunPosition();
    }

    private void UpdateSunPosition()
    {
        if (sunLight == null) return;

        // 1. Cálculo de Elevación (Pitch - Eje X): Curva parabólica alcanzando el cenit a la 1:00 PM
        float elevation;
        if (timeOfDay <= cenitNormalizedTime)
        {
            float t = timeOfDay / cenitNormalizedTime;
            elevation = Mathf.Lerp(horizonElevation, cenitElevation, Mathf.Sin(t * Mathf.PI * 0.5f));
        }
        else
        {
            float t = (timeOfDay - cenitNormalizedTime) / (1f - cenitNormalizedTime);
            elevation = Mathf.Lerp(cenitElevation, horizonElevation, 1f - Mathf.Cos(t * Mathf.PI * 0.5f));
        }

        // 2. Cálculo de Azimut (Yaw - Eje Y): Trayectoria continua Este -> Oeste
        float azimuth = Mathf.Lerp(sunriseAzimuth, sunsetAzimuth, timeOfDay);

        // 3. Aplicar rotación a la Directional Light
        sunLight.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);

        // 4. Color e intensidad según hora del día
        if (sunColorGradient != null)
        {
            sunLight.color = sunColorGradient.Evaluate(timeOfDay);
        }

        if (intensityCurve != null)
        {
            sunLight.intensity = intensityCurve.Evaluate(timeOfDay);
        }

        // 5. Emitir texto legible para UI (6:00 AM a 6:00 PM)
        string formattedTime = GetFormattedTimeString(timeOfDay);
        if (hourDisplayText != null)
        {
            hourDisplayText.text = formattedTime;
        }
        onTimeTextChanged?.Invoke(formattedTime);
    }

    private string GetFormattedTimeString(float t)
    {
        float totalHours = 6f + (t * 12f);
        int hours = Mathf.FloorToInt(totalHours);
        int minutes = Mathf.FloorToInt((totalHours - hours) * 60f);

        string period = (hours >= 12) ? "PM" : "AM";
        int displayHours = hours;
        if (displayHours > 12) displayHours -= 12;
        if (displayHours == 0) displayHours = 12;

        return $"{displayHours:00}:{minutes:00} {period}";
    }

    private void SetupDefaultGradient()
    {
        sunColorGradient = new Gradient();

        GradientColorKey[] colorKeys = new GradientColorKey[3];
        colorKeys[0] = new GradientColorKey(new Color(1.0f, 0.72f, 0.45f), 0.0f);  // 6:00 AM
        colorKeys[1] = new GradientColorKey(new Color(1.0f, 0.98f, 0.92f), 0.58f); // 1:00 PM Cenit
        colorKeys[2] = new GradientColorKey(new Color(1.0f, 0.58f, 0.35f), 1.0f);  // 6:00 PM

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1.0f, 0.0f);
        alphaKeys[1] = new GradientAlphaKey(1.0f, 1.0f);

        sunColorGradient.SetKeys(colorKeys, alphaKeys);
    }
}
