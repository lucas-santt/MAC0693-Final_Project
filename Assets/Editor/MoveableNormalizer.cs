using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ferramenta de editor: "assa" cada móvel (tag Moveable) em um novo objeto limpo.
///
/// Problemas que resolve:
///  - Pivô fora do centro: o novo objeto tem o pivô no centro da base do móvel.
///  - Distorção ao girar: móveis que eram filhos de objetos com escala não uniforme (ex.: o prato
///    e o vaso dentro de Cube.008) passam a ficar num pai com escala uniforme, sem cisalhamento.
///  - Escala/rotação estranhas do FBX (Z para cima, escala 100, escalas negativas) ficam embutidas
///    na malha; o objeto final tem escala 1 e só rotação em torno do eixo vertical.
///
/// Os objetos originais são desativados (não apagados) e podem ser removidos depois.
/// Tudo pode ser desfeito com Ctrl+Z.
/// </summary>
public static class MoveableNormalizer
{
    private const string MoveableTag = "Moveable";
    private const string MenuPath = "Tools/MAC0693/Normalizar Móveis";
    private const string OriginalSuffix = " (original)";

    [MenuItem(MenuPath)]
    public static void NormalizeAll()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Normalizar Móveis", "Saia do Play Mode antes de rodar a ferramenta.", "OK");
            return;
        }

        List<Transform> moveables = new List<Transform>();
        foreach (GameObject go in GameObject.FindGameObjectsWithTag(MoveableTag))
        {
            if (!IsAlreadyNormalized(go.transform)) moveables.Add(go.transform);
        }

        if (moveables.Count == 0)
        {
            EditorUtility.DisplayDialog("Normalizar Móveis", "Nenhum móvel (tag Moveable) precisa ser normalizado.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Normalizar Móveis",
                $"{moveables.Count} móvel(is) com a tag '{MoveableTag}' serão recriados com pivô no centro e sem " +
                "escala distorcida.\n\nOs originais serão desativados (não apagados). Dá para desfazer com Ctrl+Z.",
                "Normalizar", "Cancelar"))
        {
            return;
        }

        var scene = SceneManager_GetActiveScene();
        string folder = EnsureFolder($"Assets/Models/Baked/{scene.name}");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Normalizar Móveis");

        // Primeiro cria todas as versões novas (os originais precisam estar intactos para os móveis aninhados)
        var created = new List<GameObject>();
        try
        {
            for (int i = 0; i < moveables.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Normalizar Móveis", moveables[i].name, (float)i / moveables.Count);
                GameObject result = Normalize(moveables[i], folder);
                if (result != null) created.Add(result);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Depois desativa os originais
        foreach (Transform original in moveables)
        {
            Undo.RecordObject(original.gameObject, "Desativar original");
            original.gameObject.tag = "Untagged";
            original.gameObject.name += OriginalSuffix;
            original.gameObject.SetActive(false);
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.objects = created.ToArray();

        Debug.Log($"[MoveableNormalizer] {created.Count} móvel(is) normalizado(s). Malhas salvas em '{folder}'. " +
                  "Os originais foram desativados com o sufixo '(original)'.");
    }

    private static UnityEngine.SceneManagement.Scene SceneManager_GetActiveScene() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene();

    // Um móvel já normalizado: sem malha própria, escala 1, pai com escala uniforme
    private static bool IsAlreadyNormalized(Transform t)
    {
        if (t.GetComponent<MeshFilter>() != null) return false;
        if ((t.localScale - Vector3.one).sqrMagnitude > 1e-6f) return false;
        return t.parent == null || IsUniform(t.parent.lossyScale);
    }

    private static GameObject Normalize(Transform moveable, string folder)
    {
        List<MeshRenderer> renderers = CollectRenderers(moveable);
        if (renderers.Count == 0)
        {
            Debug.LogWarning($"[MoveableNormalizer] '{moveable.name}' não tem malhas; ignorado.", moveable);
            return null;
        }

        // Pai: o ancestral mais próximo com escala uniforme (evita herdar distorção)
        Transform container = moveable.parent;
        while (container != null && !IsUniform(container.lossyScale)) container = container.parent;

        // Pivô no centro da base, alinhado à "frente" do móvel no plano horizontal
        Bounds bounds = renderers[0].bounds;
        foreach (MeshRenderer r in renderers) bounds.Encapsulate(r.bounds);

        GameObject pivot = new GameObject(moveable.name);
        Undo.RegisterCreatedObjectUndo(pivot, "Criar móvel normalizado");
        pivot.tag = MoveableTag;
        pivot.layer = moveable.gameObject.layer;
        pivot.transform.SetParent(container, false);
        pivot.transform.SetPositionAndRotation(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
            GetHorizontalOrientation(moveable));
        pivot.transform.localScale = Vector3.one;
        pivot.transform.SetSiblingIndex(moveable.GetSiblingIndex());

        Matrix4x4 worldToPivot = pivot.transform.worldToLocalMatrix;
        string safeName = MakeSafeFileName(moveable.name);

        for (int i = 0; i < renderers.Count; i++)
        {
            MeshRenderer source = renderers[i];
            Mesh sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;

            Mesh baked = BakeMesh(sourceMesh, worldToPivot * source.transform.localToWorldMatrix);
            if (baked == null)
            {
                Debug.LogError($"[MoveableNormalizer] Não foi possível ler a malha '{sourceMesh.name}' de '{source.name}'. " +
                               "Selecione o modelo (.fbx) no Project, marque 'Read/Write' na aba Model, clique em Apply e rode de novo.", source);
                Undo.DestroyObjectImmediate(pivot);
                return null;
            }
            baked.name = $"{safeName}_{i}_{MakeSafeFileName(source.name)}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baked.name}.asset");
            AssetDatabase.CreateAsset(baked, path);

            GameObject part = new GameObject(source.name);
            part.layer = source.gameObject.layer;
            part.transform.SetParent(pivot.transform, false);

            part.AddComponent<MeshFilter>().sharedMesh = baked;

            MeshRenderer mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterials = source.sharedMaterials;
            mr.shadowCastingMode = source.shadowCastingMode;
            mr.receiveShadows = source.receiveShadows;
            mr.lightProbeUsage = source.lightProbeUsage;
            mr.reflectionProbeUsage = source.reflectionProbeUsage;
            // Objeto que se move não deve usar lightmap baked na posição antiga
            mr.receiveGI = ReceiveGI.LightProbes;

            part.AddComponent<MeshCollider>().sharedMesh = baked;
        }

        return pivot;
    }

    // Todas as malhas do móvel, exceto as que pertencem a outro móvel aninhado (esses viram objetos próprios)
    private static List<MeshRenderer> CollectRenderers(Transform moveable)
    {
        var result = new List<MeshRenderer>();
        foreach (MeshRenderer r in moveable.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!r.enabled) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (BelongsToNestedMoveable(r.transform, moveable)) continue;
            result.Add(r);
        }
        return result;
    }

    private static bool BelongsToNestedMoveable(Transform t, Transform root)
    {
        for (Transform p = t; p != null && p != root; p = p.parent)
        {
            if (p.CompareTag(MoveableTag)) return true;
        }
        return false;
    }

    // Rotação só em torno do eixo vertical, seguindo o eixo local do móvel que fica mais "deitado"
    private static Quaternion GetHorizontalOrientation(Transform t)
    {
        Vector3[] axes = { t.right, t.up, t.forward };
        int vertical = 0;
        for (int i = 1; i < 3; i++)
        {
            if (Mathf.Abs(Vector3.Dot(axes[i], Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[vertical], Vector3.up))) vertical = i;
        }

        for (int i = 0; i < 3; i++)
        {
            if (i == vertical) continue;
            Vector3 flat = Vector3.ProjectOnPlane(axes[i], Vector3.up);
            if (flat.sqrMagnitude > 1e-4f) return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }
        return Quaternion.identity;
    }

    // Copia a malha aplicando a matriz nos vértices (corrige normais, tangentes e escala negativa)
    private static Mesh BakeMesh(Mesh src, Matrix4x4 m)
    {
        var dst = new Mesh();
        if (src.vertexCount > 65535) dst.indexFormat = IndexFormat.UInt32;

        Matrix4x4 normalMatrix = m.inverse.transpose;
        bool flip = m.determinant < 0f;

        var vertices = new List<Vector3>();
        src.GetVertices(vertices);
        if (vertices.Count == 0 && src.vertexCount > 0) return null; // malha sem Read/Write
        for (int i = 0; i < vertices.Count; i++) vertices[i] = m.MultiplyPoint3x4(vertices[i]);
        dst.SetVertices(vertices);

        var normals = new List<Vector3>();
        src.GetNormals(normals);
        if (normals.Count == vertices.Count)
        {
            for (int i = 0; i < normals.Count; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            dst.SetNormals(normals);
        }

        var tangents = new List<Vector4>();
        src.GetTangents(tangents);
        if (tangents.Count == vertices.Count)
        {
            for (int i = 0; i < tangents.Count; i++)
            {
                Vector4 tg = tangents[i];
                Vector3 dir = m.MultiplyVector(new Vector3(tg.x, tg.y, tg.z)).normalized;
                tangents[i] = new Vector4(dir.x, dir.y, dir.z, flip ? -tg.w : tg.w);
            }
            dst.SetTangents(tangents);
        }

        var colors = new List<Color>();
        src.GetColors(colors);
        if (colors.Count == vertices.Count) dst.SetColors(colors);

        var uvs = new List<Vector4>();
        for (int channel = 0; channel < 8; channel++)
        {
            src.GetUVs(channel, uvs);
            if (uvs.Count == vertices.Count) dst.SetUVs(channel, uvs);
        }

        dst.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            MeshTopology topology = src.GetTopology(s);
            int[] indices = src.GetIndices(s);

            if (flip && topology == MeshTopology.Triangles)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                }
            }
            dst.SetIndices(indices, topology, s);
        }

        dst.RecalculateBounds();
        return dst;
    }

    private static bool IsUniform(Vector3 s)
    {
        float ax = Mathf.Abs(s.x), ay = Mathf.Abs(s.y), az = Mathf.Abs(s.z);
        float max = Mathf.Max(ax, Mathf.Max(ay, az));
        float min = Mathf.Min(ax, Mathf.Min(ay, az));
        bool allPositive = s.x > 0f && s.y > 0f && s.z > 0f;
        return allPositive && max > 0f && (max - min) / max < 0.01f;
    }

    private static string EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
        return current;
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_').Trim();
    }
}
