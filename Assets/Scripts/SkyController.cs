using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Faz o céu e o ambiente acompanharem o horário do SunController: manhã, tarde, pôr do sol e noite.
/// Coloque este componente no mesmo objeto do SunController (a Directional Light).
///
/// - Céu procedural (Skybox/Procedural) que muda de cor com a altura do sol
/// - Luz ambiente (Trilight), neblina e exposição do céu por gradiente
/// - Lua (luz direcional fraca e azulada) e luzes internas que acendem à noite
/// - Céu estrelado opcional para a noite
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SunController))]
public class SkyController : MonoBehaviour
{
    public enum Period { Madrugada, Manha, Tarde, Noite }

    [Header("Céu")]
    [Tooltip("Material com o shader Skybox/Procedural (ex.: Assets/Materials/SkyProcedural.mat)")]
    public Material proceduralSky;
    [Tooltip("Opcional: skybox estrelado usado quando o sol está bem abaixo do horizonte")]
    public Material nightSky;
    [Tooltip("Altura do sol (graus) abaixo da qual troca para o céu noturno")]
    public float nightSkyBelowElevation = -10f;

    [Header("Cores por altura do sol (esquerda = noite, meio = horizonte, direita = meio-dia)")]
    public Gradient skyTint = MakeGradient(
        new Color(0.05f, 0.07f, 0.15f), new Color(0.85f, 0.45f, 0.35f), new Color(0.5f, 0.55f, 0.65f));
    public Gradient ambientSky = MakeGradient(
        new Color(0.03f, 0.04f, 0.08f), new Color(0.55f, 0.4f, 0.35f), new Color(0.45f, 0.6f, 0.85f));
    public Gradient ambientEquator = MakeGradient(
        new Color(0.02f, 0.02f, 0.04f), new Color(0.5f, 0.35f, 0.25f), new Color(0.45f, 0.45f, 0.45f));
    public Gradient ambientGround = MakeGradient(
        new Color(0.01f, 0.01f, 0.02f), new Color(0.2f, 0.15f, 0.12f), new Color(0.25f, 0.23f, 0.2f));
    public Gradient fogColor = MakeGradient(
        new Color(0.02f, 0.03f, 0.06f), new Color(0.75f, 0.5f, 0.4f), new Color(0.7f, 0.78f, 0.85f));

    [Tooltip("Exposição do céu procedural: noite -> horizonte -> meio-dia")]
    public AnimationCurve skyExposure = new AnimationCurve(
        new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.9f), new Keyframe(1f, 1.3f));
    [Tooltip("Espessura da atmosfera (mais alta deixa o pôr do sol mais avermelhado)")]
    public AnimationCurve atmosphereThickness = new AnimationCurve(
        new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 1f));

    [Header("Noite")]
    [Tooltip("Opcional: luz direcional fraca e azulada que faz o papel da lua")]
    public Light moonLight;
    public float moonIntensity = 0.15f;
    [Tooltip("Luzes internas da casa que acendem quando escurece")]
    public Light[] interiorLights;
    [Tooltip("Altura do sol (graus) abaixo da qual as luzes internas acendem")]
    public float lightsOnBelowElevation = 2f;

    [Header("Eventos")]
    [Tooltip("Chamado quando muda o período do dia (útil para mostrar 'Manhã', 'Tarde', 'Noite' na interface)")]
    public UnityEvent<Period> onPeriodChanged;

    public Period CurrentPeriod { get; private set; }

    private SunController sun;
    private Material skyInstance;
    private float lastElevation = float.NaN;
    private float lastTime = float.NaN;

    void OnEnable()
    {
        sun = GetComponent<SunController>();
        SetupSky();
        Refresh(force: true);
    }

    void OnDisable()
    {
        if (skyInstance != null)
        {
            if (Application.isPlaying) Destroy(skyInstance); else DestroyImmediate(skyInstance);
        }
    }

    void OnValidate()
    {
        if (sun == null) sun = GetComponent<SunController>();
        if (isActiveAndEnabled) Refresh(force: true);
    }

    void Update()
    {
        Refresh(force: false);
    }

    private void SetupSky()
    {
        Material source = proceduralSky;
        if (source == null)
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogWarning("[SkyController] Shader 'Skybox/Procedural' não encontrado. Associe um material em 'Procedural Sky'.", this);
                return;
            }
            source = new Material(shader);
            Debug.LogWarning("[SkyController] Nenhum material em 'Procedural Sky'; usando um temporário. " +
                             "Associe Assets/Materials/SkyProcedural.mat para garantir que o céu funcione no build.", this);
        }

        // Instância própria para não alterar o arquivo do material enquanto o horário muda
        skyInstance = new Material(source) { name = source.name + " (runtime)", hideFlags = HideFlags.DontSave };
    }

    private void Refresh(bool force)
    {
        if (sun == null) return;

        sun.UpdateSun();
        float elevation = sun.Elevation;
        if (!force && Mathf.Approximately(elevation, lastElevation) && Mathf.Approximately(sun.timeOfDay, lastTime)) return;
        lastElevation = elevation;
        lastTime = sun.timeOfDay;

        // 0 = noite (sol a -18°, fim do crepúsculo), 0.5 = horizonte, 1 = sol a 60° ou mais
        float t = elevation < 0f
            ? Mathf.Lerp(0f, 0.5f, Mathf.InverseLerp(-18f, 0f, elevation))
            : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(0f, 60f, elevation));

        // Céu
        bool useNightSky = nightSky != null && elevation < nightSkyBelowElevation;
        if (useNightSky)
        {
            RenderSettings.skybox = nightSky;
        }
        else if (skyInstance != null)
        {
            skyInstance.SetColor("_SkyTint", skyTint.Evaluate(t));
            skyInstance.SetFloat("_Exposure", skyExposure.Evaluate(t));
            skyInstance.SetFloat("_AtmosphereThickness", atmosphereThickness.Evaluate(t));
            RenderSettings.skybox = skyInstance;
        }

        // Luz ambiente em três tons (não depende de recalcular o GI a cada frame)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky.Evaluate(t);
        RenderSettings.ambientEquatorColor = ambientEquator.Evaluate(t);
        RenderSettings.ambientGroundColor = ambientGround.Evaluate(t);

        if (RenderSettings.fog) RenderSettings.fogColor = fogColor.Evaluate(t);

        // Lua: aparece quando o sol se põe, do lado oposto
        float nightFactor = Mathf.Clamp01(Mathf.InverseLerp(0f, -12f, elevation));
        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.Euler(35f, transform.eulerAngles.y + 180f, 0f);
            moonLight.intensity = moonIntensity * nightFactor;
            moonLight.enabled = nightFactor > 0.01f;
        }

        // Luzes da casa
        bool lightsOn = elevation < lightsOnBelowElevation;
        if (interiorLights != null)
        {
            foreach (Light l in interiorLights)
            {
                if (l != null) l.enabled = lightsOn;
            }
        }

        // Período do dia
        Period period = GetPeriod(elevation, sun.timeOfDay);
        if (force || period != CurrentPeriod)
        {
            CurrentPeriod = period;
            if (Application.isPlaying) onPeriodChanged?.Invoke(period);
        }
    }

    private static Period GetPeriod(float elevation, float hour)
    {
        if (elevation < -6f) return hour < 12f ? Period.Madrugada : Period.Noite;
        return hour < 12f ? Period.Manha : Period.Tarde;
    }

    private static Gradient MakeGradient(Color night, Color horizon, Color noon)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(night, 0f), new GradientColorKey(horizon, 0.5f), new GradientColorKey(noon, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
