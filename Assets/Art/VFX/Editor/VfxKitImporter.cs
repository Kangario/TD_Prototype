// Настройки импорта VFX-кита. Лежит в папке Editor - в билд не попадает.
// Текстуры T_FX_*: альфа из PNG, Alpha Is Transparency, шумы - линейные и бесшовные.
// Меши SM_FX_*: без материалов и анимаций, Read/Write включён (нужно Shuriken в режиме Mesh).
using System.IO;
using UnityEditor;
using UnityEngine;

public class VfxKitImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        if (!name.StartsWith("T_FX_")) return;

        var ti = (TextureImporter)assetImporter;
        bool noise = name.StartsWith("T_FX_Noise");
        bool mask = name.StartsWith("T_FX_Mask");
        bool big = name.StartsWith("T_FX_FB_") || name.StartsWith("T_FX_Decal");

        ti.textureType = TextureImporterType.Default;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = !noise;
        ti.sRGBTexture = !noise;
        ti.mipmapEnabled = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.maxTextureSize = big ? 512 : 256;
        ti.textureCompression = TextureImporterCompression.Compressed;

        if (noise || name == "T_FX_Mask_Hex")
            ti.wrapMode = TextureWrapMode.Repeat;
        else if (mask && name != "T_FX_Mask_Trail")
        {
            ti.wrapModeU = TextureWrapMode.Repeat;   // по кругу меша - бесшовно
            ti.wrapModeV = TextureWrapMode.Clamp;
        }
        else
            ti.wrapMode = TextureWrapMode.Clamp;
    }

    void OnPreprocessModel()
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        if (!name.StartsWith("SM_FX_")) return;

        var mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;
        mi.isReadable = true;
    }
}
