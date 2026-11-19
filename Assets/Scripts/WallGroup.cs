using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Agrupa as paredes (e colunas) da casa para que funcionem como uma só:
/// ao mudar a altura de uma, todas mudam juntas. Também cria/posiciona um teto
/// que acompanha automaticamente o topo das paredes.
/// Coloque este componente no objeto raiz da casa (ex: apartment_3d_floor_plan).
/// </summary>
public class WallGroup : MonoBehaviour
{
    [Header("Paredes do Grupo")]
    [Tooltip("Objetos cujos filhos com a tag 'Wall' fazem parte do grupo (ex: Walls e Columns). " +
             "Se ficar vazio, usa todos os filhos com tag 'Wall' deste objeto.")]
    public Transform[] wallContainers;

    [Tooltip("Ao iniciar, iguala a altura de todas as paredes à da primeira parede do grupo")]
    public bool unifyHeightsOnStart = true;

    [Header("Teto")]
    public bool createCeiling = true;

    [Tooltip("Opcional: um teto pronto (ex: malha do ProBuilder com o formato da casa). " +
             "Se vazio, um teto retangular é gerado cobrindo todas as paredes.")]
    public Transform customCeiling;

    [Tooltip("Material do teto gerado (opcional)")]
    public Material ceilingMaterial;

    [Tooltip("Espessura do teto gerado, em metros")]
    public float ceilingThickness = 0.05f;

    [Tooltip("Quanto o teto gerado avança além das paredes, em metros")]
    public float ceilingPadding = 0.0f;

    [Tooltip("Se ligado, o teto não tem collider (o laser e o teleporte passam por ele)")]
    public bool ceilingWithoutCollider = true;

    private readonly List<Transform> walls = new List<Transform>();
    private readonly List<Renderer> wallRenderers = new List<Renderer>();
    private Transform ceiling;
    private float customCeilingOffsetY;

    public IReadOnlyList<Transform> Walls => walls;

    void Awake()
    {
        CollectWalls();
    }

    void Start()
    {
        if (unifyHeightsOnStart && walls.Count > 0)
        {
            SetWallHeight(walls[0].localScale.z);
        }

        SetupCeiling();
        UpdateCeiling();
    }

    private void CollectWalls()
    {
        walls.Clear();
        wallRenderers.Clear();

        Transform[] roots = (wallContainers != null && wallContainers.Length > 0)
            ? wallContainers
            : new[] { transform };

        foreach (Transform root in roots)
        {
            if (root == null) continue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.CompareTag("Wall") || walls.Contains(t)) continue;
                walls.Add(t);
                if (t.TryGetComponent(out Renderer r)) wallRenderers.Add(r);
            }
        }

        if (walls.Count == 0)
        {
            Debug.LogWarning($"[WallGroup] Nenhuma parede com tag 'Wall' encontrada em '{name}'.", this);
        }
    }

    public bool Contains(Transform wall) => walls.Contains(wall);

    /// <summary>
    /// Define a altura (escala Z local, mesmo eixo usado pelo User.cs) de todas as paredes do grupo.
    /// </summary>
    public void SetWallHeight(float scaleZ)
    {
        foreach (Transform wall in walls)
        {
            Vector3 s = wall.localScale;
            wall.localScale = new Vector3(s.x, s.y, scaleZ);
        }

        UpdateCeiling();
    }

    private void SetupCeiling()
    {
        if (!createCeiling) return;

        if (customCeiling != null)
        {
            ceiling = customCeiling;
            if (TryGetWallsBounds(out Bounds b))
            {
                customCeilingOffsetY = ceiling.position.y - b.max.y;
            }
            return;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Teto (gerado)";
        go.transform.SetParent(transform, true);

        if (ceilingWithoutCollider && go.TryGetComponent(out Collider col))
        {
            Destroy(col);
        }

        if (ceilingMaterial != null && go.TryGetComponent(out Renderer rend))
        {
            rend.sharedMaterial = ceilingMaterial;
        }

        ceiling = go.transform;
    }

    private void UpdateCeiling()
    {
        if (ceiling == null || !TryGetWallsBounds(out Bounds b)) return;

        if (ceiling == customCeiling)
        {
            Vector3 p = ceiling.position;
            ceiling.position = new Vector3(p.x, b.max.y + customCeilingOffsetY, p.z);
            return;
        }

        // Teto gerado: cobre toda a área das paredes e fica apoiado no topo delas
        ceiling.rotation = Quaternion.identity;
        ceiling.position = new Vector3(b.center.x, b.max.y + ceilingThickness * 0.5f, b.center.z);

        Vector3 worldSize = new Vector3(
            b.size.x + ceilingPadding * 2f,
            ceilingThickness,
            b.size.z + ceilingPadding * 2f);

        Vector3 parentScale = ceiling.parent != null ? ceiling.parent.lossyScale : Vector3.one;
        ceiling.localScale = new Vector3(
            worldSize.x / parentScale.x,
            worldSize.y / parentScale.y,
            worldSize.z / parentScale.z);
    }

    private bool TryGetWallsBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasAny = false;

        foreach (Renderer r in wallRenderers)
        {
            if (r == null) continue;
            if (!hasAny) { bounds = r.bounds; hasAny = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return hasAny;
    }
}
