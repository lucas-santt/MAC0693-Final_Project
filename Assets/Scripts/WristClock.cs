using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Relógio de pulso: mostra hora, data e período do dia (do SunController) acima do controle esquerdo.
/// Aparece ao mexer o joystick do tempo ou ao olhar para o pulso, e some sozinho depois.
/// Coloque este componente na Directional Light (junto do SunController). O resto é encontrado sozinho.
/// </summary>
public class WristClock : MonoBehaviour
{
    [Header("Referências (opcional, encontradas automaticamente)")]
    public SunController sun;
    public SkyController sky;
    [Tooltip("Controle (ou mão) esquerdo. Se vazio, procura 'Left Controller' / 'Left Hand' no XR Origin.")]
    public Transform leftWrist;
    public Transform head;

    [Header("Posição")]
    [Tooltip("Deslocamento a partir do controle esquerdo, em metros (no espaço do controle)")]
    public Vector3 offset = new Vector3(0f, 0.08f, -0.03f);
    [Tooltip("Largura do painel, em metros")]
    public float width = 0.14f;

    [Header("Quando aparece")]
    [Tooltip("Segundos visível depois de mudar a hora/data")]
    public float showAfterChange = 3f;
    [Tooltip("Mostrar ao olhar para o pulso")]
    public bool showWhenLooking = true;
    [Tooltip("Ângulo máximo entre o olhar e o relógio para contar como 'olhando'")]
    [Range(5f, 45f)] public float lookAngle = 20f;
    [Tooltip("Distância máxima do relógio até a cabeça para contar como 'olhando'")]
    public float lookDistance = 0.7f;
    public float fadeSpeed = 6f;

    [Header("Aparência")]
    public Color textColor = Color.white;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    private TextMeshPro label;
    private float lastChangeTime = float.NegativeInfinity;
    private float alpha;
    private readonly List<Transform> wristCandidates = new List<Transform>();

    void Start()
    {
        if (sun == null) sun = FindFirstObjectByType<SunController>();
        if (sky == null) sky = FindFirstObjectByType<SkyController>();
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (leftWrist == null) FindWristCandidates();

        if (sun == null)
        {
            Debug.LogWarning("[WristClock] Nenhum SunController na cena; relógio desativado.", this);
            enabled = false;
            return;
        }

        sun.TimeChanged += OnTimeChanged;
        CreateLabel();
        Refresh();
    }

    void OnDestroy()
    {
        if (sun != null) sun.TimeChanged -= OnTimeChanged;
        if (label != null) Destroy(label.gameObject);
    }

    private void OnTimeChanged(SunController _)
    {
        lastChangeTime = Time.time;
        Refresh();
    }

    void LateUpdate()
    {
        Transform wrist = GetWrist();
        if (label == null || wrist == null || head == null)
        {
            if (label != null) label.gameObject.SetActive(false);
            return;
        }

        // Posição acima do controle, sempre virado para o rosto
        Vector3 position = wrist.TransformPoint(offset);
        Vector3 toHead = head.position - position;
        label.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(-toHead.normalized, head.up));

        // Visibilidade: logo depois de mudar o tempo, ou quando o usuário olha para o pulso
        bool recentlyChanged = Time.time - lastChangeTime < showAfterChange;
        bool looking = showWhenLooking
                       && toHead.magnitude < lookDistance
                       && Vector3.Angle(head.forward, -toHead) < lookAngle;

        float target = (recentlyChanged || looking) ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);

        bool visible = alpha > 0.01f;
        if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
        if (visible)
        {
            if (looking) Refresh(); // mantém o período/hora atualizados se o tempo estiver animando
            label.alpha = alpha;
        }
    }

    private void Refresh()
    {
        if (label == null || sun == null) return;
        label.text = $"<b>{FormatTime(sun.timeOfDay)}</b>\n<size=50%>{sun.day:00}/{sun.month:00}  ·  {GetPeriodName()}</size>";
    }

    private static string FormatTime(float hour)
    {
        int minutes = Mathf.RoundToInt(hour * 60f) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private string GetPeriodName()
    {
        SkyController.Period period;
        if (sky != null)
        {
            period = sky.CurrentPeriod;
        }
        else
        {
            period = sun.Elevation < -6f
                ? (sun.timeOfDay < 12f ? SkyController.Period.Madrugada : SkyController.Period.Noite)
                : (sun.timeOfDay < 12f ? SkyController.Period.Manha : SkyController.Period.Tarde);
        }

        switch (period)
        {
            case SkyController.Period.Madrugada: return "Madrugada";
            case SkyController.Period.Manha: return "Manhã";
            case SkyController.Period.Tarde: return "Tarde";
            default: return "Noite";
        }
    }

    private void CreateLabel()
    {
        var go = new GameObject("Relógio de pulso");
        go.layer = 2; // Ignore Raycast: o laser do User.cs não bate no relógio

        label = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        label.rectTransform.sizeDelta = new Vector2(width, width * 0.5f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.01f;
        label.fontSizeMax = 2f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = textColor;
        label.richText = true;

        // Contorno escuro para ler a hora tanto contra o céu claro quanto contra a noite
        label.outlineWidth = 0.25f;
        label.outlineColor = outlineColor;

        go.SetActive(false);
    }

    private Transform GetWrist()
    {
        if (leftWrist != null) return leftWrist;

        // Controle ou mão, o que estiver ativo no momento (com hand tracking o controle fica desligado)
        foreach (Transform t in wristCandidates)
        {
            if (t != null && t.gameObject.activeInHierarchy) return t;
        }
        return null;
    }

    private void FindWristCandidates()
    {
        wristCandidates.Clear();
        XROrigin origin = FindFirstObjectByType<XROrigin>();
        if (origin == null) return;

        string[] preferredNames = { "Left Controller", "LeftHand Controller", "Left Hand Controller", "Left Hand" };
        Transform[] all = origin.GetComponentsInChildren<Transform>(true);

        foreach (string name in preferredNames)
        {
            foreach (Transform t in all)
            {
                if (t.name == name && !wristCandidates.Contains(t)) wristCandidates.Add(t);
            }
        }

        if (wristCandidates.Count == 0)
        {
            Debug.LogWarning("[WristClock] Não encontrei o controle esquerdo no XR Origin. Arraste-o no campo 'Left Wrist'.", this);
        }
    }
}
