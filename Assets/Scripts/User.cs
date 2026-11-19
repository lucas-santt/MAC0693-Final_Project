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

    // Controle de Rotação pelo Pulso (Moveable)
    private Quaternion initialObjectRotation;
    private Quaternion lastControllerRotation;
    private float accumulatedWristTwist;

    // Controle de Objeto Apontado (Fase Fraca / Hover)
    private Transform hoveredObject;
    private Renderer currentObjectRenderer;
    private Color originalObjectColor;

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

    private void UpdateLaserAndHover()
    {
        Vector3 startPoint = rightControllerTransform.position;
        Vector3 direction = rightControllerTransform.forward;
        Vector3 endPoint = startPoint + (direction * maxRayDistance);
        Color currentColor = defaultLaserColor;

        Ray ray = new Ray(startPoint, direction);
        Transform hitValidObject = null;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            endPoint = hit.point;
            if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("Moveable"))
            {
                hitValidObject = hit.collider.transform;
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

    private void SetHover(Transform obj)
    {
        hoveredObject = obj;
        currentObjectRenderer = hoveredObject.GetComponent<Renderer>();
        if (currentObjectRenderer != null)
        {
            originalObjectColor = currentObjectRenderer.material.color;
            currentObjectRenderer.material.color = Color.Lerp(originalObjectColor, objectHoverTint, tintIntensity);
        }
    }

    private void ClearHover()
    {
        if (hoveredObject != null && targetObject == null)
        {
            if (currentObjectRenderer != null)
            {
                currentObjectRenderer.material.color = originalObjectColor;
                currentObjectRenderer = null;
            }
            hoveredObject = null;
        }
    }

    private void TryGrabObject()
    {
        if (hoveredObject == null) return;

        targetObject = hoveredObject;

        if (targetObject.CompareTag("Wall"))
        {
            currentMode = InteractionMode.ScaleWall;
            initialControllerPos = rightControllerTransform.position;
            initialWallScaleZ = targetObject.localScale.z;
            FetchWallLimits(targetObject);
        }
        else if (targetObject.CompareTag("Moveable"))
        {
            currentMode = InteractionMode.MoveObject;
            lockedFloorY = targetObject.position.y;

            // O plano horizontal agora é criado na ALTURA EXATA onde o raio tocou no objeto (lastValidHitPoint.y)
            grabHeightPlane = new Plane(Vector3.up, new Vector3(0f, lastValidHitPoint.y, 0f));

            // Calcula o offset apenas em X e Z usando o ponto real do impacto
            grabOffsetXZ = new Vector3(
                targetObject.position.x - lastValidHitPoint.x,
                0f,
                targetObject.position.z - lastValidHitPoint.z
            );

            // Guarda o estado inicial para a rotação pelo pulso
            initialObjectRotation = targetObject.rotation;
            lastControllerRotation = rightControllerTransform.rotation;
            accumulatedWristTwist = 0f;
        }

        if (currentObjectRenderer != null)
        {
            currentObjectRenderer.material.color = Color.Lerp(originalObjectColor, objectHighlightTint, tintIntensity);
        }
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
        if (currentObjectRenderer != null)
        {
            currentObjectRenderer.material.color = originalObjectColor;
            currentObjectRenderer = null;
        }

        targetObject = null;
        hoveredObject = null;
        currentMode = InteractionMode.None;
    }

    private void ScaleTargetWall()
    {
        Vector3 controllerDelta = rightControllerTransform.position - initialControllerPos;
        float forwardMovement = Vector3.Dot(controllerDelta, rightControllerTransform.forward);

        float rawScaleZ = initialWallScaleZ - (forwardMovement * scaleSensitivity);
        float newScaleZ = Mathf.Clamp(rawScaleZ, currentMinZ, currentMaxZ);

        Vector3 currentScale = targetObject.localScale;
        targetObject.localScale = new Vector3(currentScale.x, currentScale.y, newScaleZ);
    }

    private void MoveTargetObject()
    {
        Ray ray = new Ray(rightControllerTransform.position, rightControllerTransform.forward);

        // Verifica se o raio aponta para baixo/frente e cruza o plano na altura do clique
        if (grabHeightPlane.Raycast(ray, out float enter) && enter > 0f)
        {
            float clampedDistance = Mathf.Min(enter, maxMoveDistance);
            Vector3 hitPointOnPlane = ray.GetPoint(clampedDistance);

            Vector3 newPosition = hitPointOnPlane + grabOffsetXZ;
            newPosition.y = lockedFloorY; // Mantém o objeto colado no chão

            targetObject.position = newPosition;
        }

        if (enableWristRotation)
        {
            RotateTargetObjectByWrist();
        }
    }

    private void RotateTargetObjectByWrist()
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

        // Gira em torno do eixo vertical do mundo, preservando a inclinação original do objeto
        targetObject.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * initialObjectRotation;
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