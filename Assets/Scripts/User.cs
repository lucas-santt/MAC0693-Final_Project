using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(LineRenderer))]
public class User : MonoBehaviour
{
    private enum InteractionMode { None, ScaleWall, MoveObject }

    [Header("Referência do Controle Direito")]
    [Tooltip("Arraste aqui o GameObject do controle direito (ex: RightHand Controller)")]
    public Transform rightControllerTransform;

    [Tooltip("Associe a ação 'Activate' ou 'Activate Value' da mão direita")]
    public InputActionProperty rightTriggerAction;

    [Header("Configurações do Raycast e Escala (Eixo Z)")]
    public float maxRayDistance = 10f;
    public float scaleSensitivity = 2.0f;
    public float defaultMinZ = 0.1f;
    public float defaultMaxZ = 10f;

    [Header("Configurações de Movimentação (Moveable)")]
    [Tooltip("Distância máxima permitida ao arrastar o objeto")]
    public float maxMoveDistance = 15f;

    [Header("Rotação pelo Pulso (Moveable)")]
    [Tooltip("Gira o móvel em torno do eixo vertical ao girar o pulso enquanto ele está agarrado")]
    public bool enableWristRotation = true;

    [Tooltip("Multiplicador da rotação: 1 = mesmo ângulo do pulso, 2 = o dobro (facilita dar meia-volta)")]
    [Range(0.1f, 4f)]
    public float wristRotationSensitivity = 1.5f;

    [Tooltip("Ângulo do pulso (graus) ignorado antes de começar a girar, para evitar tremidas")]
    [Range(0f, 30f)]
    public float wristRotationDeadzone = 5f;

    [Tooltip("Arredonda a rotação para múltiplos deste ângulo (0 = rotação livre)")]
    [Range(0f, 90f)]
    public float wristRotationSnapAngle = 0f;

    [Tooltip("Inverte o sentido da rotação")]
    public bool invertWristRotation = false;

    [Header("Colisão ao Mover (Moveable)")]
    [Tooltip("Impede que o móvel atravesse paredes e outros móveis")]
    public bool preventOverlap = true;

    [Tooltip("Camadas consideradas obstáculos")]
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Folga (m) descontada da caixa do móvel; evita travar no chão ou ao encostar em algo")]
    [Range(0f, 0.1f)]
    public float collisionSkin = 0.02f;

    [Tooltip("Tenta deslizar ao longo do obstáculo quando o movimento direto é bloqueado")]
    public bool slideAlongObstacles = true;

    [Header("Limiares do Trigger")]
    [Tooltip("Valor mínimo para o laser aparecer ao começar a apertar")]
    [Range(0.01f, 0.5f)]
    public float triggerTouchThreshold = 0.05f;

    [Tooltip("Força necessária no trigger para agarrar o objeto")]
    [Range(0.1f, 1f)]
    public float triggerActivateThreshold = 0.5f;

    [Header("Visual do Laser (LineRenderer)")]
    public float laserWidth = 0.01f;
    public Color defaultLaserColor = Color.red;
    public Color hoverLaserColor = Color.yellow;
    public Color validTargetColor = Color.green;

    [Header("Feedback Visual do Objeto")]
    public Color objectHoverTint = Color.yellow;
    public Color objectHighlightTint = Color.green;
    [Range(0f, 1f)]
    public float tintIntensity = 0.25f;

    private const string WallTag = "Wall";
    private const string MoveableTag = "Moveable";

    private LineRenderer lineRenderer;

    // Estado atual da interação
    private InteractionMode currentMode = InteractionMode.None;
    private Transform targetObject;

    // Controle de Escala (Wall)
    private Vector3 initialControllerPos;
    private float initialWallScaleZ;
    private float currentMinZ;
    private float currentMaxZ;

    // Controle de Movimentação no Chão (Moveable)
    private Plane grabHeightPlane;
    private Vector3 grabOffsetXZ;
    private float lockedFloorY;
    private Vector3 lastValidHitPoint;

    // Pose do móvel: "base" é a posição do pivô sem rotação extra; a rotação acontece em torno do centro visual
    private Vector3 currentBasePosition;
    private float appliedYaw;
    private Vector3 pivotToCenterOffset;  // Vetor do pivô até o centro visual, na rotação inicial
    private Quaternion initialObjectRotation;

    // Rotação pelo pulso
    private Quaternion lastControllerRotation;
    private float accumulatedWristTwist;

