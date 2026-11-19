using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ferramentas de editor para preparar a casa para o estudo de insolação:
///  - Criar Teto: coloca uma laje cobrindo toda a área das paredes, apoiada no topo delas.
///  - Paredes com Sombra nos Dois Lados: evita que o sol "vaze" por paredes de uma face só.
/// </summary>
public static class CeilingTool
{
    private const string WallTag = "Wall";
    private const string CeilingName = "Teto";

    [MenuItem("Tools/MAC0693/Criar Teto")]
    public static void CreateCeiling()
    {
        List<Renderer> walls = FindWallRenderers(out Transform houseRoot);
        if (walls.Count == 0)
        {
            EditorUtility.DisplayDialog("Criar Teto", "Nenhum objeto com a tag 'Wall' encontrado na cena.", "OK");
            return;
        }

        // Área: tudo que forma a casa (paredes e colunas)
        Bounds area = walls[0].bounds;
        foreach (Renderer r in walls) area.Encapsulate(r.bounds);

        // Altura: o topo mais baixo entre as paredes principais, para não sobrar fresta por onde a luz entra
        List<Renderer> mainWalls = walls.Where(IsMainWall).ToList();
        if (mainWalls.Count == 0) mainWalls = walls;
        float height = mainWalls.Min(r => r.bounds.max.y);
        float thickness = 0.1f;

        if (!EditorUtility.DisplayDialog("Criar Teto",
                $"Criar uma laje de {area.size.x:0.00} m × {area.size.z:0.00} m a {height:0.00} m de altura, " +
                $"apoiada no topo de {mainWalls.Count} parede(s)?\n\nSe já existir um objeto '{CeilingName}' na casa, ele será substituído.",
                "Criar", "Cancelar"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Criar Teto");

        Transform existing = houseRoot != null ? houseRoot.Find(CeilingName) : null;
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(ceiling, "Criar Teto");
        ceiling.name = CeilingName;

        // Sem collider: o laser, o teleporte e a colisão dos móveis não interagem com o teto
        Object.DestroyImmediate(ceiling.GetComponent<Collider>());

        if (houseRoot != null) ceiling.transform.SetParent(houseRoot, true);
        ceiling.transform.rotation = Quaternion.identity;
        ceiling.transform.position = new Vector3(area.center.x, height + thickness * 0.5f, area.center.z);
        SetWorldScale(ceiling.transform, new Vector3(area.size.x, thickness, area.size.z));

        MeshRenderer mr = ceiling.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.TwoSided;
        mr.receiveShadows = true;

        // Paredes principais e colunas passam a ir sempre do chão até o teto
        Undo.AddComponent<CeilingWalls>(ceiling);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(ceiling.scene);
        Selection.activeGameObject = ceiling;
        EditorGUIUtility.PingObject(ceiling);

        Debug.Log($"[CeilingTool] Teto criado a {height:0.00} m. Para trocar o acabamento, arraste um material para o objeto '{CeilingName}'.", ceiling);
    }

    [MenuItem("Tools/MAC0693/Paredes com Sombra nos Dois Lados")]
    public static void MakeWallShadowsTwoSided()
    {
        List<Renderer> walls = FindWallRenderers(out _);
        if (walls.Count == 0)
        {
            EditorUtility.DisplayDialog("Paredes", "Nenhum objeto com a tag 'Wall' encontrado na cena.", "OK");
            return;
        }

        Undo.RecordObjects(walls.ToArray(), "Sombra nos dois lados");
        foreach (Renderer r in walls) r.shadowCastingMode = ShadowCastingMode.TwoSided;

        EditorSceneManager.MarkSceneDirty(walls[0].gameObject.scene);
        Debug.Log($"[CeilingTool] {walls.Count} parede(s) agora projetam sombra dos dois lados.");
    }

    [MenuItem("Tools/MAC0693/Igualar Altura de Paredes e Pilares")]
    public static void EqualizeWallHeights()
    {
        List<Renderer> walls = FindWallRenderers(out Transform houseRoot);
        if (walls.Count == 0)
        {
            EditorUtility.DisplayDialog("Igualar Altura", "Nenhum objeto com a tag 'Wall' encontrado na cena.", "OK");
            return;
        }

        // Altura alvo: a mediana dos topos (ignora uma ou outra parede que foi esticada sem querer)
        List<float> tops = walls.Select(r => r.bounds.max.y).OrderBy(y => y).ToList();
        float target = tops[tops.Count / 2];
        float lowest = tops[0], highest = tops[tops.Count - 1];

        if (!EditorUtility.DisplayDialog("Igualar Altura",
                $"{walls.Count} paredes/pilares têm topo entre {lowest:0.00} m e {highest:0.00} m.\n\n" +
                $"Todos ficarão com o topo a {target:0.00} m (altura mais comum).",
                "Igualar", "Cancelar"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Igualar Altura de Paredes");

        // Se existe um teto controlando as paredes, basta mover o teto: as paredes seguem
        Transform ceilingT = houseRoot != null ? houseRoot.Find(CeilingName) : null;
        CeilingWalls ceiling = ceilingT != null ? ceilingT.GetComponent<CeilingWalls>() : null;
        if (ceiling != null && ceiling.isActiveAndEnabled)
        {
            Undo.RecordObject(ceilingT, "Mover teto");
            ceilingT.position += Vector3.up * (target - ceiling.CeilingBottom);
        }

        int changed = 0;
        foreach (Renderer r in walls)
        {
            Transform t = r.transform;
            if (!TryMeasureHeightPerScale(t, r, out float offset, out float heightPerScale))
            {
                Debug.LogWarning($"[CeilingTool] Não foi possível ajustar '{t.name}': a escala Z não muda a altura.", t);
                continue;
            }

            float scaleZ = (target - offset) / heightPerScale;
            if (scaleZ <= 0f || Mathf.Approximately(scaleZ, t.localScale.z)) continue;

            Undo.RecordObject(t, "Igualar altura");
            t.localScale = new Vector3(t.localScale.x, t.localScale.y, scaleZ);
            changed++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(walls[0].gameObject.scene);
        Debug.Log($"[CeilingTool] {changed} parede(s)/pilar(es) ajustados para o topo a {target:0.00} m.");
    }

    // Mede como o topo varia com a escala Z: topo = offset + escalaZ * alturaPorEscala
    private static bool TryMeasureHeightPerScale(Transform t, Renderer r, out float offset, out float heightPerScale)
    {
        Vector3 original = t.localScale;
        float s0 = original.z;
        float s1 = Mathf.Approximately(s0, 0f) ? 1f : s0 * 1.25f;

        float top0 = r.bounds.max.y;
        t.localScale = new Vector3(original.x, original.y, s1);
        float top1 = r.bounds.max.y;
        t.localScale = original;

        heightPerScale = (top1 - top0) / (s1 - s0);
        offset = top0 - heightPerScale * s0;
        return heightPerScale > 1e-6f;
    }

    // Paredes e colunas da casa. Se algo estiver selecionado, usa só o que está dentro da seleção.
    private static List<Renderer> FindWallRenderers(out Transform houseRoot)
    {
        IEnumerable<GameObject> candidates = GameObject.FindGameObjectsWithTag(WallTag);

        Transform selected = Selection.activeTransform;
        if (selected != null)
        {
            candidates = candidates.Where(go => go.transform.IsChildOf(selected));
        }

        var renderers = new List<Renderer>();
        foreach (GameObject go in candidates)
        {
            // Ignora o que está dentro da pasta de móveis (peças decorativas com a tag Wall)
            if (IsInsideFurniture(go.transform)) continue;
            if (go.TryGetComponent(out Renderer r)) renderers.Add(r);
        }

        houseRoot = selected != null ? selected : FindCommonRoot(renderers);
        return renderers;
    }

    private static bool IsMainWall(Renderer r)
    {
        for (Transform t = r.transform; t != null; t = t.parent)
        {
            if (t.name == "Walls") return true;
        }
        return false;
    }

    private static bool IsInsideFurniture(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            if (p.name.StartsWith("Móveis") || p.name.StartsWith("Moveis")) return true;
        }
        return false;
    }

    private static Transform FindCommonRoot(List<Renderer> renderers)
    {
        if (renderers.Count == 0) return null;
        Transform root = renderers[0].transform.root;
        return renderers.All(r => r.transform.root == root) ? root : null;
    }

    private static void SetWorldScale(Transform t, Vector3 worldScale)
    {
        Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(
            worldScale.x / parentScale.x,
            worldScale.y / parentScale.y,
            worldScale.z / parentScale.z);
    }
}
