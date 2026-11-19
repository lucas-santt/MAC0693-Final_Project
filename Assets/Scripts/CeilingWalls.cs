using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Faz as paredes principais (e colunas) sempre irem do chão até o teto.
/// Coloque este componente no objeto "Teto".
///
/// - Mover o teto para cima/baixo (no editor ou em execução) estica/encolhe todas as paredes.
/// - Esticar uma parede com o laser (User.cs) move o teto, e as outras paredes acompanham.
///
/// As paredes são esticadas pela escala Z local, a mesma usada pelo User.cs.
/// </summary>
[ExecuteAlways]
public class CeilingWalls : MonoBehaviour
{
    [Tooltip("Objetos cujos filhos com a tag 'Wall' seguem o teto. Se vazio, usa 'Walls' e 'Columns' irmãos do teto.")]
    public Transform[] wallContainers;

    private const string WallTag = "Wall";

    // Altura do topo de cada parede em função da escala: topo = offset + escalaZ * alturaPorEscala
    private struct WallEntry
    {
        public Transform wall;
        public Renderer renderer;
        public float offset;
        public float heightPerScale;
    }

    private static readonly List<CeilingWalls> instances = new List<CeilingWalls>();

    private readonly List<WallEntry> walls = new List<WallEntry>();
    private Renderer ceilingRenderer;
    private float lastCeilingBottom = float.NaN;

    /// <summary>Retorna o teto que controla esta parede, se houver.</summary>
    public static CeilingWalls FindFor(Transform wall)
    {
        foreach (CeilingWalls c in instances)
        {
            if (c.IndexOf(wall) >= 0) return c;
        }
        return null;
    }

    void OnEnable()
    {
        ceilingRenderer = GetComponent<Renderer>();
        CollectWalls();
        if (!instances.Contains(this)) instances.Add(this);
        lastCeilingBottom = float.NaN;
        ApplyIfMoved();
    }

    void OnDisable()
    {
        instances.Remove(this);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled)
        {
            CollectWalls();
            lastCeilingBottom = float.NaN;
        }
    }

    void Update()
    {
        ApplyIfMoved();
    }

    /// <summary>Recarrega a lista de paredes (ex.: depois de criar um fechamento de vão).</summary>
    public void Rebuild()
    {
        CollectWalls();
        lastCeilingBottom = float.NaN;
        ApplyIfMoved();
    }

    /// <summary>Altura (y) da face de baixo do teto.</summary>
    public float CeilingBottom => ceilingRenderer != null ? ceilingRenderer.bounds.min.y : transform.position.y;

    /// <summary>
    /// Chamado ao esticar uma parede no VR: move o teto para o topo que essa parede teria com a nova escala.
    /// </summary>
    public void SetHeightFromWall(Transform wall, float newScaleZ)
    {
        int i = IndexOf(wall);
        if (i < 0) return;

        float newTop = walls[i].offset + newScaleZ * walls[i].heightPerScale;
        transform.position += Vector3.up * (newTop - CeilingBottom);
        ApplyIfMoved();
    }

    private int IndexOf(Transform wall)
    {
        for (int i = 0; i < walls.Count; i++)
        {
            if (walls[i].wall == wall) return i;
        }
        return -1;
    }

    private void ApplyIfMoved()
    {
        float bottom = CeilingBottom;
        if (Mathf.Approximately(bottom, lastCeilingBottom)) return;
        lastCeilingBottom = bottom;

        foreach (WallEntry e in walls)
        {
            if (e.wall == null) continue;

            float scaleZ = (bottom - e.offset) / e.heightPerScale;
            if (scaleZ <= 0f) continue;

            Vector3 s = e.wall.localScale;
            if (Mathf.Approximately(s.z, scaleZ)) continue;
            e.wall.localScale = new Vector3(s.x, s.y, scaleZ);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(e.wall);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(e.wall.gameObject.scene);
            }
#endif
        }
    }

    private void CollectWalls()
    {
        walls.Clear();

        foreach (Transform container in GetContainers())
        {
            foreach (Transform t in container.GetComponentsInChildren<Transform>(true))
            {
                if (!t.CompareTag(WallTag)) continue;
                Renderer r = t.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                if (IndexOf(t) >= 0) continue;

                if (TryMeasure(t, r, out float offset, out float heightPerScale))
                {
                    walls.Add(new WallEntry { wall = t, renderer = r, offset = offset, heightPerScale = heightPerScale });
                }
                else
                {
                    Debug.LogWarning($"[CeilingWalls] Esticar a escala Z de '{t.name}' não aumenta a altura; parede ignorada.", t);
                }
            }
        }
    }

    private IEnumerable<Transform> GetContainers()
    {
        if (wallContainers != null && wallContainers.Length > 0)
        {
            foreach (Transform c in wallContainers)
            {
                if (c != null) yield return c;
            }
            yield break;
        }

        Transform root = transform.parent;
        if (root == null) yield break;

        foreach (Transform child in root)
        {
            if (child.name == "Walls" || child.name == "Columns") yield return child;
        }
    }

    // Mede como o topo da parede varia com a escala Z (testando duas escalas e voltando à original)
    private static bool TryMeasure(Transform wall, Renderer r, out float offset, out float heightPerScale)
    {
        Vector3 original = wall.localScale;
        float s0 = original.z;
        float s1 = Mathf.Approximately(s0, 0f) ? 1f : s0 * 1.25f;

        float top0 = r.bounds.max.y;
        wall.localScale = new Vector3(original.x, original.y, s1);
        float top1 = r.bounds.max.y;
        wall.localScale = original;

        heightPerScale = (top1 - top0) / (s1 - s0);
        offset = top0 - heightPerScale * s0;
        return heightPerScale > 1e-6f;
    }
}