    // Colisão: caixa orientada que envolve o móvel, no espaço "posição + rotação" do pivô (sem escala)
    private Vector3 collisionBoxCenter;
    private Vector3 collisionBoxHalfExtents;
    private readonly HashSet<Collider> ownColliders = new HashSet<Collider>();
    // Obstáculos que já invadiam o móvel quando ele foi agarrado -> profundidade inicial.
    // Eles só bloqueiam se o móvel tentar entrar MAIS neles (assim dá para tirar o móvel de dentro da parede).
    private readonly Dictionary<Collider, float> initialPenetration = new Dictionary<Collider, float>();
    private BoxCollider collisionProxy;
    private readonly Collider[] overlapBuffer = new Collider[64];

    // Controle de Objeto Apontado (Fase Fraca / Hover)
    private Transform hoveredObject;
    private readonly List<(Material material, Color color)> tintedMaterials = new List<(Material, Color)>();

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (rightControllerTransform == null)
        {
            rightControllerTransform = transform;
        }

        SetupLineRenderer();
    }

    void OnEnable()
    {
        if (rightTriggerAction.action != null)
            rightTriggerAction.action.Enable();
    }

    private void SetupLineRenderer()
    {
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;

        if (lineRenderer.sharedMaterial == null)
        {
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }
    }

    void Update()
    {
        float triggerValue = rightTriggerAction.action != null ? rightTriggerAction.action.ReadValue<float>() : 0f;

        bool isTriggerTouched = triggerValue > triggerTouchThreshold;
        bool isTriggerActivated = triggerValue >= triggerActivateThreshold;

        lineRenderer.enabled = isTriggerTouched;

        if (isTriggerTouched)
        {
            UpdateLaserAndHover();
        }
        else
        {
            ClearHover();
        }

        if (isTriggerActivated && targetObject == null)
        {
            TryGrabObject();
        }
        else if (isTriggerActivated && targetObject != null)
        {
            if (currentMode == InteractionMode.ScaleWall)
            {
                ScaleTargetWall();
            }
            else if (currentMode == InteractionMode.MoveObject)
            {
                MoveTargetObject();
            }
        }
        else if (!isTriggerActivated && targetObject != null)
        {
            ReleaseObject();
        }
    }

    // Retorna o objeto interativo atingido: a própria parede, ou o ancestral mais próximo com a tag Moveable
    // (assim, clicar em qualquer peça de um móvel seleciona o móvel inteiro)
    private static Transform GetInteractableFromHit(Collider hitCollider)
    {
        if (hitCollider.CompareTag(WallTag)) return hitCollider.transform;

        for (Transform t = hitCollider.transform; t != null; t = t.parent)
        {
            if (t.CompareTag(MoveableTag)) return t;
        }
        return null;
    }

    private void UpdateLaserAndHover()
    {
        Vector3 startPoint = rightControllerTransform.position;
        Vector3 direction = rightControllerTransform.forward;
        Vector3 endPoint = startPoint + (direction * maxRayDistance);
        Color currentColor = defaultLaserColor;

        Ray ray = new Ray(startPoint, direction);
        Transform hitValidObject = null;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            hitValidObject = GetInteractableFromHit(hit.collider);
            if (hitValidObject != null)
            {
                lastValidHitPoint = hit.point; // Guarda o ponto exato onde o laser tocou no objeto
            }
        }

        // CASO A: Já estamos agarrando um objeto
        if (targetObject != null)
        {
            currentColor = validTargetColor;

            // Faz a ponta do laser acompanhar exatamente o ponto travado no objeto enquanto move
            if (currentMode == InteractionMode.MoveObject && grabHeightPlane.Raycast(ray, out float enter) && enter > 0f)
            {
                float clampedEnter = Mathf.Min(enter, maxMoveDistance);
                endPoint = ray.GetPoint(clampedEnter);
            }
        }
        // CASO B: Apenas apontando (fase fraca)
        else
        {
            if (hitValidObject != null)
            {
                currentColor = hoverLaserColor;

                if (hoveredObject != hitValidObject)
                {
                    ClearHover();
                    SetHover(hitValidObject);
                }
            }
            else
            {
                ClearHover();
            }
        }

        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);
        lineRenderer.startColor = currentColor;
        lineRenderer.endColor = currentColor;
    }

    // ---------- Feedback visual (todas as malhas do objeto) ----------

    private void SetHover(Transform obj)
    {
        hoveredObject = obj;
        tintedMaterials.Clear();

        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.materials)
            {
                if (m != null && HasMainColor(m))
                {
                    tintedMaterials.Add((m, m.color));
                }
            }
        }

        ApplyTint(objectHoverTint);
    }

    private static bool HasMainColor(Material m) => m.HasColor("_BaseColor") || m.HasColor("_Color");

    private void ApplyTint(Color tint)
    {
        foreach (var (material, color) in tintedMaterials)
        {
            if (material != null) material.color = Color.Lerp(color, tint, tintIntensity);
        }
    }

    private void RestoreTint()
    {
        foreach (var (material, color) in tintedMaterials)
        {
            if (material != null) material.color = color;
        }
        tintedMaterials.Clear();
    }

    private void ClearHover()
    {
        if (hoveredObject != null && targetObject == null)
        {
            RestoreTint();
            hoveredObject = null;
        }
    }

    // ---------- Agarrar / soltar ----------

    private void TryGrabObject()
    {
        if (hoveredObject == null) return;

        targetObject = hoveredObject;

        if (targetObject.CompareTag(WallTag))
        {
            currentMode = InteractionMode.ScaleWall;
            initialControllerPos = rightControllerTransform.position;
            initialWallScaleZ = targetObject.localScale.z;
            FetchWallLimits(targetObject);
        }
        else
        {
            currentMode = InteractionMode.MoveObject;
            lockedFloorY = targetObject.position.y;

            // O plano horizontal é criado na ALTURA EXATA onde o raio tocou no objeto
            grabHeightPlane = new Plane(Vector3.up, new Vector3(0f, lastValidHitPoint.y, 0f));

            // Offset apenas em X e Z usando o ponto real do impacto
            grabOffsetXZ = new Vector3(
                targetObject.position.x - lastValidHitPoint.x,
                0f,
                targetObject.position.z - lastValidHitPoint.z
            );

            // Rotação pelo pulso
            initialObjectRotation = targetObject.rotation;
            lastControllerRotation = rightControllerTransform.rotation;
            accumulatedWristTwist = 0f;
            appliedYaw = 0f;

            // O pivô do modelo nem sempre fica no meio do móvel: usa o centro visual como eixo de rotação
            currentBasePosition = targetObject.position;
            Bounds worldBounds = GetWorldBounds(targetObject);
            pivotToCenterOffset = worldBounds.center - targetObject.position;
            pivotToCenterOffset.y = 0f; // o eixo é vertical; só importa o deslocamento horizontal

            SetupCollisionProxy(targetObject);
            WarnIfParentIsNonUniform(targetObject);
        }

        ApplyTint(objectHighlightTint);
    }

    private void FetchWallLimits(Transform wall)
    {
        if (wall.TryGetComponent<WallProperties>(out WallProperties props))
        {
            currentMinZ = props.minHeight;
            currentMaxZ = props.maxHeight;
        }
        else
        {
            currentMinZ = defaultMinZ;
            currentMaxZ = defaultMaxZ;
        }
    }

    private void ReleaseObject()
    {
        RestoreTint();

        targetObject = null;
        hoveredObject = null;
        currentMode = InteractionMode.None;
        ownColliders.Clear();
        initialPenetration.Clear();
    }

    void OnDestroy()
    {
        if (collisionProxy != null) Destroy(collisionProxy.gameObject);
    }

    // ---------- Escala de divisórias ----------

    private void ScaleTargetWall()
    {
        Vector3 controllerDelta = rightControllerTransform.position - initialControllerPos;
        float forwardMovement = Vector3.Dot(controllerDelta, rightControllerTransform.forward);

        float rawScaleZ = initialWallScaleZ - (forwardMovement * scaleSensitivity);
        float newScaleZ = Mathf.Clamp(rawScaleZ, currentMinZ, currentMaxZ);

        Vector3 currentScale = targetObject.localScale;
        targetObject.localScale = new Vector3(currentScale.x, currentScale.y, newScaleZ);
    }

    // ---------- Movimentação e rotação de móveis ----------

    private void MoveTargetObject()
    {
        Vector3 desiredBase = currentBasePosition;

        Ray ray = new Ray(rightControllerTransform.position, rightControllerTransform.forward);

        // Verifica se o raio aponta para baixo/frente e cruza o plano na altura do clique
        if (grabHeightPlane.Raycast(ray, out float enter) && enter > 0f)
        {
            float clampedDistance = Mathf.Min(enter, maxMoveDistance);
            Vector3 hitPointOnPlane = ray.GetPoint(clampedDistance);

            desiredBase = hitPointOnPlane + grabOffsetXZ;
            desiredBase.y = lockedFloorY; // Mantém o objeto colado no chão
        }

        // 1) Rotação: só aplica se o móvel girado não invadir nada
        float desiredYaw = enableWristRotation ? GetWristYaw() : 0f;
        if (!Mathf.Approximately(desiredYaw, appliedYaw))
        {
            GetPose(currentBasePosition, desiredYaw, out Vector3 p, out Quaternion r);
            if (IsPoseFree(p, r))
            {
                appliedYaw = desiredYaw;
            }
        }

        // 2) Translação: avança em passos para não "pular" através de paredes finas
        currentBasePosition = MoveWithCollisions(currentBasePosition, desiredBase, appliedYaw);

        GetPose(currentBasePosition, appliedYaw, out Vector3 finalPos, out Quaternion finalRot);
        targetObject.SetPositionAndRotation(finalPos, finalRot);
    }

    // Converte (posição base, yaw) na pose real do pivô, girando em torno do centro visual
    private void GetPose(Vector3 basePosition, float yaw, out Vector3 position, out Quaternion rotation)
    {
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, Vector3.up);
        Vector3 visualCenter = basePosition + pivotToCenterOffset;

        rotation = yawRotation * initialObjectRotation;
        position = visualCenter - (yawRotation * pivotToCenterOffset);
    }

    private Vector3 MoveWithCollisions(Vector3 from, Vector3 to, float yaw)
    {
        if (!preventOverlap) return to;

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return from;

        float stepSize = Mathf.Max(0.02f, Mathf.Min(collisionBoxHalfExtents.x, collisionBoxHalfExtents.z) * 0.5f);
        int steps = Mathf.Min(Mathf.CeilToInt(distance / stepSize), 200);
        Vector3 step = delta / steps;
        Vector3 position = from;

        for (int i = 0; i < steps; i++)
        {
            if (TryStep(position, position + step, yaw, out Vector3 next))
            {
                position = next;
                continue;
            }

            if (!slideAlongObstacles) break;

            // Desliza ao longo do obstáculo testando cada eixo separadamente
            bool moved = false;
            if (TryStep(position, position + new Vector3(step.x, 0f, 0f), yaw, out next)) { position = next; moved = true; }
            if (TryStep(position, position + new Vector3(0f, 0f, step.z), yaw, out next)) { position = next; moved = true; }
            if (!moved) break;
        }

        return position;
    }

    private bool TryStep(Vector3 from, Vector3 to, float yaw, out Vector3 result)
    {
        GetPose(to, yaw, out Vector3 p, out Quaternion r);
        if (IsPoseFree(p, r))
        {
            result = to;
            return true;
        }
        result = from;
        return false;
    }

    // Calcula uma caixa orientada que envolve todas as malhas do móvel, alinhada à rotação do pivô
    private void SetupCollisionProxy(Transform obj)
    {
        ownColliders.Clear();
        initialPenetration.Clear();

        foreach (Collider c in obj.GetComponentsInChildren<Collider>(true))
        {
            ownColliders.Add(c);
        }

        Quaternion invRotation = Quaternion.Inverse(obj.rotation);
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        bool hasAny = false;

        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            Bounds lb = GetLocalBounds(r);
            Matrix4x4 m = r.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));

                Vector3 local = invRotation * (m.MultiplyPoint3x4(corner) - obj.position);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
                hasAny = true;
            }
        }

        if (!hasAny)
        {
            min = Vector3.one * -0.05f;
            max = Vector3.one * 0.05f;
        }

        collisionBoxCenter = (min + max) * 0.5f;
        collisionBoxHalfExtents = (max - min) * 0.5f;

        EnsureCollisionProxy();
        collisionProxy.center = collisionBoxCenter;
        collisionProxy.size = GetShrunkHalfExtents() * 2f;

        // O que já invadia o móvel quando ele foi agarrado (ex.: móvel do modelo encostado demais na parede)
        // guarda a profundidade inicial, para permitir afastar o móvel mas não afundá-lo mais
        initialPenetration.Clear();
        foreach (Collider c in GetOverlappingColliders(obj.position, obj.rotation))
        {
            initialPenetration[c] = GetPenetrationDepth(c, obj.position, obj.rotation);
        }
    }

    // Collider auxiliar (trigger, invisível) usado só para medir a penetração com Physics.ComputePenetration
    private void EnsureCollisionProxy()
    {
        if (collisionProxy != null) return;

        GameObject go = new GameObject("_MoveCollisionProxy");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.layer = 2; // Ignore Raycast
        collisionProxy = go.AddComponent<BoxCollider>();
        collisionProxy.isTrigger = true;
    }

    private float GetPenetrationDepth(Collider other, Vector3 position, Quaternion rotation)
    {
        if (Physics.ComputePenetration(collisionProxy, position, rotation,
                                       other, other.transform.position, other.transform.rotation,
                                       out _, out float distance))
        {
            return distance;
        }
        return 0f;
    }

    private Vector3 GetShrunkHalfExtents()
    {
        Vector3 halfExtents = collisionBoxHalfExtents - Vector3.one * collisionSkin;
        return Vector3.Max(halfExtents, Vector3.one * 0.005f);
    }

    private static Bounds GetLocalBounds(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.localBounds;
        if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null) return mf.sharedMesh.bounds;
        return new Bounds(r.transform.InverseTransformPoint(r.bounds.center), Vector3.zero);
    }

    private bool IsPoseFree(Vector3 position, Quaternion rotation)
    {
        if (!preventOverlap) return true;

        foreach (Collider c in GetOverlappingColliders(position, rotation))
        {
            if (initialPenetration.TryGetValue(c, out float startDepth))
            {
                // Já começou invadindo este obstáculo: só bloqueia se for mais fundo do que no início
                if (GetPenetrationDepth(c, position, rotation) > startDepth + 0.005f) return false;
                continue;
            }
            return false;
        }
        return true;
    }

    private readonly List<Collider> overlapResults = new List<Collider>();

    private List<Collider> GetOverlappingColliders(Vector3 position, Quaternion rotation)
    {
        overlapResults.Clear();
        Vector3 center = position + rotation * collisionBoxCenter;

        int count = Physics.OverlapBoxNonAlloc(center, GetShrunkHalfExtents(), overlapBuffer, rotation,
                                               obstacleLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == null || c == collisionProxy || ownColliders.Contains(c)) continue;

            // Ignora o próprio jogador (XR Origin, mãos, controles)
            if (c.GetComponentInParent<XROrigin>() != null) continue;

            overlapResults.Add(c);
        }
        return overlapResults;
    }

    private static Bounds GetWorldBounds(Transform obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    // Girar um filho de um objeto com escala não uniforme distorce a malha (cisalhamento)
    private static void WarnIfParentIsNonUniform(Transform obj)
    {
        Transform parent = obj.parent;
        if (parent == null) return;

        Vector3 s = parent.lossyScale;
        float maxS = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        float minS = Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        if (maxS > 0f && (maxS - minS) / maxS > 0.01f)
        {
            Debug.LogWarning($"[User] '{obj.name}' é filho de '{parent.name}', que tem escala não uniforme ({s}). " +
                             "Ao girar, a malha vai distorcer. Rode 'Tools > MAC0693 > Normalizar Móveis' no editor.", obj);
        }
    }

    private float GetWristYaw()
    {
        Quaternion currentRotation = rightControllerTransform.rotation;

        // Rotação do controle desde o último frame, no espaço local do controle
        Quaternion frameDelta = Quaternion.Inverse(lastControllerRotation) * currentRotation;
        lastControllerRotation = currentRotation;

        // Isola só o "twist" em torno do eixo para onde o controle aponta (Z local),
        // ignorando quando o usuário apenas aponta para outro lugar
        accumulatedWristTwist += GetTwistAngleAroundZ(frameDelta);

        // Zona morta: só começa a girar depois de passar do limite
        float magnitude = Mathf.Max(0f, Mathf.Abs(accumulatedWristTwist) - wristRotationDeadzone);
        float effectiveTwist = Mathf.Sign(accumulatedWristTwist) * magnitude;

        // Girar o pulso no sentido horário (visto pelo usuário) gira o móvel no sentido horário visto de cima
        float yaw = -effectiveTwist * wristRotationSensitivity;
        if (invertWristRotation) yaw = -yaw;

        if (wristRotationSnapAngle > 0f)
        {
            yaw = Mathf.Round(yaw / wristRotationSnapAngle) * wristRotationSnapAngle;
        }

        return yaw;
    }

    // Decomposição swing-twist: retorna o ângulo (graus, -180..180) da rotação em torno do eixo Z local
    private static float GetTwistAngleAroundZ(Quaternion q)
    {
        float z = q.z;
        float w = q.w;
        if (w < 0f) { z = -z; w = -w; }
        if (Mathf.Approximately(z, 0f) && Mathf.Approximately(w, 0f)) return 0f;
        return 2f * Mathf.Atan2(z, w) * Mathf.Rad2Deg;
    }
}
