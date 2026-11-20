// Arcane-вид проекта одним действием:
//  - шейдер P1/Arcane на всех лит-материалах (окружение, гномы/оружие, персонажи AA, X Bot и прочие модели);
//  - URP: HDR + HDR-грейдинг, жёсткие тени дальше и чётче, SSAO под рисованные контактные тени;
//  - пост-обработка и цветокор (Assets/Settings/PP_Arcane.asset): ACES, холодные тени / тёплый свет,
//    bloom, виньетка, зерно, лёгкая хроматика, размытие дальнего плана;
//  - сцены: тёплое низкое солнце, холодный трёхцветный ambient, тёмный фон, глобальный Volume,
//    ArcaneAtmosphere (ветер, тени облаков), серый туман выключен - вместо него облачка ArcaneCloudField.
// Меню: Tools > P1 Art > Arcane Look > Apply All. Повторный запуск безопасен.
// Batchmode: -executeMethod ArcaneLook.RunBatch
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ArcaneLook
{
    public const string ShaderName = "P1/Arcane";
    public const string CloudShaderName = "P1/ArcaneCloud";
    const string MatDir = "Assets/Art/Materials";
    const string AtlasMat = MatDir + "/M_Toon_Atlas.mat";
    const string UnitsAtlasMat = MatDir + "/M_Arcane_Atlas_Units.mat";
    const string GrassMat = MatDir + "/M_Arcane_Grass.mat";
    const string OutlineMat = MatDir + "/M_Outline.mat";
    const string CharacterMat = "Assets/Characters/AA_Universal/Materials/UniversalCharacterMaterial.mat";
    const string BrushTex = "Assets/Art/Textures/T_ArcaneBrush.png";
    const string CloudTex = "Assets/Art/Textures/T_CloudPuffs.png";
    const string ProfilePath = "Assets/Settings/PP_Arcane.asset";
    const string ShowcaseScene = "Assets/Scenes/ArtShowcase.unity";
    static readonly string[] Scenes = { ShowcaseScene, "Assets/Scenes/CharacterLineup.unity", "Assets/_Project/Scenes/Game.unity" };
    static readonly string[] UnitModelDirs = { "Assets/Art/Characters", "Assets/Art/Weapons" };
    // монстры (враги): пак LowPoly Fantasy Monsters, материалы переводятся на месте - префабы пака уже ссылаются на них
    const string MonsterDir = "Assets/LowPoly Fantasy Monsters Pack Ver1.0_Demo";

    // ------------------------------------------------------------------ палитра
    static readonly Color SunColor = new Color(1f, 0.85f, 0.66f);
    const float SunIntensity = 1.6f;
    // направление НА солнце: слева и чуть со стороны камеры, ~40° - грани к камере освещены (читаемость TD),
    // правые стороны в тени, длинные тени уходят вправо-назад
    static readonly Vector3 SunFrom = new Vector3(-0.68f, 0.64f, 0.36f);
    static readonly Color AmbientSky = new Color(0.34f, 0.4f, 0.58f);
    static readonly Color AmbientEquator = new Color(0.26f, 0.25f, 0.36f);
    static readonly Color AmbientGround = new Color(0.14f, 0.11f, 0.14f);
    static readonly Color Background = new Color(0.05f, 0.065f, 0.095f);
    static readonly Color ShadowCool = new Color(0.44f, 0.41f, 0.6f);
    static readonly Color OutlineInk = new Color(0.07f, 0.04f, 0.08f);

    [MenuItem("Tools/P1 Art/Arcane Look/Apply All", priority = 1)]
    public static void ApplyAllMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Arcane Look", "Выйдите из Play Mode.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        ApplyAll();
    }

    [MenuItem("Tools/P1 Art/Arcane Look/Materials Only", priority = 2)]
    public static void MaterialsMenu()
    {
        SetupTextures();
        SetupMaterials();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/P1 Art/Arcane Look/Monsters Only", priority = 3)]
    public static void MonstersMenu()
    {
        SetupMonsters();
        AssetDatabase.SaveAssets();
    }

    /// Материалы монстров (Standard из пака) -> P1/Arcane в настройке ConfigureMonster. Повторный запуск безопасен.
    public static int SetupMonsters()
    {
        if (!AssetDatabase.IsValidFolder(MonsterDir)) return 0;
        var shader = FindShader(ShaderName);
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MonsterDir }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null || (m.shader != shader && !IsConvertible(m.shader))) continue;   // частицы демо-сцен не трогаем
            ConvertKeepingLook(m, shader);
            ConfigureMonster(m);
            n++;
        }
        Debug.Log("[ArcaneLook] монстры: материалов " + n);
        return n;
    }

    [MenuItem("Tools/P1 Art/Arcane Look/Setup Open Scenes", priority = 4)]
    public static void OpenScenesMenu()
    {
        var profile = SetupVolumeProfile();
        var active = SceneManager.GetActiveScene();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            SceneManager.SetActiveScene(s);
            SetupScene(s, profile);
        }
        SceneManager.SetActiveScene(active);
    }

    public static void RunBatch()
    {
        try { ApplyAll(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void ApplyAll()
    {
        SetupTextures();
        SetupMaterials();
        var profile = SetupVolumeProfile();
        SetupPipeline(profile);
        foreach (var path in Scenes) SetupSceneAsset(path, profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[ArcaneLook] OK: материалы, URP, пост-обработка " + ProfilePath + ", сцены: " + string.Join(", ", Scenes));
    }

    // ------------------------------------------------------------------ текстуры (линейные данные)
    static void SetupTextures()
    {
        LinearData(BrushTex, TextureWrapMode.Repeat, 4);
        LinearData(CloudTex, TextureWrapMode.Clamp, 1);
    }

    static void LinearData(string path, TextureWrapMode wrap, int aniso)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) throw new System.Exception("Нет текстуры " + path);
        if (!ti.sRGBTexture && ti.wrapMode == wrap && ti.anisoLevel == aniso && ti.mipmapEnabled && !ti.alphaIsTransparency &&
            ti.alphaSource == TextureImporterAlphaSource.FromInput && ti.textureCompression == TextureImporterCompression.Uncompressed)
            return;
        ti.sRGBTexture = false;
        ti.wrapMode = wrap;
        ti.anisoLevel = aniso;
        ti.mipmapEnabled = true;
        ti.alphaIsTransparency = false;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 512;
        ti.SaveAndReimport();
    }

    // ------------------------------------------------------------------ материалы
    public static void SetupMaterials()
    {
        var shader = FindShader(ShaderName);

        // окружение: общий атлас с масками vertex color, мазки в мире
        var atlas = AssetDatabase.LoadAssetAtPath<Material>(AtlasMat);
        if (atlas != null)
        {
            atlas.shader = shader;
            ConfigureEnvironment(atlas);
        }

        // гномы и оружие: тот же атлас, но мазки в пространстве объекта (не «плывут» по идущему юниту)
        if (atlas != null)
        {
            var units = LoadOrCreateMaterial(UnitsAtlasMat, shader);
            units.SetTexture("_BaseMap", atlas.GetTexture("_BaseMap"));
            ConfigureUnitsAtlas(units);
            RemapModels(UnitModelDirs, atlas, units);
        }

        // трава: без текстуры, 4 цвета из материала, 1 треугольник = 1 цвет
        if (atlas != null) SetupGrass(atlas, shader);

        // контур-оболочка из Blender: чернила Arcane
        var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlineMat);
        if (outline != null)
        {
            outline.SetColor("_OutlineColor", OutlineInk);
            EditorUtility.SetDirty(outline);
        }

        // персонажи AA
        var aa = AssetDatabase.LoadAssetAtPath<Material>(CharacterMat);
        if (aa != null)
        {
            ConvertKeepingLook(aa, shader);
            ConfigureCharacter(aa);
        }

        // остальные лит-материалы проекта (частицы и декали VFX не трогаем)
        int other = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/Art/VFX/") || path.StartsWith(MonsterDir + "/")) continue;   // монстры - SetupMonsters
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m == atlas || m == aa || m == outline || !IsConvertible(m.shader)) continue;
            ConvertKeepingLook(m, shader);
            if (path.StartsWith("Assets/Characters/")) ConfigureCharacter(m);
            else ConfigureProp(m);
            other++;
        }

        // монстры: свой, более тёмный вариант персонажа
        SetupMonsters();

        // встроенные материалы моделей вне Assets/Art (X Bot, пропсы пака)
        RemapEmbeddedMaterials(shader);
        AssetDatabase.SaveAssets();
        Debug.Log("[ArcaneLook] материалы: атлас, юниты, персонажи AA, прочих " + other);
    }

    static bool IsConvertible(Shader s)
    {
        if (s == null) return false;
        string n = s.name;
        return n == "P1/Toon" || n == "Standard" || n == "Standard (Specular setup)" || n == "Universal Render Pipeline/Lit" ||
               n == "Universal Render Pipeline/Simple Lit" || n == "Universal Render Pipeline/Complex Lit" ||
               n == "Universal Render Pipeline/Baked Lit";
    }

    /// Переводит материал на P1/Arcane, сохраняя текстуру и цвет (Lit: _BaseMap/_BaseColor, Standard: _MainTex/_Color).
    static void ConvertKeepingLook(Material m, Shader shader)
    {
        if (m.shader == shader) return;
        Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
        m.shader = shader;
        m.shaderKeywords = new string[0];
        m.renderQueue = -1;
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Cull", cull);
    }

    /// Общие настройки - всё явно, чтобы повторный запуск давал тот же вид.
    static void Common(Material m)
    {
        m.SetTexture("_BrushTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BrushTex));
        m.SetColor("_LightTint", new Color(1f, 0.97f, 0.93f));
        m.SetColor("_ShadowColor", ShadowCool);
        m.SetFloat("_AmbientStrength", 0.45f);
        m.SetFloat("_ShadowSaturation", 1.2f);
        m.SetFloat("_ShadowEdge", 0.05f);
        m.SetFloat("_ShadowSoftness", 0.05f);
        m.SetFloat("_HighlightEdge", 0.72f);
        m.SetFloat("_HighlightBoost", 0.12f);
        m.SetColor("_TerminatorColor", new Color(1f, 0.36f, 0.2f));
        m.SetFloat("_TerminatorStrength", 0.35f);
        m.SetFloat("_GrainStrength", 0.05f);
        m.SetColor("_HatchColor", new Color(0.55f, 0.5f, 0.72f));
        m.SetFloat("_VariationScale", 0.03f);
        m.SetColor("_VariationWarm", new Color(1.08f, 0.97f, 0.84f));
        m.SetColor("_VariationCool", new Color(0.86f, 0.95f, 1.08f));
        m.SetColor("_ArcSpecColor", new Color(1f, 0.95f, 0.85f));
        m.SetFloat("_SpecSize", 0.965f);
        m.SetColor("_RimColor", new Color(1f, 0.8f, 0.58f));
        m.SetColor("_RimShadowColor", new Color(0.2f, 0.55f, 0.8f));
        m.SetFloat("_RimMax", 0.82f);
        m.SetFloat("_AOStrength", 0.75f);
        m.SetColor("_GlowHotColor", new Color(1f, 0.85f, 0.62f));
        m.SetColor("_EmissionColor", Color.black);
        m.SetColor("_OutlineColor", OutlineInk);
        m.SetFloat("_OutlineAlbedo", 0.35f);
        m.enableInstancing = true;
    }

    public static void ConfigureEnvironment(Material m)
    {
        Common(m);
        m.SetFloat("_VertexMasks", 1f);
        m.SetFloat("_BrushObjectSpace", 0f);
        m.SetFloat("_BrushScale", 0.08f);        // тайл ~12 м: мазки 0.4-1 м - читаются с камеры TD
        m.SetFloat("_BrushShading", 0.35f);
        m.SetFloat("_BrushAlbedo", 0.12f);
        m.SetFloat("_ShadowWobble", 0.3f);
        m.SetFloat("_HatchStrength", 0.15f);
        m.SetFloat("_VariationStrength", 0.45f);
        m.SetFloat("_SpecAmount", 0f);
        m.SetFloat("_SpecStrength", 0.5f);
        m.SetFloat("_RimMin", 0.62f);
        m.SetFloat("_RimStrength", 0.22f);
        m.SetFloat("_EmitMult", 2.2f);
        SetOutline(m, 0f);                        // у мешей из Blender своя оболочка-контур (M_Outline)
        EditorUtility.SetDirty(m);
    }

    /// M_Arcane_Grass на моделях из ArtImport.TriangleColorModels. Цвета ставятся только при создании -
    /// дальше их правят в инспекторе материала, повторный запуск их не трогает.
    static void SetupGrass(Material atlas, Shader shader)
    {
        bool created = AssetDatabase.LoadAssetAtPath<Material>(GrassMat) == null;
        var m = LoadOrCreateMaterial(GrassMat, shader);
        ConfigureEnvironment(m);
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_VertexMasks", 0f);
        m.SetFloat("_TriColors", 1f);
        m.EnableKeyword("_TRI_COLORS");
        if (created)
        {
            m.SetColor("_TriColor0", new Color(0.36f, 0.55f, 0.25f));
            m.SetColor("_TriColor1", new Color(0.42f, 0.62f, 0.28f));
            m.SetColor("_TriColor2", new Color(0.30f, 0.48f, 0.22f));
            m.SetColor("_TriColor3", new Color(0.48f, 0.64f, 0.30f));
        }
        EditorUtility.SetDirty(m);

        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.Array.IndexOf(ArtImport.TriangleColorModels, Path.GetFileNameWithoutExtension(path)) < 0) continue;
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            bool changed = false;
            foreach (var kv in mi.GetExternalObjectMap())
            {
                if (kv.Key.type != typeof(Material) || kv.Value != atlas) continue;
                mi.AddRemap(kv.Key, m);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }
    }

    public static void ConfigureUnitsAtlas(Material m)
    {
        ConfigureEnvironment(m);
        m.SetFloat("_BrushObjectSpace", 1f);
        m.SetFloat("_BrushScale", 0.7f);
        m.SetFloat("_ShadowWobble", 0.04f);
        m.SetFloat("_VariationStrength", 0.12f);
        m.SetFloat("_RimMin", 0.55f);
        m.SetFloat("_RimStrength", 0.5f);
        EditorUtility.SetDirty(m);
    }

    public static void ConfigureCharacter(Material m)
    {
        Common(m);
        m.SetFloat("_VertexMasks", 0f);
        m.SetFloat("_BrushObjectSpace", 1f);
        m.SetFloat("_BrushScale", 0.7f);
        m.SetFloat("_BrushShading", 0.3f);
        m.SetFloat("_BrushAlbedo", 0.08f);
        m.SetFloat("_ShadowWobble", 0.04f);
        m.SetFloat("_HatchStrength", 0.2f);
        m.SetFloat("_VariationStrength", 0.12f);
        m.SetFloat("_SpecAmount", 0.35f);
        m.SetFloat("_SpecStrength", 0.4f);
        m.SetFloat("_RimMin", 0.55f);
        m.SetFloat("_RimStrength", 0.55f);
        m.SetFloat("_EmitMult", 2f);
        SetOutline(m, 1.3f);
        EditorUtility.SetDirty(m);
    }

    /// Монстры: тот же рисованный тун, что у героев, но чуть темнее и злее. Текстура пака без подкраски
    /// (серый _Color пака был под блики Standard, в туне он только гасит цвет); свет немного приглушён, тень на тон
    /// глубже и фиолетовее, граница тени чуть сдвинута, тёплого ободка меньше, холодный кикер в тени сильнее
    /// (силуэт читается и в тени), штриховка гуще, контур толще.
    public static void ConfigureMonster(Material m)
    {
        ConfigureCharacter(m);
        m.SetColor("_BaseColor", Color.white);
        m.SetColor("_LightTint", new Color(0.93f, 0.88f, 0.86f));
        m.SetColor("_ShadowColor", new Color(0.33f, 0.29f, 0.48f));
        m.SetFloat("_AmbientStrength", 0.38f);
        m.SetFloat("_ShadowSaturation", 1.3f);
        m.SetFloat("_ShadowEdge", 0.09f);
        m.SetFloat("_HighlightBoost", 0.08f);
        m.SetColor("_TerminatorColor", new Color(0.9f, 0.25f, 0.2f));
        m.SetFloat("_TerminatorStrength", 0.4f);
        m.SetFloat("_HatchStrength", 0.26f);
        m.SetColor("_HatchColor", new Color(0.5f, 0.45f, 0.68f));
        m.SetFloat("_SpecAmount", 0.25f);
        m.SetFloat("_SpecStrength", 0.35f);
        m.SetColor("_RimColor", new Color(1f, 0.66f, 0.48f));
        m.SetColor("_RimShadowColor", new Color(0.3f, 0.55f, 0.9f));
        m.SetFloat("_RimMin", 0.55f);
        m.SetFloat("_RimStrength", 0.6f);
        m.SetFloat("_OutlineAlbedo", 0.25f);
        SetOutline(m, 1.5f);
        EditorUtility.SetDirty(m);
    }

    static void ConfigureProp(Material m)
    {
        ConfigureCharacter(m);
        m.SetFloat("_BrushObjectSpace", 0f);
        m.SetFloat("_BrushScale", 0.15f);
        m.SetFloat("_ShadowWobble", 0.15f);
        m.SetFloat("_VariationStrength", 0.35f);
        m.SetFloat("_RimStrength", 0.3f);
        SetOutline(m, 1f);
        EditorUtility.SetDirty(m);
    }

    static void SetOutline(Material m, float px)
    {
        m.SetFloat("_OutlineWidth", px);
        m.SetShaderPassEnabled("SRPDefaultUnlit", px > 0f);
    }

    /// Перекидывает внешние материалы моделей с from на to (ремап по имени материала в FBX).
    static void RemapModels(string[] dirs, Material from, Material to)
    {
        var dirsOk = dirs.Where(AssetDatabase.IsValidFolder).ToArray();
        if (dirsOk.Length == 0) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", dirsOk))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            bool changed = false;
            foreach (var kv in mi.GetExternalObjectMap())
            {
                if (kv.Key.type != typeof(Material) || kv.Value != from) continue;
                mi.AddRemap(kv.Key, to);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }
    }

    /// Модели вне Assets/Art со встроенными материалами (X Bot и т.п.): внешний материал P1/Arcane с их цветом.
    static void RemapEmbeddedMaterials(Shader shader)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/Art/") || path.Contains("/Animations/")) continue;
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null || mi.materialImportMode == ModelImporterMaterialImportMode.None) continue;

            // уже переведённые - просто освежаем настройки
            foreach (var kv in mi.GetExternalObjectMap())
                if (kv.Value is Material ext && ext.shader == shader && AssetDatabase.GetAssetPath(ext).StartsWith(MatDir + "/M_Arcane_"))
                    ConfigureCharacter(ext);

            bool changed = false;
            string model = Sanitize(Path.GetFileNameWithoutExtension(path));
            foreach (var em in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string matPath = MatDir + "/M_Arcane_" + model + "_" + Sanitize(em.name) + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null)
                {
                    m = new Material(em);
                    AssetDatabase.CreateAsset(m, matPath);
                }
                ConvertKeepingLook(m, shader);
                if (em.HasProperty("_BaseColor")) m.SetColor("_BaseColor", em.GetColor("_BaseColor"));
                else if (em.HasProperty("_Color")) m.SetColor("_BaseColor", em.GetColor("_Color"));
                ConfigureCharacter(m);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name), m);
                changed = true;
            }
            if (changed)
            {
                mi.SaveAndReimport();
                Debug.Log("[ArcaneLook] встроенные материалы -> P1/Arcane: " + path);
            }
        }
    }

    static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // ------------------------------------------------------------------ облачка
    static (Material clouds, Material mist) SetupCloudMaterials()
    {
        var shader = FindShader(CloudShaderName);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CloudTex);

        var clouds = LoadOrCreateMaterial(MatDir + "/M_Arcane_Clouds.mat", shader);
        clouds.SetTexture("_CloudTex", tex);
        clouds.SetColor("_LitColor", new Color(1f, 0.94f, 0.86f));
        clouds.SetColor("_MidColor", new Color(0.8f, 0.79f, 0.9f));
        clouds.SetColor("_ShadowColor", new Color(0.54f, 0.53f, 0.7f));
        clouds.SetColor("_RimColor", new Color(1f, 0.86f, 0.66f));
        clouds.SetFloat("_LightInfluence", 0.45f);
        clouds.SetFloat("_LitEdge", 0.4f);
        clouds.SetFloat("_MidEdge", -0.1f);
        clouds.SetFloat("_ShadeSoftness", 0.035f);
        clouds.SetFloat("_ShadeNoise", 0.3f);
        clouds.SetFloat("_RimStrength", 0.45f);
        clouds.SetFloat("_EdgeCut", 0.1f);
        clouds.SetFloat("_EdgeSoftness", 0.03f);
        clouds.SetFloat("_EdgeNoise", 0.14f);
        clouds.SetFloat("_Opacity", 1f);
        clouds.SetFloat("_DepthFade", 4f);
        clouds.SetFloat("_NearFadeStart", 8f);
        clouds.SetFloat("_NearFadeRange", 12f);
        clouds.SetFloat("_CycleSpeed", 0.02f);
        clouds.SetFloat("_DriftMul", 1f);
        clouds.SetFloat("_Jitter", 5f);
        clouds.SetFloat("_Bob", 0.3f);
        EditorUtility.SetDirty(clouds);

        var mist = LoadOrCreateMaterial(MatDir + "/M_Arcane_Mist.mat", shader);
        mist.CopyPropertiesFromMaterial(clouds);
        mist.SetColor("_LitColor", new Color(0.9f, 0.9f, 0.92f));
        mist.SetColor("_MidColor", new Color(0.74f, 0.77f, 0.86f));
        mist.SetColor("_ShadowColor", new Color(0.55f, 0.58f, 0.72f));
        mist.SetFloat("_RimStrength", 0.25f);
        mist.SetFloat("_EdgeCut", 0.18f);
        mist.SetFloat("_EdgeSoftness", 0.06f);
        mist.SetFloat("_Opacity", 0.38f);
        mist.SetFloat("_DepthFade", 1.2f);
        mist.SetFloat("_CycleSpeed", 0.04f);
        mist.SetFloat("_DriftMul", 0.6f);
        mist.SetFloat("_Jitter", 4f);
        mist.SetFloat("_Bob", 0.1f);
        EditorUtility.SetDirty(mist);
        return (clouds, mist);
    }

    // ------------------------------------------------------------------ URP
    static void SetupPipeline(VolumeProfile profile)
    {
        foreach (var (path, pc) in new[] { ("Assets/Settings/PC_RPAsset.asset", true), ("Assets/Settings/Mobile_RPAsset.asset", false) })
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) continue;
            var so = new SerializedObject(asset);
            Set(so, "m_SupportsHDR", 1f);                 // свечение и bloom по-настоящему ярче белого
            Set(so, "m_HDRColorBufferPrecision", 0f);     // 32 бита (R11G11B10) - дёшево, хватает
            Set(so, "m_ColorGradingMode", 1f);            // HDR-грейдинг: ACES
            Set(so, "m_ColorGradingLutSize", 32f);
            Set(so, "m_RequireDepthTexture", 1f);         // мягкие края облачков
            Set(so, "m_SoftShadowsSupported", 0f);        // рваный край тени рисует шейдер
            Set(so, "m_ShadowDistance", pc ? 150f : 120f);// камера TD далеко: тени до верхнего края кадра
            Set(so, "m_ShadowCascadeCount", pc ? 2f : 1f);
            Set(so, "m_Cascade2Split", 0.5f);
            Set(so, "m_MainLightShadowmapResolution", pc ? 4096f : 2048f);
            var vp = so.FindProperty("m_VolumeProfile");
            if (vp != null) vp.objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
        if (renderer != null)
            foreach (var f in renderer.rendererFeatures)
            {
                if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var so = new SerializedObject(f);
                Set(so, "m_Settings.AfterOpaque", 0f);        // AO берёт шейдер и красит цветом тени
                Set(so, "m_Settings.Source", 1f);             // глубина + нормали
                Set(so, "m_Settings.Intensity", 1.1f);
                Set(so, "m_Settings.Radius", 0.9f);           // масштаб под камеру TD
                Set(so, "m_Settings.DirectLightingStrength", 0.15f);
                Set(so, "m_Settings.Falloff", 220f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(f);
            }
        Debug.Log("[ArcaneLook] URP: HDR, ACES-грейдинг, тени 150 м / 4096, SSAO для рисованных контактных теней");
    }

    static void Set(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("[ArcaneLook] нет поля " + name); return; }
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float: p.floatValue = value; break;
            case SerializedPropertyType.Boolean: p.boolValue = value > 0.5f; break;
            default: p.intValue = (int)value; break;   // int и enum (значение, не индекс: 4096, а не 4)
        }
    }

    // ------------------------------------------------------------------ пост-обработка и цветокор
    public static VolumeProfile SetupVolumeProfile()
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, ProfilePath);
        }
        p.components.Clear();
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath).OfType<VolumeComponent>())
            Object.DestroyImmediate(sub, true);

        var tm = Add<Tonemapping>(p);
        tm.mode.Override(TonemappingMode.ACES);

        var ca = Add<ColorAdjustments>(p);
        ca.postExposure.Override(0.2f);
        ca.contrast.Override(10f);
        ca.saturation.Override(-4f);
        ca.colorFilter.Override(new Color(1f, 0.98f, 0.97f));

        var wb = Add<WhiteBalance>(p);
        wb.temperature.Override(-4f);
        wb.tint.Override(4f);

        // холодные бирюзовые тени, тёплый свет
        var st = Add<SplitToning>(p);
        st.shadows.Override(new Color(0.36f, 0.48f, 0.54f));
        st.highlights.Override(new Color(0.62f, 0.5f, 0.38f));
        st.balance.Override(-5f);

        var smh = Add<ShadowsMidtonesHighlights>(p);
        smh.shadows.Override(new Vector4(0.95f, 0.98f, 1.07f, -0.02f));
        smh.midtones.Override(new Vector4(1f, 0.98f, 1.01f, 0f));
        smh.highlights.Override(new Vector4(1.06f, 1f, 0.9f, 0f));

        var lgg = Add<LiftGammaGain>(p);
        lgg.lift.Override(new Vector4(0.98f, 0.99f, 1.03f, 0f));
        lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
        lgg.gain.Override(new Vector4(1.03f, 1f, 0.96f, 0f));

        var bloom = Add<Bloom>(p);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.7f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(new Color(1f, 0.86f, 0.72f));
        bloom.highQualityFiltering.Override(false);

        var vig = Add<Vignette>(p);
        vig.color.Override(new Color(0.06f, 0.02f, 0.08f));
        vig.center.Override(new Vector2(0.5f, 0.5f));
        vig.intensity.Override(0.34f);
        vig.smoothness.Override(0.42f);
        vig.rounded.Override(false);

        var grain = Add<FilmGrain>(p);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.22f);
        grain.response.Override(0.75f);

        var chroma = Add<ChromaticAberration>(p);
        chroma.intensity.Override(0.05f);

        // дальний план (верх кадра TD) чуть размыт - глубина как в кадрах Arcane
        var dof = Add<DepthOfField>(p);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(105f);
        dof.gaussianEnd.Override(190f);
        dof.gaussianMaxRadius.Override(1f);
        dof.highQualitySampling.Override(false);

        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    static T Add<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = p.Add<T>(false);
        c.name = typeof(T).Name;
        c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    // ------------------------------------------------------------------ сцены
    static void SetupSceneAsset(string path, VolumeProfile profile)
    {
        if (!File.Exists(path)) return;
        var scene = SceneManager.GetSceneByPath(path);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var prevActive = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);   // RenderSettings и новые объекты - в эту сцену
        SetupScene(scene, profile);
        EditorSceneManager.SaveScene(scene);
        if (prevActive.IsValid() && prevActive != scene) SceneManager.SetActiveScene(prevActive);
        if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
    }

    /// Свет, камера, Volume, атмосфера; в витрине - облачка и подсветка. Сцена должна быть активной.
    public static void SetupScene(Scene scene, VolumeProfile profile)
    {
        if (profile == null) profile = SetupVolumeProfile();
        bool showcase = scene.path == ShowcaseScene;

        // серую дымку убираем: атмосферу делают облачка и цветокор
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        RenderSettings.ambientIntensity = 1f;

        var sun = InScene<Light>(scene).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.color = SunColor;
        sun.useColorTemperature = false;
        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Hard;
        sun.shadowStrength = 1f;
        sun.transform.rotation = Quaternion.LookRotation(-SunFrom.normalized);
        RenderSettings.sun = sun;
        EditorUtility.SetDirty(sun);

        foreach (var cam in InScene<Camera>(scene))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;   // MSAA из ассета URP
            data.dithering = true;                       // без полос на виньетке и тёмных градиентах
            EditorUtility.SetDirty(cam);
            EditorUtility.SetDirty(data);
        }

        var volume = GetOrAdd<Volume>(FindOrCreate(scene, "Arcane Volume"));
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.weight = 1f;
        volume.sharedProfile = profile;

        var atmo = GetOrAdd<ArcaneAtmosphere>(FindOrCreate(scene, "Arcane Atmosphere"));
        atmo.Apply();

        if (showcase) SetupShowcaseExtras(scene);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// Витрина: облачка по краю карты (вместо серой дымки), редкие клочки над полем, подсветка портала и ратуши.
    static void SetupShowcaseExtras(Scene scene)
    {
        var mats = SetupCloudMaterials();

        // кольцо облаков вокруг карты (скалы стоят дугой радиуса 50-55 вокруг (0, 0, -14))
        var edgeGo = FindOrCreate(scene, "Clouds_Edge");
        edgeGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -14f), Quaternion.identity);
        var edge = GetOrAdd<ArcaneCloudField>(edgeGo);
        edge.area = ArcaneCloudField.Area.Ring;
        edge.innerRadius = 48f;
        edge.outerRadius = 76f;
        edge.arcFrom = -225f;
        edge.arcTo = 45f;
        edge.height = new Vector2(6f, 13f);
        edge.count = 80;
        edge.size = new Vector2(10f, 19f);
        edge.flatness = 0.8f;
        edge.opacityMin = 0.95f;
        edge.seed = 11;
        edgeGo.GetComponent<MeshRenderer>().sharedMaterial = mats.clouds;
        edge.Rebuild();

        // редкие клочки тумана над полем - полупрозрачные, низко
        var mistGo = FindOrCreate(scene, "Mist_Wisps");
        mistGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -8f), Quaternion.identity);
        var mist = GetOrAdd<ArcaneCloudField>(mistGo);
        mist.area = ArcaneCloudField.Area.Box;
        mist.boxSize = new Vector2(64f, 46f);
        mist.height = new Vector2(0.8f, 2.4f);
        mist.count = 7;
        mist.size = new Vector2(6f, 11f);
        mist.flatness = 0.5f;
        mist.opacityMin = 0.7f;
        mist.seed = 5;
        mistGo.GetComponent<MeshRenderer>().sharedMaterial = mats.mist;
        mist.Rebuild();

        // цветные источники - как неон Зауна и тёплые окна Пилтовера
        PointLight(scene, "Glow_Portal", new Vector3(0f, 2.2f, 16f), new Color(0.85f, 0.32f, 1f), 9f, 14f);
        PointLight(scene, "Glow_TownHall", new Vector3(0f, 3f, -21.5f), new Color(1f, 0.6f, 0.3f), 6f, 10f);
    }

    static void PointLight(Scene scene, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = FindOrCreate(scene, name);
        go.transform.position = pos;
        var l = GetOrAdd<Light>(go);
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        EditorUtility.SetDirty(l);
    }

    // ------------------------------------------------------------------ помощники
    static IEnumerable<T> InScene<T>(Scene s) where T : Component =>
        s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

    static GameObject FindOrCreate(Scene scene, string name)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go != null) return go;
        go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static Shader FindShader(string name)
    {
        var s = Shader.Find(name);
        if (s == null) throw new System.Exception("Шейдер не найден: " + name + " (есть ли ошибки компиляции?)");
        return s;
    }

    static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader) m.shader = shader;
        return m;
    }
}
