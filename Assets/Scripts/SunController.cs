using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simula o sol real: posiciona a luz direcional de acordo com a localização, a data e a hora,
/// para ver como a luz entra pelas janelas ao longo do dia e do ano.
/// Coloque este componente na Directional Light da cena.
///
/// Posição solar calculada com as equações simplificadas da NOAA (precisão de ~1°, suficiente para estudo de insolação).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class SunController : MonoBehaviour
{
    [Header("Localização (padrão: Tatuapé, São Paulo)")]
    [Range(-90f, 90f)] public float latitude = -23.540f;
    [Range(-180f, 180f)] public float longitude = -46.576f;
    [Tooltip("Fuso horário em horas em relação ao UTC (São Paulo = -3)")]
    [Range(-12f, 14f)] public float utcOffset = -3f;

    [Tooltip("Para onde aponta o norte da planta: ângulo (graus, sentido horário visto de cima) entre o eixo +Z da cena e o norte real")]
    [Range(0f, 360f)] public float northOffset = 0f;

    [Header("Data e hora")]
    [Range(1, 12)] public int month = 6;
    [Range(1, 31)] public int day = 21;
    [Tooltip("Hora local (ex.: 14.5 = 14h30)")]
    [Range(0f, 24f)] public float timeOfDay = 9f;

    [Header("Animação")]
    [Tooltip("Avança o relógio automaticamente")]
    public bool animate = false;
    [Tooltip("Horas simuladas por segundo real")]
    public float hoursPerSecond = 0.5f;

    [Header("Controle em VR (opcional)")]
    [Tooltip("Ação Vector2 (ex.: joystick do controle esquerdo). Eixo X avança/volta a hora.")]
    public InputActionProperty timeScrubAction;
    [Tooltip("Horas por segundo com o joystick totalmente inclinado")]
    public float scrubSpeed = 2f;

    [Header("Aparência")]
    [Tooltip("Intensidade com o sol no alto")]
    public float maxIntensity = 2f;
    [Tooltip("Cor do sol conforme a altura: esquerda = horizonte, direita = zênite")]
    public Gradient sunColorByElevation = DefaultSunGradient();
    [Tooltip("Ajusta a luz ambiente conforme o sol (noite mais escura)")]
    public bool adjustAmbient = true;
    [Range(0f, 1f)] public float nightAmbient = 0.1f;
    [Range(0f, 2f)] public float dayAmbient = 1f;

    /// <summary>Altura do sol acima do horizonte, em graus (negativo = noite).</summary>
    public float Elevation { get; private set; }
    /// <summary>Azimute do sol em graus, a partir do norte, sentido horário.</summary>
    public float Azimuth { get; private set; }

    private Light sun;

    void OnEnable()
    {
        sun = GetComponent<Light>();
        if (timeScrubAction.action != null) timeScrubAction.action.Enable();
        if (RenderSettings.sun == null) RenderSettings.sun = sun;
        UpdateSun();
    }

    void OnValidate()
    {
        day = Mathf.Clamp(day, 1, DateTime.DaysInMonth(2026, month));
        if (sun == null) sun = GetComponent<Light>();
        UpdateSun();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        float delta = 0f;
        if (animate) delta += hoursPerSecond * Time.deltaTime;

        if (timeScrubAction.action != null)
        {
            Vector2 stick = timeScrubAction.action.ReadValue<Vector2>();
            if (Mathf.Abs(stick.x) > 0.2f) delta += stick.x * scrubSpeed * Time.deltaTime;
        }

        if (delta != 0f)
        {
            timeOfDay = Mathf.Repeat(timeOfDay + delta, 24f);
            UpdateSun();
        }
    }

    /// <summary>Muda a hora (ex.: por um botão ou slider na interface).</summary>
    public void SetTime(float hour)
    {
        timeOfDay = Mathf.Repeat(hour, 24f);
        UpdateSun();
    }

    /// <summary>Muda a data (ex.: solstício de verão = 21/12, de inverno = 21/6 no hemisfério sul).</summary>
    public void SetDate(int newMonth, int newDay)
    {
        month = Mathf.Clamp(newMonth, 1, 12);
        day = Mathf.Clamp(newDay, 1, DateTime.DaysInMonth(2026, month));
        UpdateSun();
    }

    public void UpdateSun()
    {
        if (sun == null) return;

        int dayOfYear = new DateTime(2026, month, Mathf.Clamp(day, 1, DateTime.DaysInMonth(2026, month))).DayOfYear;
        ComputeSolarPosition(dayOfYear, timeOfDay, latitude, longitude, utcOffset, out float elevation, out float azimuth);
        Elevation = elevation;
        Azimuth = azimuth;

        // A luz "anda" do sol para a cena: aponta para o lado oposto do azimute, inclinada pela altura
        transform.rotation = Quaternion.Euler(elevation, azimuth + 180f + northOffset, 0f);

        // Intensidade e cor: some abaixo do horizonte, fica alaranjada perto dele
        float dayFactor = Mathf.Clamp01(Mathf.InverseLerp(-2f, 10f, elevation));
        sun.intensity = maxIntensity * dayFactor;
        sun.color = sunColorByElevation.Evaluate(Mathf.Clamp01(elevation / 60f));
        sun.enabled = elevation > -2f;

        if (adjustAmbient)
        {
            RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbient, dayAmbient, dayFactor);
        }
    }

    // Equações da NOAA (General Solar Position Calculations)
    private static void ComputeSolarPosition(int dayOfYear, float localHour, float latDeg, float lonDeg, float tz,
                                             out float elevationDeg, out float azimuthDeg)
    {
        double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1 + (localHour - 12.0) / 24.0);

        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                  - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));

        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                      - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                      - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double timeOffset = eqTime + 4.0 * lonDeg - 60.0 * tz;   // minutos
        double trueSolarTime = localHour * 60.0 + timeOffset;    // minutos
        double hourAngle = (trueSolarTime / 4.0 - 180.0) * Math.PI / 180.0;

        double lat = latDeg * Math.PI / 180.0;
        double cosZenith = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(hourAngle);
        cosZenith = Math.Max(-1.0, Math.Min(1.0, cosZenith));
        double zenith = Math.Acos(cosZenith);

        // Azimute a partir do sul (positivo para oeste) -> converte para a partir do norte, sentido horário
        double azFromSouth = Math.Atan2(Math.Sin(hourAngle),
                                        Math.Cos(hourAngle) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat));

        elevationDeg = (float)(90.0 - zenith * 180.0 / Math.PI);
        azimuthDeg = (float)((azFromSouth * 180.0 / Math.PI + 180.0) % 360.0);
    }

    private static Gradient DefaultSunGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.45f, 0.2f), 0f),   // nascer/pôr do sol
                new GradientColorKey(new Color(1f, 0.8f, 0.6f), 0.2f),
                new GradientColorKey(new Color(1f, 0.96f, 0.9f), 1f),   // meio-dia
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
