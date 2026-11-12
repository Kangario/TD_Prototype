// Контроллеры анимаций юнитов (TDD: один базовый Animator + Override Controller на каждый вид оружия).
//   Unit_Base.controller - слоты-заглушки Slot_*, древа смешивания, переходы, параметры (API - UnitAnimator):
//     Base Layer: Locomotion = 1D MoveBlend [Idle (1D IdleVariant) @0, Walk @1, Run @2], темп LocoSpeed
//                 Attack = 1D AttackVariant, темп AttackSpeed;  Cast = 1D CastType, темп CastSpeed
//                 WeaponSwap, Emote, Death = 1D DeathVariant (bool Dead; снятие Dead возвращает в Locomotion)
//     Hit (Additive, маска UpperBody): Empty <-> Hit = 1D HitVariant - вздрагивание поверх бега и атаки
//   Unit_<Набор>.overrideController - клипы набора в слотах (таблица Sets ниже)
//   Sets/<Набор>.asset (UnitAnimationSet) - Override + замеренная родная скорость Walk/Run; его и берёт UnitAnimator
// Меню: Tools > P1 Characters > Rebuild Animation Controllers. Повторный запуск безопасен: базовый пересобирается,
//   заглушки слотов сохраняются; в существующих Override заполняются только пустые слоты (ручные замены
//   в инспекторе не затираются). Вернуть набор к таблице - удалить его .overrideController и запустить меню.
// Batchmode: -executeMethod AnimationSetup.RunBatch ; проверка и снимки: -executeMethod AnimationSetup.VerifyBatch
//   (папка снимков - переменная P1_SNAPSHOT_DIR, по умолчанию Logs/)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AnimationSetup
{
    const string AnimDir = "Assets/Characters/Animations";
    const string CtrlDir = AnimDir + "/Controllers";
    const string SetDir = AnimDir + "/Sets";
    public const string BasePath = CtrlDir + "/Unit_Base.controller";
    const string MaskPath = CtrlDir + "/UpperBody.mask";
    const string OldController = AnimDir + "/HumanAnimator.controller";
    const string OldIdleClip = MixamoImport.ClipDir + "X Bot_Breathing Idle.fbx";
    public const string AA = "Assets/Characters/AA_Universal";
    public const string LineupScene = "Assets/Scenes/CharacterLineup.unity";
    const string ShowcaseScene = "Assets/Scenes/ArtShowcase.unity";
    public const string DefaultSet = "Sword";   // TDD: новый герой получает меч

    // ------------------------------------------------------------------ наборы: слот -> клип (имя файла без .fbx)
    static Dictionary<string, string> Common() => new Dictionary<string, string>
    {
        ["Idle_1"] = "Common_Idle_01", ["Idle_2"] = "Common_Idle_02",
        ["Walk"] = "Common_Walk_01", ["Run"] = "Common_Run_01",
        ["Attack_1"] = "Fists_Attack_01", ["Attack_2"] = "Fists_Attack_01", ["Attack_3"] = "Fists_Attack_01",
        ["Cast_1"] = "Hero_Cast_01", ["Cast_2"] = "Hero_Cast_01", ["Cast_3"] = "Hero_Cast_01",
        ["Hit_1"] = "Common_Hit_01", ["Hit_2"] = "Common_Hit_02", ["Hit_3"] = "Common_Hit_03",
        ["Death_1"] = "Common_Death_01", ["Death_2"] = "Common_Death_02", ["Death_3"] = "Common_Death_03",
        ["WeaponSwap"] = "Hero_WeaponSwap_01", ["Emote"] = "Hero_Cast_01",
    };

    static Dictionary<string, string> With(Dictionary<string, string> from, params string[] kv)
    {
        var d = new Dictionary<string, string>(from);
        for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
        return d;
    }

    public static Dictionary<string, Dictionary<string, string>> Sets()
    {
        var swordBody = With(Common(),
            "Idle_1", "Sword_Idle_01", "Idle_2", "Sword_Idle_01", "Walk", "Sword_Walk_01", "Run", "Sword_Run_01",
            "Hit_1", "Sword_Hit_01", "Death_1", "Sword_Death_01",
            "Cast_1", "Sword_Cast_01", "Cast_2", "Sword_Cast_01", "Cast_3", "Sword_Cast_01");
        var magicBody = With(Common(),
            "Idle_1", "Magic_Idle_01", "Idle_2", "Magic_Idle_02", "Walk", "Magic_Walk_01", "Run", "Magic_Run_01");
        var club = With(swordBody,
            "Attack_1", "Sword_Attack_01", "Attack_2", "Sword_Attack_03", "Attack_3", "Sword_Attack_01", "Emote", "Boss_Roar_01");
        return new Dictionary<string, Dictionary<string, string>>
        {
            // герои + солдат
            ["Sword"] = With(swordBody,
                "Attack_1", "Sword_Attack_01", "Attack_2", "Sword_Attack_02", "Attack_3", "Sword_Attack_03", "Emote", "Sword_Cast_01"),
            ["Bow"] = With(Common(),
                "Idle_1", "Bow_Idle_01", "Idle_2", "Bow_Idle_02", "Walk", "Bow_Walk_01", "Run", "Bow_Run_01",
                "Attack_1", "Bow_Attack_01", "Attack_2", "Bow_Attack_01", "Attack_3", "Bow_Attack_01",
                "Hit_1", "Bow_Hit_01", "Death_1", "Bow_Death_01"),
            ["Staff"] = With(magicBody,
                "Attack_1", "Staff_Attack_01", "Attack_2", "Staff_Attack_02", "Attack_3", "Staff_Attack_03",
                "Cast_1", "Staff_Cast_01", "Cast_2", "Staff_Cast_02", "Cast_3", "Staff_Cast_02", "Emote", "Staff_Cast_02"),
            ["Wand"] = With(magicBody,
                "Attack_1", "Wand_Attack_01", "Attack_2", "Wand_Attack_02", "Attack_3", "Wand_Attack_03"),
            // враги: бегун, громила, босс (солдат = Sword)
            ["Fists"] = With(Common(),
                "Idle_1", "Fists_Idle_01", "Idle_2", "Fists_Idle_01", "Walk", "Fists_Walk_01", "Run", "Fists_Run_01",
                "Cast_1", "Boss_Roar_01", "Cast_2", "Boss_Roar_01", "Cast_3", "Boss_Roar_01",
                "Death_1", "Fists_Death_01", "Death_2", "Fists_Death_02", "Emote", "Boss_Roar_01"),
            ["Club"] = club,
            ["Boss"] = With(club, "Cast_1", "Boss_Stomp_01", "Cast_2", "Boss_Stomp_01", "Cast_3", "Boss_Stomp_01"),
        };
    }

    // ------------------------------------------------------------------ сборка
    [MenuItem("Tools/P1 Characters/Rebuild Animation Controllers", priority = 10)]
    public static void SetupAll()
    {
        CharacterSetup.EnsureFolder(CtrlDir);
        CharacterSetup.EnsureFolder(MixamoImport.ClipDir + "Common");
        CharacterSetup.Move(OldIdleClip, MixamoImport.ClipDir + "Common/Common_Idle_01.fbx");
        if (AssetDatabase.LoadMainAssetAtPath(BasePath) == null) CharacterSetup.Move(OldController, BasePath);
        ReconfigureClips();
        var mask = BuildMask();
        var ctrl = BuildBase(mask, out var slots);
        var overrides = BuildOverrides(ctrl, slots);
        AssetDatabase.SaveAssets();
        var sets = BuildSets(overrides);
        AssetDatabase.SaveAssets();
        foreach (var c in CharacterSetup.Characters) AssignToPrefab(AA + "/Prefabs/" + c + ".prefab", sets[DefaultSet]);
        RetargetScene(ShowcaseScene, ctrl, overrides[DefaultSet]);
        BuildLineup(sets);
        AssetDatabase.SaveAssets();
        Debug.Log("[AnimSetup] OK: " + BasePath + " + наборы " + string.Join(", ", sets.Keys));
    }

    public static void RunBatch()
    {
        try { SetupAll(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    /// Клипы с устаревшими настройками (переименованный файл, старое запекание XZ) приводит к MixamoImport.ConfigureClips.
    static void ReconfigureClips()
    {
        var avatar = MixamoImport.LoadAvatar();
        int total = 0, changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { MixamoImport.ClipDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
            total++;
            var have = mi.clipAnimations;
            string name = MixamoImport.ClipName(path);
            bool stale = have.Length == 0 || have[0].name != name || have.Any(c => c.lockRootPositionXZ)
                         || mi.animationType != ModelImporterAnimationType.Human
                         || mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther;
            if (!stale) continue;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = avatar;
            mi.clipAnimations = MixamoImport.ConfigureClips(mi, mi.defaultClipAnimations, path);
            mi.SaveAndReimport();
            changed++;
        }
        Debug.Log("[AnimSetup] клипов Mixamo: " + total + ", перенастроено: " + changed);
    }

    static AvatarMask BuildMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head ||
                                              p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm ||
                                              p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static AnimatorController BuildBase(AvatarMask mask, out Dictionary<string, AnimationClip> slots)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BasePath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(BasePath);

        // заглушки слотов живут внутри контроллера; на них ссылаются Override, поэтому не пересоздаются
        var subs = AssetDatabase.LoadAllAssetsAtPath(BasePath);
        slots = new Dictionary<string, AnimationClip>();
        foreach (var s in UnitAnimator.Slots)
        {
            string n = UnitAnimator.SlotPrefix + s;
            var clip = subs.OfType<AnimationClip>().FirstOrDefault(c => c.name == n);
            if (clip == null)
            {
                clip = new AnimationClip { name = n };
                AssetDatabase.AddObjectToAsset(clip, ctrl);
            }
            slots[s] = clip;
        }

        // всё остальное пересобираем с нуля
        while (ctrl.layers.Length > 0) ctrl.RemoveLayer(0);
        while (ctrl.parameters.Length > 0) ctrl.RemoveParameter(0);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(BasePath))
            if (o != null && o != ctrl && !(o is AnimationClip c && c.name.StartsWith(UnitAnimator.SlotPrefix)))
                Object.DestroyImmediate(o, true);

        foreach (var n in new[] { "MoveBlend", "IdleVariant", "AttackVariant", "CastType", "HitVariant", "DeathVariant" })
            ctrl.AddParameter(n, AnimatorControllerParameterType.Float);
        foreach (var n in new[] { "LocoSpeed", "AttackSpeed", "CastSpeed" })
            ctrl.AddParameter(new AnimatorControllerParameter { name = n, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        foreach (var n in new[] { "Attack", "Cast", "Hit", "WeaponSwap", "Emote" })
            ctrl.AddParameter(n, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        // ---- Base Layer
        ctrl.AddLayer("Base Layer");
        var sm = ctrl.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(40, 200);
        sm.entryPosition = new Vector3(40, 60);
        sm.exitPosition = new Vector3(900, 60);
        var idle = Tree(ctrl, "Idle", "IdleVariant", (slots["Idle_1"], 0f), (slots["Idle_2"], 1f));
        var loco = Tree(ctrl, "Locomotion", "MoveBlend", (idle, 0f), (slots["Walk"], 1f), (slots["Run"], 2f));
        var sLoco = State(sm, "Locomotion", loco, new Vector3(300, 60), "LocoSpeed");
        sm.defaultState = sLoco;
        var sAttack = State(sm, "Attack", Tree(ctrl, "Attack", "AttackVariant",
            (slots["Attack_1"], 0f), (slots["Attack_2"], 1f), (slots["Attack_3"], 2f)), new Vector3(620, -60), "AttackSpeed");
        var sCast = State(sm, "Cast", Tree(ctrl, "Cast", "CastType",
            (slots["Cast_1"], 0f), (slots["Cast_2"], 1f), (slots["Cast_3"], 2f)), new Vector3(620, 20), "CastSpeed");
        var sSwap = State(sm, "WeaponSwap", slots["WeaponSwap"], new Vector3(620, 100), null);
        var sEmote = State(sm, "Emote", slots["Emote"], new Vector3(620, 180), null);
        var sDeath = State(sm, "Death", Tree(ctrl, "Death", "DeathVariant",
            (slots["Death_1"], 0f), (slots["Death_2"], 1f), (slots["Death_3"], 2f)), new Vector3(300, 300), null);

        var die = sm.AddAnyStateTransition(sDeath);   // первым: смерть важнее всего
        die.hasExitTime = false; die.hasFixedDuration = true; die.duration = 0.15f; die.canTransitionToSelf = false;
        die.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
        Any(sm, sAttack, "Attack", 0.08f, true);
        Any(sm, sCast, "Cast", 0.12f, false);
        Any(sm, sSwap, "WeaponSwap", 0.12f, false);
        Any(sm, sEmote, "Emote", 0.15f, false);
        foreach (var s in new[] { sAttack, sCast, sSwap, sEmote }) Back(s, sLoco, 0.9f, 0.15f);
        var revive = sDeath.AddTransition(sLoco);
        revive.hasExitTime = false; revive.hasFixedDuration = true; revive.duration = 0.25f;
        revive.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

        // ---- Hit: аддитивно и только верх тела - вздрагивание не ломает бег и атаку
        var hsm = new AnimatorStateMachine { name = "Hit", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(hsm, ctrl);
        ctrl.AddLayer(new AnimatorControllerLayer
        {
            name = "Hit", stateMachine = hsm, defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Additive, avatarMask = mask,
        });
        hsm.anyStatePosition = new Vector3(40, 200);
        var empty = hsm.AddState("Empty", new Vector3(300, 60));
        hsm.defaultState = empty;
        var sHit = State(hsm, "Hit", Tree(ctrl, "Hit", "HitVariant",
            (slots["Hit_1"], 0f), (slots["Hit_2"], 1f), (slots["Hit_3"], 2f)), new Vector3(300, 200), null);
        Any(hsm, sHit, "Hit", 0.05f, true);
        Back(sHit, empty, 0.85f, 0.15f);

        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    static BlendTree Tree(AnimatorController ctrl, string name, string param, params (Motion motion, float threshold)[] children)
    {
        var bt = new BlendTree
        {
            name = name, blendType = BlendTreeType.Simple1D, blendParameter = param,
            useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(bt, ctrl);
        foreach (var (motion, threshold) in children) bt.AddChild(motion, threshold);
        return bt;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, Vector3 pos, string speedParam)
    {
        var s = sm.AddState(name, pos);
        s.motion = motion;
        if (speedParam != null)
        {
            s.speedParameterActive = true;
            s.speedParameter = speedParam;
        }
        return s;
    }

    static void Any(AnimatorStateMachine sm, AnimatorState dst, string trigger, float duration, bool toSelf)
    {
        var t = sm.AddAnyStateTransition(dst);
        t.hasExitTime = false; t.hasFixedDuration = true; t.duration = duration; t.canTransitionToSelf = toSelf;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
    }

    static void Back(AnimatorState from, AnimatorState to, float exitTime, float duration)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true; t.exitTime = exitTime; t.hasFixedDuration = true; t.duration = duration;
    }

    static Dictionary<string, AnimatorOverrideController> BuildOverrides(AnimatorController ctrl, Dictionary<string, AnimationClip> slots)
    {
        var result = new Dictionary<string, AnimatorOverrideController>();
        var common = Common();
        foreach (var set in Sets())
        {
            string path = CtrlDir + "/Unit_" + set.Key + ".overrideController";
            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(ctrl) { name = "Unit_" + set.Key };
                AssetDatabase.CreateAsset(oc, path);
            }
            else if (oc.runtimeAnimatorController != ctrl) oc.runtimeAnimatorController = ctrl;

            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(oc.overridesCount);
            oc.GetOverrides(pairs);
            int filled = 0, kept = 0;
            var missing = new List<string>();
            for (int i = 0; i < pairs.Count; i++)
            {
                var key = pairs[i].Key;
                if (key == null || !key.name.StartsWith(UnitAnimator.SlotPrefix)) continue;
                if (pairs[i].Value != null) { kept++; continue; }
                string slot = key.name.Substring(UnitAnimator.SlotPrefix.Length);
                var clip = LoadClip(set.Value[slot]) ?? LoadClip(common[slot]);
                if (clip == null) { missing.Add(slot + "=" + set.Value[slot]); continue; }
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, clip);
                filled++;
            }
            oc.ApplyOverrides(pairs);
            EditorUtility.SetDirty(oc);
            result[set.Key] = oc;
            Debug.Log("[AnimSetup] Unit_" + set.Key + ": заполнено " + filled + ", оставлено как было " + kept +
                      (missing.Count > 0 ? ", НЕТ КЛИПОВ: " + string.Join(", ", missing) : ""));
        }
        return result;
    }

    /// UnitAnimationSet на каждый Override + родная скорость Walk/Run, замеренная прогоном root motion на X Bot.
    static Dictionary<string, UnitAnimationSet> BuildSets(Dictionary<string, AnimatorOverrideController> overrides)
    {
        CharacterSetup.EnsureFolder(SetDir);
        var result = new Dictionary<string, UnitAnimationSet>();
        var probe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MixamoImport.AvatarModel));
        probe.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var a = probe.GetComponent<Animator>();
            a.applyRootMotion = true;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var kv in overrides)
            {
                string path = SetDir + "/" + kv.Key + ".asset";
                var set = AssetDatabase.LoadAssetAtPath<UnitAnimationSet>(path);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<UnitAnimationSet>();
                    AssetDatabase.CreateAsset(set, path);
                }
                set.controller = kv.Value;
                a.runtimeAnimatorController = kv.Value;
                set.walkSpeed = MeasureSpeed(a, "Walk", 1f);
                set.runSpeed = MeasureSpeed(a, "Run", 2f);
                EditorUtility.SetDirty(set);
                result[kv.Key] = set;
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[AnimSetup] Sets/{0}: шаг {1:0.00} м/с, бег {2:0.00} м/с (при humanScale 1)", kv.Key, set.walkSpeed, set.runSpeed));
            }
        }
        finally { Object.DestroyImmediate(probe); }
        return result;
    }

    /// Скорость, с которой Animator двигал бы персонажа на клипе слота (целое число циклов, не меньше 2 с), / humanScale.
    public static float MeasureSpeed(Animator a, string slot, float moveBlend)
    {
        var oc = (AnimatorOverrideController)a.runtimeAnimatorController;
        var clip = oc[UnitAnimator.SlotPrefix + slot];
        float cycle = clip != null && clip.length > 0.1f ? clip.length : 1f;
        float time = cycle * Mathf.Max(1f, Mathf.Ceil(2f / cycle));
        a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        a.Rebind();
        a.SetFloat("MoveBlend", moveBlend);
        a.Update(0f);
        var p0 = a.transform.position;
        int steps = Mathf.CeilToInt(time / 0.02f);
        for (int k = 0; k < steps; k++) a.Update(time / steps);
        var d = a.transform.position - p0;
        return new Vector2(d.x, d.z).magnitude / time / a.humanScale;
    }

    public static AnimationClip LoadClip(string name)
    {
        string folder = name.Split('_')[0];
        var clips = MixamoImport.LoadClips(MixamoImport.ClipDir + folder + "/" + name + ".fbx");
        return clips.FirstOrDefault(c => c.name == name) ?? clips.FirstOrDefault();
    }

    // ------------------------------------------------------------------ персонажи и сцены
    public static void AssignToPrefab(string path, UnitAnimationSet set)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var anim = root.GetComponent<Animator>();
            anim.runtimeAnimatorController = set.controller;
            anim.applyRootMotion = false;
            var ua = root.GetComponent<UnitAnimator>();
            if (ua == null) ua = root.AddComponent<UnitAnimator>();
            ua.animationSet = set;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// В сцене все Animator с базовым контроллером (сам по себе он - пустые слоты) получают набор по умолчанию.
    static void RetargetScene(string scenePath, RuntimeAnimatorController from, RuntimeAnimatorController to)
    {
        if (!File.Exists(scenePath)) return;
        var scene = SceneManager.GetSceneByPath(scenePath);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen)
        {
            try { scene = EditorSceneManager.OpenScene(scenePath, Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive); }
            catch (System.Exception e) { Debug.LogWarning("[AnimSetup] не открыть " + scenePath + ": " + e.Message); return; }
        }
        int n = 0;
        foreach (var a in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Animator>(true)))
            if (a.runtimeAnimatorController == from) { a.runtimeAnimatorController = to; a.applyRootMotion = false; n++; }
        if (n > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasOpen) EditorSceneManager.SaveScene(scene);
            Debug.Log("[AnimSetup] " + scenePath + ": Animator на базовом контроллере -> " + to.name + ": " + n);
        }
        if (!wasOpen && !Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
    }

    // персонаж, набор, интервал атаки, скорость шага и бега для витрины
    static readonly (string model, string set, float interval, float walk, float run)[] Lineup =
    {
        ("X Bot", "Fists", 0.8f, 1.2f, 2.5f),
        ("M_AA_001", "Sword", 1.0f, 1.2f, 3.0f),
        ("F_AA_001", "Bow", 0.7f, 1.2f, 3.0f),
        ("F_AA_002", "Staff", 1.4f, 1.2f, 3.0f),
        ("UniversalFemale", "Wand", 1.2f, 1.2f, 3.0f),
        ("M_AA_002", "Club", 1.6f, 1.0f, 2.0f),
        ("UniversalMale", "Boss", 1.6f, 0.8f, 1.6f),
    };

    static void BuildLineup(Dictionary<string, UnitAnimationSet> sets)
    {
        var prevActive = EditorSceneManager.GetActiveScene();
        bool untitled = Enumerable.Range(0, EditorSceneManager.sceneCount)
            .Any(i => string.IsNullOrEmpty(EditorSceneManager.GetSceneAt(i).path));
        if (untitled && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(scene);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.47f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.22f);
        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.17f, 0.19f, 0.23f);
        cam.fieldOfView = 30f;
        cam.transform.position = new Vector3(0f, 1.4f, 11f);
        cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));

        const float spacing = 1.15f;
        for (int i = 0; i < Lineup.Length; i++)
        {
            var e = Lineup[i];
            string modelPath = e.model == "X Bot" ? MixamoImport.AvatarModel : AA + "/Prefabs/" + e.model + ".prefab";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new System.Exception("нет " + modelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            go.name = e.model + " - " + e.set;
            go.transform.position = new Vector3(((Lineup.Length - 1) * 0.5f - i) * spacing, 0f, 0f);   // слева направо с камеры
            var anim = go.GetComponent<Animator>();
            anim.runtimeAnimatorController = sets[e.set].controller;
            anim.applyRootMotion = false;
            var ua = go.GetComponent<UnitAnimator>();
            if (ua == null) ua = go.AddComponent<UnitAnimator>();
            ua.animationSet = sets[e.set];
            var demo = go.AddComponent<UnitAnimatorDemo>();
            demo.attackInterval = e.interval;
            demo.walkSpeed = e.walk;
            demo.runSpeed = e.run;
        }

        ArcaneLook.SetupScene(scene, null);   // Arcane-свет, Volume, атмосфера (Assets/Art/Editor/ArcaneLook.cs)
        EditorSceneManager.SaveScene(scene, LineupScene);
        if (!untitled)
        {
            EditorSceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log("[AnimSetup] сцена " + LineupScene + ": " + Lineup.Length + " персонажей, в Play показывают свои наборы");
    }

    // ------------------------------------------------------------------ проверка
    public static void VerifyBatch()
    {
        try
        {
            string dir = System.Environment.GetEnvironmentVariable("P1_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetFullPath("Logs");
            var ci = System.Globalization.CultureInfo.InvariantCulture;

            foreach (var path in AssetDatabase.FindAssets("t:AnimatorOverrideController", new[] { CtrlDir }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(oc.overridesCount);
                oc.GetOverrides(pairs);
                var empty = pairs.Where(p => p.Value == null).Select(p => p.Key.name).ToList();
                string Info(string slot)
                {
                    var c = pairs.First(p => p.Key.name == UnitAnimator.SlotPrefix + slot).Value;
                    return c == null ? slot + "=?" : string.Format(ci, "{0}={1}({2:0.00}s{3})", slot, c.name, c.length,
                        c.isLooping ? " loop" : "");
                }
                Debug.Log("[AnimVerify] " + oc.name + ": слотов " + pairs.Count + ", пустых " + empty.Count +
                          (empty.Count > 0 ? " (" + string.Join(",", empty) + ")" : "") + " | " +
                          string.Join(" ", new[] { "Idle_1", "Walk", "Run", "Attack_1", "Cast_1", "Death_1" }.Select(Info)));
            }

            EditorSceneManager.OpenScene(LineupScene);
            var units = Object.FindObjectsByType<UnitAnimator>().OrderBy(u => -u.transform.position.x).ToArray();
            var anims = units.Select(u => u.GetComponent<Animator>()).ToArray();
            foreach (var a in anims) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // в batchmode без кадров скиннинг иначе берётся с прошлого рендера
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;

            // родная скорость на этом персонаже: замер root motion против предсказания набора (скорость x humanScale)
            foreach (var u in units)
            {
                var a = u.GetComponent<Animator>();
                var start = a.transform.position;
                a.applyRootMotion = true;
                float walk = MeasureSpeed(a, "Walk", 1f) * a.humanScale, run = MeasureSpeed(a, "Run", 2f) * a.humanScale;
                a.applyRootMotion = false;
                a.transform.SetPositionAndRotation(start, Quaternion.identity);
                Debug.Log(string.Format(ci, "[AnimVerify] {0} (humanScale {1:0.000}): шаг замер {2:0.00} / набор {3:0.00} м/с, бег замер {4:0.00} / набор {5:0.00} м/с",
                    a.name, a.humanScale, walk, u.animationSet.walkSpeed * a.humanScale, run, u.animationSet.runSpeed * a.humanScale));
            }

            // снимок 1: каждый в своём действии в момент удара/выстрела/каста
            var actions = new (string trigger, string param, float value, float time)[]
            {
                ("Attack", "AttackVariant", 0f, 0.32f),   // Fists
                ("Attack", "AttackVariant", 0f, 0.32f),   // Sword
                ("Attack", "AttackVariant", 0f, 0.62f),   // Bow: выпуск
                ("Cast", "CastType", 0f, 0.75f),          // Staff: площадной каст
                ("Attack", "AttackVariant", 0f, 0.32f),   // Wand
                (null, "MoveBlend", 1f, 0.40f),           // Club: шаг
                ("Cast", "CastType", 0f, 0.80f),          // Boss: удар по земле
            };
            for (int i = 0; i < anims.Length; i++)
            {
                var a = anims[i];
                var act = actions[Mathf.Min(i, actions.Length - 1)];
                a.Rebind();
                a.Update(0f);
                a.SetFloat(act.param, act.value);
                if (act.trigger != null) a.SetTrigger(act.trigger);
                a.Update(0f);
                a.Update(act.time);
            }
            Render(Camera.main, Path.Combine(dir, "lineup_actions.png"));

            // снимок 2: все бегут, у каждого второго вздрагивание (аддитивный слой Hit)
            for (int i = 0; i < anims.Length; i++)
            {
                var a = anims[i];
                a.Rebind();
                a.SetFloat("MoveBlend", 2f);
                a.Update(0f);
                a.Update(0.3f);
                if (i % 2 == 0) { a.SetTrigger("Hit"); a.Update(0f); a.Update(0.25f); }
                else a.Update(0.25f);
            }
            Render(Camera.main, Path.Combine(dir, "lineup_run_hit.png"));

            // смерть и возврат: таз должен опуститься, а после Dead=false - подняться
            foreach (var a in anims)
            {
                a.Rebind();
                a.Update(0f);
                a.SetBool("Dead", true);
                a.Update(0f);
                for (int k = 0; k < 30; k++) a.Update(0.1f);
                float down = a.GetBoneTransform(HumanBodyBones.Hips).position.y;
                a.SetBool("Dead", false);
                a.Update(0f);
                for (int k = 0; k < 10; k++) a.Update(0.1f);
                float up = a.GetBoneTransform(HumanBodyBones.Hips).position.y;
                Debug.Log(string.Format(ci, "[AnimVerify] {0}: таз после смерти {1:0.00} м, после возврата {2:0.00} м", a.name, down, up));
            }
        }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void Render(Camera cam, string path, int w = 1920, int h = 1080)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Debug.Log("[AnimVerify] снимок " + path);
    }
}
