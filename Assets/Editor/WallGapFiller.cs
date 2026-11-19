using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fecha vãos de parede (janelas sem vidro, peitoris "pela metade").
/// Uso: Tools > MAC0693 > Fechar Vão de Parede, depois clique no topo do peitoril na janela Scene.
/// A ferramenta mede a largura do vão (até as laterais) e a espessura da parede, e cria um
/// fechamento do peitoril até o teto, com o mesmo material da parede. O fechamento segue o teto (CeilingWalls).
/// Esc cancela.
/// </summary>
public static class WallGapFiller
{
    private const string WallTag = "Wall";
    private const float ProbeAboveSill = 0.05f;   // altura acima do peitoril onde as laterais são medidas
    private const float MaxOpeningWidth = 6f;
    private const float Overlap = 0.01f;          // avança 1 cm nas laterais para não sobrar fresta

    private static bool active;

    [MenuItem("Tools/MAC0693/Fechar Vão de Parede")]
    public static void Begin()
    {
        if (active) return;
        active = true;
        SceneView.duringSceneGui += OnSceneGUI;
        SceneView.lastActiveSceneView?.Focus();
        SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("Clique no topo do peitoril do vão (Esc cancela)"), 4f);
    }

    private static void End()
    {
        active = false;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView view)
    {
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            End();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            e.Use();
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            End();
            TryFill(ray);
        }
    }

    private static void TryFill(Ray clickRay)
    {
        // Colliders temporários em tudo que tem malha (ex.: caixilhos de porta sem collider),
        // para que as laterais do vão sejam detectadas corretamente
        var tempColliders = new List<Collider>();
        foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (mr.GetComponent<Collider>() != null) continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mc = mr.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.hideFlags = HideFlags.HideAndDontSave;
            tempColliders.Add(mc);
        }
        Physics.SyncTransforms();

        try
        {
            if (!Physics.Raycast(clickRay, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                Fail("O clique não acertou nenhuma superfície.");
                return;
            }
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.7f)
            {
                Fail("Clique no TOPO do peitoril (a superfície horizontal da parede baixa).");
                return;
            }

            Vector3 sillPoint = hit.point;
            Vector3 probe = sillPoint + Vector3.up * ProbeAboveSill;

            // 1) Largura: procura o eixo horizontal com as duas laterais mais próximas
            if (!FindOpeningAxis(probe, out Vector3 axis, out float distA, out float distB))
            {
                Fail("Não encontrei as duas laterais do vão. Verifique se o vão tem paredes dos dois lados.");
                return;
            }
            Vector3 normal = Vector3.Cross(Vector3.up, axis).normalized;

            // 2) Espessura: anda pelo topo do peitoril até cair da borda, para frente e para trás
            float front = MeasureSillEdge(sillPoint, normal);
            float back = MeasureSillEdge(sillPoint, -normal);
            float thickness = front + back;
            if (thickness < 0.02f)
            {
                Fail("Não consegui medir a espessura do peitoril.");
                return;
            }

            // 3) Altura: até o teto (ou até o topo das paredes, se não houver teto)
            float top = GetCeilingHeight(out CeilingWalls ceiling);
            float height = top - sillPoint.y;
            if (height < 0.05f)
            {
                Fail("O peitoril já está na altura do teto.");
                return;
            }

            float width = distA + distB + Overlap * 2f;
            Vector3 center = sillPoint + axis * ((distA - distB) * 0.5f) + normal * ((front - back) * 0.5f);
            center.y = sillPoint.y;

            if (!EditorUtility.DisplayDialog("Fechar Vão de Parede",
                    $"Criar fechamento de {width:0.00} m de largura × {height:0.00} m de altura × {thickness:0.00} m de espessura?",
                    "Criar", "Cancelar"))
            {
                return;
            }

            Material wallMaterial = hit.collider.TryGetComponent(out Renderer wallRenderer) ? wallRenderer.sharedMaterial : null;
            GameObject filler = CreateFiller(center, axis, normal, width, thickness, height, wallMaterial, hit.collider.transform);

            if (ceiling != null) ceiling.Rebuild();

            Selection.activeGameObject = filler;
            EditorSceneManager.MarkSceneDirty(filler.scene);
            Debug.Log($"[WallGapFiller] Vão fechado: {width:0.00} × {height:0.00} m. Ajuste com as ferramentas de mover/escalar se precisar.", filler);
        }
        finally
        {
            foreach (Collider c in tempColliders)
            {
                if (c != null) Object.DestroyImmediate(c);
            }
        }
    }

    private static bool FindOpeningAxis(Vector3 probe, out Vector3 bestAxis, out float bestA, out float bestB)
    {
        bestAxis = Vector3.zero;
        bestA = bestB = 0f;
        float bestSum = float.MaxValue;

        for (int deg = 0; deg < 180; deg += 3)
        {
            Vector3 dir = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward;
            if (!Physics.Raycast(probe, dir, out RaycastHit a, MaxOpeningWidth, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Physics.Raycast(probe, -dir, out RaycastHit b, MaxOpeningWidth, ~0, QueryTriggerInteraction.Ignore)) continue;

            float sum = a.distance + b.distance;
            if (sum < bestSum)
            {
                bestSum = sum;
                bestAxis = dir;
                bestA = a.distance;
                bestB = b.distance;
            }
        }

        return bestSum < MaxOpeningWidth;
    }

    private static float MeasureSillEdge(Vector3 sillPoint, Vector3 dir)
    {
        const float step = 0.01f;
        Vector3 start = sillPoint + Vector3.up * ProbeAboveSill;

        for (float d = step; d < 1f; d += step)
        {
            Vector3 origin = start + dir * d;
            bool onSill = Physics.Raycast(origin, Vector3.down, out RaycastHit h, ProbeAboveSill + 0.03f, ~0, QueryTriggerInteraction.Ignore)
                          && Mathf.Abs(h.point.y - sillPoint.y) < 0.02f;
            if (!onSill) return d - step;
        }
        return 0f;
    }

    private static float GetCeilingHeight(out CeilingWalls ceiling)
    {
        ceiling = Object.FindFirstObjectByType<CeilingWalls>();
        if (ceiling != null) return ceiling.CeilingBottom;

        float top = float.MinValue;
        foreach (GameObject go in GameObject.FindGameObjectsWithTag(WallTag))
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null) top = Mathf.Max(top, r.bounds.max.y);
        }
        return top;
    }

    // Estrutura: "Fechamento de vão" (tag Wall, pivô na base, eixo Z local = vertical)
    //              └ Cube (malha, collider), deslocado para a base ficar no pivô
    // Assim, esticar a escala Z do pai aumenta a altura a partir do peitoril, como nas outras paredes.
    private static GameObject CreateFiller(Vector3 baseCenter, Vector3 axis, Vector3 normal,
                                           float width, float thickness, float height,
                                           Material material, Transform hitWall)
    {
        Transform container = FindWallsContainer(hitWall);

        var root = new GameObject("Fechamento de vão");
        Undo.RegisterCreatedObjectUndo(root, "Fechar vão");
        root.tag = WallTag;
        root.transform.SetParent(container, true);
        root.transform.SetPositionAndRotation(baseCenter, Quaternion.LookRotation(Vector3.up, normal));
        // Pivô na base; a escala Z (vertical) é a altura em metros (o container Walls tem escala 1)
        root.transform.localScale = new Vector3(1f, 1f, height);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Parede";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        cube.transform.localRotation = Quaternion.identity;
        // Local: X = ao longo do vão, Y = espessura, Z = altura (vem da escala do pai)
        cube.transform.localScale = new Vector3(width, thickness, 1f);

        MeshRenderer mr = cube.GetComponent<MeshRenderer>();
        if (material != null) mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

        return root;
    }

    private static Transform FindWallsContainer(Transform hitWall)
    {
        for (Transform t = hitWall; t != null; t = t.parent)
        {
            if (t.name == "Walls") return t;
        }
        Transform walls = hitWall.root.Find("Walls");
        return walls != null ? walls : null; // sem container: fica na raiz da cena (escala 1)
    }

    private static void Fail(string message)
    {
        EditorUtility.DisplayDialog("Fechar Vão de Parede", message, "OK");
    }
}
