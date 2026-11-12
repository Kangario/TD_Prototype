// Настройки импорта арта P1 (Assets/Art). VFX-кит настраивает свой VfxKitImporter.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ArtImport : AssetPostprocessor
{
    const string Root = "Assets/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root) || Path.GetFileName(assetPath) != "T_Atlas.png") return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.anisoLevel = 2;
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Root) || assetPath.Contains("/VFX/")) return;
        var mi = (ModelImporter)assetImporter;
        bool animated = Path.GetFileNameWithoutExtension(assetPath) == "SM_Portal";
        mi.useFileScale = true;
        mi.globalScale = 1f;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importVisibility = false;
        mi.importBlendShapes = false;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.None;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        // меши с цветом по треугольникам переписываются в OnPostprocessModel - нужен Read/Write
        mi.isReadable = System.Array.IndexOf(TriangleColorModels, Path.GetFileNameWithoutExtension(assetPath)) >= 0;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.importAnimation = animated;
        mi.animationType = animated ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
    }

    // Модели, которые красятся цветом по треугольникам (P1/Arcane, галка «4 colors by triangle»).
    public static readonly string[] TriangleColorModels = { "SM_Ground_Tile" };

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(Root)) return;
        if (System.Array.IndexOf(TriangleColorModels, Path.GetFileNameWithoutExtension(assetPath)) < 0) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) SplitTriangles(mf.sharedMesh);
    }

    /// Каждый треугольник получает свои 3 вершины, в UV2 (TEXCOORD2) - центр треугольника в пространстве объекта.
    /// По нему шейдер выбирает один из 4 цветов: 1 треугольник = 1 цвет.
    static void SplitTriangles(Mesh m)
    {
        var pos = m.vertices;
        var nrm = m.normals;
        var col = m.colors32;
        var uv0 = new List<Vector4>();
        var uv1 = new List<Vector4>();
        m.GetUVs(0, uv0);
        m.GetUVs(1, uv1);

        int subCount = m.subMeshCount;
        var subs = new int[subCount][];
        int total = 0;
        for (int s = 0; s < subCount; s++)
        {
            subs[s] = m.GetTriangles(s);
            total += subs[s].Length;
        }

        var nPos = new Vector3[total];
        var nNrm = nrm.Length > 0 ? new Vector3[total] : null;
        var nCol = col.Length > 0 ? new Color32[total] : null;
        var nUv0 = uv0.Count > 0 ? new List<Vector4>(total) : null;
        var nUv1 = uv1.Count > 0 ? new List<Vector4>(total) : null;
        var center = new List<Vector3>(total);
        var nSubs = new int[subCount][];

        int k = 0;
        for (int s = 0; s < subCount; s++)
        {
            var tris = subs[s];
            nSubs[s] = new int[tris.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 c = (pos[tris[t]] + pos[tris[t + 1]] + pos[tris[t + 2]]) / 3f;
                for (int j = 0; j < 3; j++, k++)
                {
                    int v = tris[t + j];
                    nPos[k] = pos[v];
                    if (nNrm != null) nNrm[k] = nrm[v];
                    if (nCol != null) nCol[k] = col[v];
                    nUv0?.Add(uv0[v]);
                    nUv1?.Add(uv1[v]);
                    center.Add(c);
                    nSubs[s][t + j] = k;
                }
            }
        }

        m.Clear();
        m.indexFormat = total > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        m.vertices = nPos;
        if (nNrm != null) m.normals = nNrm;
        if (nCol != null) m.colors32 = nCol;
        if (nUv0 != null) m.SetUVs(0, nUv0);
        if (nUv1 != null) m.SetUVs(1, nUv1);
        m.SetUVs(2, center);
        m.subMeshCount = subCount;
        for (int s = 0; s < subCount; s++) m.SetTriangles(nSubs[s], s, false);
        m.RecalculateBounds();
    }
}
