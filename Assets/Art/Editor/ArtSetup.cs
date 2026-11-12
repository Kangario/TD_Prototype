// Подключение арта P1 к проекту: URP, материалы, префабы, примеры частиц, сцена-витрина.
// Меню: Tools > P1 Art > Setup All. Повторный запуск безопасен - всё пересоздаётся на тех же путях.
// Batchmode: Unity.exe -batchmode -projectPath <проект> -executeMethod ArtSetup.RunBatch -quit
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MMC = UnityEngine.ParticleSystem.MinMaxCurve;
using Object = UnityEngine.Object;

public static class ArtSetup
{
    const string Art = "Assets/Art";
    const string MatDir = Art + "/Materials";
    const string PrefabDir = Art + "/Prefabs";
    const string FxDir = Art + "/VFX";
    const string FxMatDir = FxDir + "/Materials";
    const string FxExDir = FxDir + "/Examples";
    const string SceneDir = "Assets/Scenes";
    const string ScenePath = SceneDir + "/ArtShowcase.unity";
    static readonly string[] ModelDirs = { Art + "/Characters", Art + "/Weapons", Art + "/Environment" };

    // Текстуры кита, которым по умолчанию нужен аддитив (остальные - Alpha Blend)
    static readonly HashSet<string> Additive = new HashSet<string>
    {
        "Glow", "GlowSmooth", "Dot", "Spark", "Star4", "Burst", "Mask_Beam", "Mask_Trail",
        "FB_Impact_4x4", "FB_MagicBurst_4x4"
    };

    [MenuItem("Tools/P1 Art/Setup All", priority = 0)]
    public static void SetupAll()
    {
        SetupUrp();
        var toon = SetupWorldMaterials();
        SetupModels(toon);
        var fx = SetupFxMaterials();
        var prefabs = SetupPrefabs();
        var examples = SetupFxExamples(fx);
        BuildShowcase(prefabs, examples, fx);
        AssetDatabase.SaveAssets();
        Debug.Log("[ArtSetup] OK: материалы, префабы, примеры частиц и сцена " + ScenePath);
    }

    public static void RunBatch()
    {
        try
        {
            SetupAll();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ URP
    static void SetupUrp()
    {
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
        if (pc == null)
            throw new System.Exception("Нет Assets/Settings/PC_RPAsset.asset - настройки URP не скопированы");
        Tune(pc, 80f, 2, 4);
        if (mobile != null)
            Tune(mobile, 60f, 1, 2);

        GraphicsSettings.defaultRenderPipeline = pc;
        int current = QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = names[i].Contains("Mobile") && mobile != null ? mobile : pc;
        }
        QualitySettings.SetQualityLevel(current, false);
        Debug.Log("[ArtSetup] URP: " + pc.name + ", уровни качества: " + string.Join(", ", names));
    }

    static void Tune(UniversalRenderPipelineAsset asset, float shadowDistance, int cascades, int msaa)
    {
        var so = new SerializedObject(asset);
        SetNumber(so, "m_ShadowDistance", shadowDistance);
        SetNumber(so, "m_ShadowCascadeCount", cascades);
        SetNumber(so, "m_MSAA", msaa);
        SetBool(so, "m_SoftShadowsSupported", false);   // toon: тени жёсткие
        SetBool(so, "m_SupportsHDR", false);            // WebGL: дешевле, ступенчатому свету HDR не нужен
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    static void SetNumber(SerializedObject so, string name, float value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("[ArtSetup] нет поля " + name); return; }
        if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
        else p.intValue = (int)value;
    }

    static void SetBool(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("[ArtSetup] нет поля " + name); return; }
        p.boolValue = value;
    }

    // ------------------------------------------------------------------ материалы мира
    static Material SetupWorldMaterials()
    {
        EnsureFolder(MatDir);
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/T_Atlas.png");
        // Arcane-вид (ArcaneLook): окружение - мазки в мире; юниты и оружие - отдельный материал, мазки на объекте
        var toon = LoadOrCreateMaterial(MatDir + "/M_Toon_Atlas.mat", ArcaneLook.ShaderName);
        toon.SetTexture("_BaseMap", atlas);
        ArcaneLook.ConfigureEnvironment(toon);
        var units = LoadOrCreateMaterial(MatDir + "/M_Arcane_Atlas_Units.mat", ArcaneLook.ShaderName);
        units.SetTexture("_BaseMap", atlas);
        ArcaneLook.ConfigureUnitsAtlas(units);
        var outline = LoadOrCreateMaterial(MatDir + "/M_Outline.mat", "P1/ToonOutline");
        outline.enableInstancing = true;
        outline.SetColor("_OutlineColor", new Color(0.07f, 0.04f, 0.08f));
        EditorUtility.SetDirty(toon);
        EditorUtility.SetDirty(units);
        EditorUtility.SetDirty(outline);
        return toon;
    }

