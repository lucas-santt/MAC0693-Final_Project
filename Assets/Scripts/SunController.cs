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

    [Header("Controle em VR (joystick)")]
    [Tooltip("Ação Vector2. Se ficar vazia, usa automaticamente o joystick do controle ESQUERDO.")]
    public InputActionProperty timeScrubAction;
    [Tooltip("Desliga a locomoção contínua do template, que também usa o joystick esquerdo")]
    public bool disableContinuousMove = true;

    [Tooltip("Inclinação mínima do joystick para contar como toque")]
    [Range(0.2f, 0.9f)] public float stickThreshold = 0.5f;
    [Tooltip("Tempo segurando antes de começar a acelerar (s)")]
    public float holdDelay = 0.35f;
    [Tooltip("Tempo para chegar à velocidade máxima depois do atraso (s)")]
    public float accelerationTime = 1.5f;

    [Tooltip("Hora (←/→): cada toque avança/volta este tanto, em horas (0,25 = 15 min)")]
    public float hourTapStep = 0.25f;
    [Tooltip("Hora (←/→): velocidade máxima segurando, em horas por segundo")]
    public float hourMaxSpeed = 2f;

    [Tooltip("Data (↑/↓): cada toque avança/volta este tanto, em dias")]
    public int dateTapStepDays = 7;
    [Tooltip("Data (↑/↓): velocidade máxima segurando, em dias por segundo")]
    public float dateMaxSpeedDays = 30f;

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

    // Joystick
    private InputAction ownScrubAction;          // criada quando nenhuma ação foi associada
    private int heldAxis = -1;                   // -1 = solto, 0 = horizontal (hora), 1 = vertical (data)
    private float heldSign;
    private float heldTime;
    private float pendingDays;                   // fração de dia acumulada ao segurar o joystick na data

    /// <summary>Chamado sempre que hora ou data mudam (útil para um relógio de pulso).</summary>
    public event Action<SunController> TimeChanged;

    void OnEnable()
    {
        sun = GetComponent<Light>();
        if (RenderSettings.sun == null) RenderSettings.sun = sun;

        if (Application.isPlaying)
        {
            SetupScrubAction();
            if (disableContinuousMove) DisableContinuousMove();
        }

        UpdateSun();
    }

    void OnDisable()
    {
        if (ownScrubAction != null)
        {
            ownScrubAction.Disable();
            ownScrubAction.Dispose();
            ownScrubAction = null;
        }
    }

    private void SetupScrubAction()
    {
        InputAction assigned = timeScrubAction.action;
        if (assigned != null && assigned.bindings.Count > 0)
        {
            assigned.Enable();
            return;
        }

        // Nenhuma ação associada: joystick do controle esquerdo (qualquer controle XR)
        ownScrubAction = new InputAction("Time Scrub", InputActionType.Value, expectedControlType: "Vector2");
        ownScrubAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        ownScrubAction.Enable();
    }

    private Vector2 ReadScrub()
    {
        if (ownScrubAction != null) return ownScrubAction.ReadValue<Vector2>();
        InputAction a = timeScrubAction.action;
        return a != null ? a.ReadValue<Vector2>() : Vector2.zero;
    }

    // O VR Template usa o joystick esquerdo para andar. O design usa só teleporte, então a locomoção
    // contínua é desligada (por nome de tipo, para não depender da versão do XR Interaction Toolkit).
    private static void DisableContinuousMove()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                if (t.Name == "ContinuousMoveProvider")
                {
                    mb.enabled = false;
                    break;
                }
            }
        }
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

        bool changed = false;

        if (animate)
        {
            AddHours(hoursPerSecond * Time.deltaTime);
            changed = true;
        }

        changed |= HandleStick(ReadScrub());

        if (changed)
        {
            UpdateSun();
            TimeChanged?.Invoke(this);
        }
    }

    // Toque curto = um passo; segurando, depois de um instante acelera até a velocidade máxima
    private bool HandleStick(Vector2 stick)
    {
        int axis = -1;
        if (Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y)) >= stickThreshold)
        {
            axis = Mathf.Abs(stick.x) >= Mathf.Abs(stick.y) ? 0 : 1;
        }

        if (axis < 0)
        {
            heldAxis = -1;
            pendingDays = 0f;
            return false;
        }

        float value = axis == 0 ? stick.x : stick.y;
        float sign = Mathf.Sign(value);

        // Começou um novo toque (ou mudou de eixo/sentido): aplica um passo
        if (axis != heldAxis || sign != heldSign)
        {
            heldAxis = axis;
            heldSign = sign;
            heldTime = 0f;
            pendingDays = 0f;

            if (axis == 0) AddHours(sign * hourTapStep);
            else AddDays(Mathf.RoundToInt(sign) * dateTapStepDays);
            return true;
        }

        heldTime += Time.deltaTime;
        if (heldTime < holdDelay) return false;

        float ramp = accelerationTime > 0f ? Mathf.Clamp01((heldTime - holdDelay) / accelerationTime) : 1f;
        float strength = Mathf.InverseLerp(stickThreshold, 1f, Mathf.Abs(value));

        if (axis == 0)
        {
            float speed = Mathf.Lerp(hourTapStep * 4f, hourMaxSpeed, ramp) * Mathf.Lerp(0.5f, 1f, strength);
            AddHours(sign * speed * Time.deltaTime);
            return true;
        }

        float daySpeed = Mathf.Lerp(dateTapStepDays * 2f, dateMaxSpeedDays, ramp) * Mathf.Lerp(0.5f, 1f, strength);
        pendingDays += sign * daySpeed * Time.deltaTime;
        int wholeDays = (int)pendingDays;
        if (wholeDays == 0) return false;
        pendingDays -= wholeDays;
        AddDays(wholeDays);
        return true;
    }

    /// <summary>Avança (ou volta, se negativo) o relógio; passar da meia-noite muda o dia.</summary>
    public void AddHours(float hours)
    {
        float total = timeOfDay + hours;
        int dayShift = Mathf.FloorToInt(total / 24f);
        timeOfDay = total - dayShift * 24f;
        if (dayShift != 0) AddDays(dayShift);
    }

    /// <summary>Avança (ou volta) a data em dias. O ano é ignorado: depois de 31/12 vem 1/1.</summary>
    public void AddDays(int days)
    {
        DateTime date = new DateTime(2026, month, Mathf.Clamp(day, 1, DateTime.DaysInMonth(2026, month))).AddDays(days);
        month = date.Month;
        day = date.Day;
    }

    /// <summary>Texto pronto para mostrar na interface, ex.: "21/06 09:15".</summary>
    public string GetDisplayText()
    {
        int totalMinutes = Mathf.RoundToInt(timeOfDay * 60f) % (24 * 60);
        return $"{day:00}/{month:00}  {totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    /// <summary>Muda a hora (ex.: por um botão ou slider na interface).</summary>
    public void SetTime(float hour)
    {
        timeOfDay = Mathf.Repeat(hour, 24f);
        UpdateSun();
        TimeChanged?.Invoke(this);
    }

    /// <summary>Muda a data (ex.: solstício de verão = 21/12, de inverno = 21/6 no hemisfério sul).</summary>
    public void SetDate(int newMonth, int newDay)
    {
        month = Mathf.Clamp(newMonth, 1, 12);
        day = Mathf.Clamp(newDay, 1, DateTime.DaysInMonth(2026, month));
        UpdateSun();
        TimeChanged?.Invoke(this);
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