    static Material LoadOrCreateMaterial(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new System.Exception("Шейдер не найден: " + shaderName);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.shader = shader;
        return m;
    }

    // ------------------------------------------------------------------ модели: материалы и анимация портала
    static void SetupModels(Material toon)
    {
        var outline = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Outline.mat");
        var units = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Arcane_Atlas_Units.mat");
        foreach (var guid in AssetDatabase.FindAssets("t:Model", ModelDirs))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            bool isUnit = !path.StartsWith(Art + "/Environment/") && units != null;   // герои и оружие двигаются
            // трава - свой материал с 4 цветами по треугольникам (ArcaneLook.SetupGrass)
            var grass = System.Array.IndexOf(ArtImport.TriangleColorModels, Path.GetFileNameWithoutExtension(path)) >= 0
                ? AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Arcane_Grass.mat") : null;
            bool changed = false;
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var target = m.name.StartsWith("M_Outline") ? outline : grass != null ? grass : isUnit ? units : toon;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), target);
                changed = true;
            }
            if (Path.GetFileNameWithoutExtension(path) == "SM_Portal")
            {
                var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.loopTime = true;
                    c.name = "Portal_Loop";
                }
                mi.clipAnimations = clips;
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ материалы частиц
    static Dictionary<string, Material> SetupFxMaterials()
    {
        EnsureFolder(FxMatDir);
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) throw new System.Exception("Нет шейдера URP Particles/Unlit");
        var result = new Dictionary<string, Material>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FxDir + "/Textures", FxDir + "/Flipbooks" }))
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            string texName = Path.GetFileNameWithoutExtension(texPath);
            if (!texName.StartsWith("T_FX_") || texName.StartsWith("T_FX_Noise") || texName == "T_FX_ScreenEdge")
                continue;
            string key = texName.Substring("T_FX_".Length);
            string path = FxMatDir + "/M_FX_" + key + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            SetupParticleMaterial(m, Additive.Contains(key), key.StartsWith("Mask_"));
            EditorUtility.SetDirty(m);
            result[key] = m;
        }
        return result;
    }

    static void SetupParticleMaterial(Material m, bool additive, bool twoSided)
    {
        m.SetFloat("_Surface", 1f);                 // Transparent
        m.SetFloat("_Blend", additive ? 2f : 0f);   // 0 = Alpha, 2 = Additive
        m.SetFloat("_ColorMode", 0f);               // Multiply
        m.SetFloat("_Cull", twoSided ? 0f : 2f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_FlipbookBlending", 0f);        // toon: кадры меняются ступенями
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.DisableKeyword("_ALPHAMODULATE_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
        ValidateWithUrpGui(m);
    }

    /// Штатная проверка URP: выставляет ключевые слова так же, как инспектор материала.
    static void ValidateWithUrpGui(Material m)
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.ParticlesUnlitShader"))
            .FirstOrDefault(t => t != null);
        if (type == null) return;
        try
        {
            var gui = System.Activator.CreateInstance(type, true);
            var method = type.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(gui, new object[] { m });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ArtSetup] ValidateMaterial: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ префабы моделей
    static Dictionary<string, GameObject> SetupPrefabs()
    {
        var result = new Dictionary<string, GameObject>();
        foreach (var dir in ModelDirs)
        {
            string sub = Path.GetFileName(dir);
            EnsureFolder(PrefabDir + "/" + sub);
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                string name = model.name;
                if (name == "SM_Portal") SetupPortal(inst, path);
                if (name == "SM_Ground_Tile" || name.StartsWith("SM_RoadTile"))
                    foreach (var r in inst.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = ShadowCastingMode.Off;   // плоское - тень не нужна
                string prefabPath = PrefabDir + "/" + sub + "/" + name.Replace("SM_", "") + ".prefab";
                result[name] = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
            }
        }
        Debug.Log("[ArtSetup] префабов: " + result.Count);
        return result;
    }

    static void SetupPortal(GameObject inst, string modelPath)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null)
        {
            Debug.LogWarning("[ArtSetup] в SM_Portal нет анимации");
            return;
        }
        string ctrlPath = Art + "/Environment/Portal.controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
        var animator = inst.GetComponent<Animator>();
        if (animator == null) animator = inst.AddComponent<Animator>();
        animator.runtimeAnimatorController = ctrl;
        animator.cullingMode = AnimatorCullingMode.CullCompletely;
    }

    // ------------------------------------------------------------------ примеры частиц (шаблоны для доработки)
    static Dictionary<string, GameObject> SetupFxExamples(Dictionary<string, Material> fx)
    {
        EnsureFolder(FxExDir);
        var result = new Dictionary<string, GameObject>();
        foreach (var go in new[] { ExampleBurning(fx), ExampleHit(fx), ExampleHeal(fx) })
        {
            result[go.name] = PrefabUtility.SaveAsPrefabAsset(go, FxExDir + "/" + go.name + ".prefab");
            Object.DestroyImmediate(go);
        }
        return result;
    }

    static ParticleSystem NewSystem(string name, Transform parent, Material mat, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startSpeed = 0f;
        main.startColor = Color.white;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;
        return ps;
    }

    static void Burst(ParticleSystem ps, float time, short count)
    {
        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(time, count) });
    }

    static void Sheet(ParticleSystem ps, float fps = 0f)
    {
        var t = ps.textureSheetAnimation;
        t.enabled = true;
        t.mode = ParticleSystemAnimationMode.Grid;
        t.numTilesX = 4;
        t.numTilesY = 4;
        t.animation = ParticleSystemAnimationType.WholeSheet;
        if (fps > 0f)
        {
            t.timeMode = ParticleSystemAnimationTimeMode.FPS;
            t.fps = fps;
        }
    }

    /// Альфа по времени жизни. stepped = Fixed (ступени; значение ключа действует слева от него).
    static void Alpha(ParticleSystem ps, bool stepped, params Vector2[] keys)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient { mode = stepped ? GradientMode.Fixed : GradientMode.Blend };
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  keys.Select(k => new GradientAlphaKey(k.y, k.x)).ToArray());
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static void SizeCurve(ParticleSystem ps, params Vector2[] keys)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new MMC(1f, new AnimationCurve(keys.Select(k => new Keyframe(k.x, k.y)).ToArray()));
    }

    static readonly Vector2[] SharpFade = { new Vector2(0.45f, 1f), new Vector2(0.75f, 0.6f), new Vector2(1f, 0.25f) };

    static GameObject ExampleBurning(Dictionary<string, Material> fx)
    {
        var root = new GameObject("FX_Burning");
        var fire = NewSystem("Fire", root.transform, fx["FB_Fire_4x4"], out _);
        var m = fire.main;
        m.duration = 1f;
        m.startLifetime = new MMC(0.5f, 0.7f);
        m.startSize = new MMC(0.45f, 0.75f);
        m.maxParticles = 16;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = fire.emission;
        em.rateOverTime = 7f;
        var sh = fire.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.22f;
        var vel = fire.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = 0f;
        vel.y = 0.45f;
        vel.z = 0f;
        Alpha(fire, false, new Vector2(0f, 0f), new Vector2(0.12f, 1f), new Vector2(0.75f, 1f), new Vector2(1f, 0f));
        SizeCurve(fire, new Vector2(0f, 0.6f), new Vector2(0.2f, 1f), new Vector2(1f, 0.75f));
        Sheet(fire, 14f);

        var embers = NewSystem("Embers", root.transform, fx["Dot"], out _);
        m = embers.main;
        m.duration = 1f;
        m.startLifetime = new MMC(0.6f, 1.0f);
        m.startSpeed = new MMC(0.8f, 1.6f);
        m.startSize = new MMC(0.05f, 0.09f);
        m.startColor = new Color(1f, 0.55f, 0.15f);
        m.maxParticles = 12;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        em = embers.emission;
        em.rateOverTime = 4f;
        sh = embers.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 15f;
        sh.radius = 0.25f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        Alpha(embers, true, SharpFade);
        return root;
    }

    static GameObject ExampleHit(Dictionary<string, Material> fx)
    {
        var root = new GameObject("FX_HitSparks");
        var flash = NewSystem("Impact", root.transform, fx["FB_Impact_4x4"], out _);
        var m = flash.main;
        m.duration = 1.2f;
        m.startLifetime = 0.3f;
        m.startSize = 1.0f;
        m.startColor = new Color(1f, 0.92f, 0.6f);
        m.maxParticles = 4;
        Burst(flash, 0f, 1);
        Sheet(flash);

        var sparks = NewSystem("Sparks", root.transform, fx["Spark"], out var r);
        m = sparks.main;
        m.duration = 1.2f;
        m.startLifetime = new MMC(0.18f, 0.32f);
        m.startSpeed = new MMC(4f, 7f);
        m.startSize = new MMC(0.25f, 0.4f);
        m.startColor = new Color(1f, 0.9f, 0.5f);
        m.gravityModifier = 1f;
        m.maxParticles = 20;
        Burst(sparks, 0f, 9);
        var sh = sparks.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Hemisphere;
        sh.radius = 0.1f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.5f;
        Alpha(sparks, true, SharpFade);
        return root;
    }

    static GameObject ExampleHeal(Dictionary<string, Material> fx)
    {
        var root = new GameObject("FX_Heal");
        var green = new Color(0.45f, 1f, 0.5f);

        var pillar = NewSystem("Pillar", root.transform, fx["Mask_Beam"], out var r);
        var m = pillar.main;
        m.duration = 1.6f;
        m.startLifetime = 0.9f;
        m.startSize3D = true;
        m.startSizeX = 1.4f;
        m.startSizeY = 1.7f;
        m.startSizeZ = 1.4f;
        m.startColor = green;
        m.maxParticles = 2;
        Burst(pillar, 0f, 1);
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FxDir + "/Meshes/SM_FX_Cylinder.fbx");
        r.alignment = ParticleSystemRenderSpace.World;
        var sol = pillar.sizeOverLifetime;
        sol.enabled = true;
        sol.separateAxes = true;
        sol.x = new MMC(1f, AnimationCurve.Constant(0f, 1f, 1f));
        sol.y = new MMC(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.18f, 1f), new Keyframe(1f, 1f)));
        sol.z = new MMC(1f, AnimationCurve.Constant(0f, 1f, 1f));
        Alpha(pillar, false, new Vector2(0f, 0f), new Vector2(0.12f, 1f), new Vector2(0.6f, 1f), new Vector2(1f, 0f));

        var plus = NewSystem("Plus", root.transform, fx["Plus"], out _);
        m = plus.main;
        m.duration = 1.6f;
        m.startLifetime = new MMC(0.6f, 0.9f);
        m.startSpeed = new MMC(0.8f, 1.5f);
        m.startSize = new MMC(0.2f, 0.35f);
        m.startColor = green;
        m.maxParticles = 12;
        Burst(plus, 0.05f, 7);
        var sh = plus.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 5f;
        sh.radius = 0.45f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        Alpha(plus, true, SharpFade);

        var glow = NewSystem("GroundGlow", root.transform, fx["GlowSmooth"], out var rg);
        glow.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        m = glow.main;
        m.duration = 1.6f;
        m.startLifetime = 0.9f;
        m.startSize = 2.2f;
        m.startColor = green;
        m.maxParticles = 2;
        Burst(glow, 0f, 1);
        rg.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        Alpha(glow, false, new Vector2(0f, 0f), new Vector2(0.15f, 0.7f), new Vector2(1f, 0f));
        return root;
    }

    // ------------------------------------------------------------------ сцена-витрина
    const float HallZ = -30f, HallExitZ = -20.5f, PortalZ = 16f, RoadEndZ = 12.5f;

    static float RoadX(float z) => 6f * Mathf.Sin(Mathf.PI * (z - HallExitZ) / (RoadEndZ - HallExitZ));

    static void BuildShowcase(Dictionary<string, GameObject> P, Dictionary<string, GameObject> examples,
                              Dictionary<string, Material> fx)
    {
        EnsureFolder(SceneDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var rng = new System.Random(7);
        var sky = new Color(0.56f, 0.68f, 0.74f);

        var lighting = new LightingSettings { bakedGI = false, realtimeGI = false };
        AssetDatabase.CreateAsset(lighting, SceneDir + "/ArtShowcase_Lighting.lighting");
        Lightmapping.lightingSettings = lighting;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.6f);
        RenderSettings.fog = false;   // вместо серой дымки - облачка и цветокор ArcaneLook (в конце BuildShowcase)

        // солнце - как в превью Blender: спереди-слева от камеры
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.97f, 0.92f);
        sun.intensity = 1f;
        sun.shadows = LightShadows.Hard;
        sun.transform.rotation = Quaternion.LookRotation(-new Vector3(0.62f, 0.85f, 0.48f).normalized);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = sky;
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 300f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0f, 56f, 44f);
        camGo.transform.LookAt(new Vector3(0f, 0f, -8f));

        var map = new GameObject("Map").transform;
        var ground = Group("Ground", map);
        var road = Group("Road", map);
        var buildings = Group("Buildings", map);
        var nature = Group("Nature", map);
        var units = new GameObject("Units").transform;
        var effects = new GameObject("Effects").transform;

        // земля 7 x 5 тайлов по 24 м
        for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 1; j++)
                Place(P["SM_Ground_Tile"], ground, new Vector3(i * 24f, 0f, j * 24f), 0f);

        Place(P["SM_TownHall"], buildings, new Vector3(0f, 0f, HallZ), 0f);
        Place(P["SM_Portal"], buildings, new Vector3(0f, 0f, PortalZ), 180f);

        // дорога по S-кривой от портала к воротам ратуши
        var path = new List<Vector3>();
        for (int k = 0; k <= 400; k++)
        {
            float z = Mathf.Lerp(RoadEndZ, HallExitZ, k / 400f);
            path.Add(new Vector3(RoadX(z), 0f, z));
        }
        var tiles = Enumerable.Range(1, 8).Select(i => P["SM_RoadTile_0" + i]).ToArray();
        var tb = PrefabBounds(tiles[0]);
        float tileLen = Mathf.Max(tb.size.x, tb.size.z) * 0.98f;
        int across = Mathf.Max(1, Mathf.RoundToInt(3.2f / tileLen));
        float walked = 0f;
        for (int k = 1; k < path.Count; k++)
        {
            walked += Vector3.Distance(path[k], path[k - 1]);
            if (walked < tileLen && k != 1) continue;
            walked = 0f;
            var dir = (path[k] - path[k - 1]).normalized;
            var side = Vector3.Cross(Vector3.up, dir);
            float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
            for (int a = 0; a < across; a++)
            {
                float off = (a - (across - 1) * 0.5f) * tileLen;
                var tile = tiles[rng.Next(tiles.Length)];
                Place(tile, road, path[k] + side * off, yaw + 90f * rng.Next(4));
            }
        }

        // скалы - дугой по краям и сзади
        for (int k = 0; k < 16; k++)
        {
            float ang = Mathf.Lerp(-200f, 20f, k / 15f) * Mathf.Deg2Rad;
            float rad = 50f + (float)rng.NextDouble() * 5f;
            var pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad - 14f);
            float yaw = Mathf.Atan2(-pos.x, -(pos.z + 14f)) * Mathf.Rad2Deg + Rand(rng, -30f, 30f);
            Place(P["SM_Cliff_0" + (k % 8 + 1)], nature, pos, yaw, Rand(rng, 1f, 1.3f));
        }

        // деревья, кусты, камни - вне дороги и площадок
        var taken = new List<Vector3>();
        Scatter(rng, P, "SM_Tree_0", 8, 34, 4.0f, 6f, nature, path, taken, 0.9f, 1.2f);
        Scatter(rng, P, "SM_Bush_0", 8, 44, 2.0f, 3.5f, nature, path, taken, 0.9f, 1.25f);
        Scatter(rng, P, "SM_Rock_0", 4, 12, 2.5f, 4f, nature, path, taken, 0.8f, 1.3f);
        Place(P["SM_Tombstone_01"], nature, new Vector3(-11f, 0f, -12f), 15f);
        Place(P["SM_Tombstone_02"], nature, new Vector3(-12.6f, 0f, -13.4f), -10f);

        // оружие и снаряды - стойкой у площади
        string[] weapons = { "SM_Sword", "SM_Bow", "SM_Staff", "SM_Wand", "SM_Arrow", "SM_MagicBall" };
        for (int k = 0; k < weapons.Length; k++)
        {
            if (!P.ContainsKey(weapons[k])) continue;
            var w = Place(P[weapons[k]], buildings, new Vector3(9f + k * 0.9f, 0f, -17f), 0f);
            var b = BoundsOf(w);
            w.transform.position += Vector3.up * (0.05f - b.min.y);
        }

        // герои у ворот + выделение и радиусы под центральным
        var gnome = P["SM_Hero_Gnome"];
        var g0 = Place(gnome, units, new Vector3(-2.4f, 0f, -16.8f), 0f);
        var g1 = Place(gnome, units, new Vector3(0f, 0f, -16f), 0f);
        var g2 = Place(gnome, units, new Vector3(2.4f, 0f, -16.8f), 0f);
        var g3 = Place(gnome, units, new Vector3(RoadX(-2f), 0f, -2f), 0f);
        var quad = DecalQuad();
        Decal(quad, fx["Decal_Selection"], new Color(0.45f, 1f, 0.5f), g1.transform.position, 1.5f, 0.06f, effects);
        Decal(quad, fx["Decal_Range"], new Color(0.45f, 0.75f, 1f, 0.9f), g1.transform.position, 8f, 0.04f, effects);
        Decal(quad, fx["Decal_RangeDashed"], new Color(1f, 0.45f, 0.35f, 0.9f), g1.transform.position, 5f, 0.05f, effects);

        // примеры частиц
        PlaceFx(examples["FX_Heal"], effects, g0.transform.position);
        PlaceFx(examples["FX_HitSparks"], effects, g2.transform.position + new Vector3(0.3f, 0.75f, 0.45f));
        PlaceFx(examples["FX_Burning"], effects, g3.transform.position + new Vector3(-0.35f, 0.75f, 0.45f));

        foreach (Transform t in map) MarkStatic(t.gameObject);

        EditorSceneManager.SaveScene(scene, ScenePath);
        ArcaneLook.SetupScene(scene, null);   // Arcane: свет, Volume, атмосфера, облачка (нужен путь сцены)
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[ArtSetup] сцена сохранена: " + ScenePath);
    }

    static void Scatter(System.Random rng, Dictionary<string, GameObject> P, string prefix, int variants, int count,
                        float spacing, float roadMargin, Transform parent, List<Vector3> road, List<Vector3> taken,
                        float sMin, float sMax)
    {
        int placed = 0;
        for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
        {
            var pos = new Vector3(Rand(rng, -48f, 48f), 0f, Rand(rng, -58f, 22f));
            if (road.Any(p => (p - pos).sqrMagnitude < roadMargin * roadMargin)) continue;
            if ((pos - new Vector3(0f, 0f, HallZ)).magnitude < 15f) continue;          // ратуша и площадь
            if ((pos - new Vector3(0f, 0f, PortalZ)).magnitude < 8f) continue;         // портал
            if (pos.x > 7f && pos.x < 16f && pos.z > -20f && pos.z < -13f) continue;   // стойка с оружием
            if (pos.x < -8f && pos.x > -15f && pos.z > -16f && pos.z < -9f) continue;  // надгробия
            if (taken.Any(p => (p - pos).sqrMagnitude < spacing * spacing)) continue;
            taken.Add(pos);
            string key = prefix + (rng.Next(variants) + 1);
            if (!P.ContainsKey(key)) continue;
            Place(P[key], parent, pos, Rand(rng, 0f, 360f), Rand(rng, sMin, sMax));
            placed++;
        }
    }

    // ------------------------------------------------------------------ помощники
    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale = 1f)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static void PlaceFx(GameObject prefab, Transform parent, Vector3 pos)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = pos;
    }

    static void Decal(Mesh quad, Material mat, Color tint, Vector3 pos, float size, float lift, Transform parent)
    {
        var go = new GameObject("Decal_" + mat.name.Replace("M_FX_", ""));
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(pos.x, lift, pos.z);
        go.transform.localScale = new Vector3(size, 1f, size);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TintedCopy(mat, tint);   // MaterialPropertyBlock в сцену не сохраняется
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// Копия материала частиц с цветом (_BaseColor) - для декалей на MeshRenderer.
    static Material TintedCopy(Material src, Color tint)
    {
        string dir = FxMatDir + "/Tinted";
        EnsureFolder(dir);
        string hex = ColorUtility.ToHtmlStringRGBA(tint);
        string path = dir + "/" + src.name + "_" + hex + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(src);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.CopyPropertiesFromMaterial(src);
        m.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Плоский квад для декалей с белыми vertex color (частичные шейдеры умножают на vertex color).
    static Mesh DecalQuad()
    {
        string path = FxDir + "/Meshes/SM_FX_DecalQuad.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        mesh = new Mesh { name = "SM_FX_DecalQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
        mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
        mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Bounds PrefabBounds(GameObject prefab)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var b = BoundsOf(go);
        Object.DestroyImmediate(go);
        return b;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void MarkStatic(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponentInParent<Animator>() != null) continue;   // портал анимирован
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }

    static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
