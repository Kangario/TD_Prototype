// Персонажи AA_UniversalCharacter: раскладка, материал, чистка префабов, затем контроллеры анимаций (AnimationSetup).
// Меню: Tools > P1 Characters > Setup AA Characters. Повторный запуск безопасен.
//  - раскладывает пак и X Bot по Assets/Characters (оригинал пака копируется в <проект>/_backup_aa)
//  - материал пака -> P1/Arcane (Arcane-вид, ArcaneLook.ConfigureCharacter)
//  - из префабов персонажей убирает ModulesShaker и скрытые меши других наборов/пола
//  - удаляет демо-сцену, скрипты пака (ломают билд: UnityEditor вне Editor-папки) и лишние префабы
//  - AnimationSetup.SetupAll: Unit_Base + Override-наборы, набор Sword на префабах, сцена CharacterLineup
// Batchmode: -executeMethod CharacterSetup.RunBatch ; проверка - AnimationSetup.VerifyBatch
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CharacterSetup
{
    const string OldRoot = "Assets/AA_UniversalCharacter";
    const string Root = "Assets/Characters";
    const string AA = AnimationSetup.AA;
    const string BackupDir = "_backup_aa/AA_UniversalCharacter_original";   // от корня проекта, вне Assets

    public static readonly string[] Characters =
        { "M_AA_001", "M_AA_002", "F_AA_001", "F_AA_002", "UniversalMale", "UniversalFemale" };

    static readonly string[] Unneeded =
    {
        AA + "/DemoScene", AA + "/Scripts",
        AA + "/Prefabs/UniversalCharacter.prefab", AA + "/Prefabs/UniversalCharacterAll.prefab",
    };

    [MenuItem("Tools/P1 Characters/Setup AA Characters", priority = 0)]
    public static void SetupAll()
    {
        Reorganize();
        SetupMaterial();
        foreach (var c in Characters) CleanCharacterPrefab(AA + "/Prefabs/" + c + ".prefab");
        RemoveUnneeded();
        AssetDatabase.SaveAssets();
        AnimationSetup.SetupAll();
        Debug.Log("[CharSetup] OK: персонажи " + string.Join(", ", Characters));
    }

    public static void RunBatch()
    {
        try { SetupAll(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // ------------------------------------------------------------------ папки
    static void Reorganize()
    {
        if (AssetDatabase.IsValidFolder(OldRoot) && !Directory.Exists(BackupDir))
        {
            CopyDir(OldRoot, BackupDir);
            Debug.Log("[CharSetup] бэкап оригинала: " + Path.GetFullPath(BackupDir));
        }
        EnsureFolder(Root + "/Mixamo");
        Move(OldRoot + "/X Bot.fbx", MixamoImport.AvatarModel);
        Move(OldRoot, AA);
    }

    public static void Move(string from, string to)
    {
        if (!AssetDatabase.IsValidFolder(from) && AssetDatabase.LoadMainAssetAtPath(from) == null) return;
        EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
        string err = AssetDatabase.MoveAsset(from, to);
        if (!string.IsNullOrEmpty(err)) throw new System.Exception("MoveAsset " + from + " -> " + to + ": " + err);
        Debug.Log("[CharSetup] " + from + " -> " + to);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ материал
    static void SetupMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AA + "/Materials/UniversalCharacterMaterial.mat");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AA + "/Textures/UniversalCharacterTexture.png");
        var arcane = Shader.Find(ArcaneLook.ShaderName);   // Arcane-вид, см. Assets/Art/Editor/ArcaneLook.cs
        if (mat == null || tex == null || arcane == null) throw new System.Exception("нет материала/текстуры пака или шейдера " + ArcaneLook.ShaderName);
        if (mat.shader != arcane) mat.shader = arcane;
        mat.shaderKeywords = new string[0];
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        ArcaneLook.ConfigureCharacter(mat);
        EditorUtility.SetDirty(mat);
    }

    static void ValidateWithUrpGui(Material m, string guiType)
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(guiType)).FirstOrDefault(t => t != null);
        if (type == null) return;
        try
        {
            var gui = System.Activator.CreateInstance(type, true);
            type.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(gui, new object[] { m });
        }
        catch (System.Exception e) { Debug.LogWarning("[CharSetup] ValidateMaterial: " + e.Message); }
    }

    // ------------------------------------------------------------------ префабы
    static void CleanCharacterPrefab(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int scripts = 0;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "ModulesShaker") { Object.DestroyImmediate(mb); scripts++; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                scripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            var all = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var visible = all.Where(r => r.gameObject.activeInHierarchy && r.enabled && r.sharedMesh != null).ToList();
            var hidden = all.Except(visible)
                .Where(r => !visible.Any(v => v.transform.IsChildOf(r.transform))).ToList();
            foreach (var r in hidden) Object.DestroyImmediate(r.gameObject);

            // скелет целиком и путь до видимых мешей не трогаем, остальные пустые узлы убираем
            var armature = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Armature");
            if (armature == null) throw new System.Exception(path + ": нет Armature");
            var keep = new HashSet<Transform>(armature.GetComponentsInChildren<Transform>(true)) { root.transform };
            foreach (var r in visible)
                foreach (var start in r.bones.Append(r.rootBone).Append(r.transform))
                    for (var t = start; t != null; t = t.parent) keep.Add(t);
            int pruned = 0;
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (keep.Contains(t) || t.childCount > 0 || t.GetComponents<Component>().Length > 1) continue;
                    Object.DestroyImmediate(t.gameObject);
                    pruned++;
                    changed = true;
                    break;
                }
            }

            var anim = root.GetComponent<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) throw new System.Exception(path + ": нет Animator с Humanoid-аватаром");
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            if (hidden.Count + pruned + scripts > 0)
                Debug.Log("[CharSetup] " + Path.GetFileNameWithoutExtension(path) + ": мешей " + visible.Count +
                          ", удалено скрытых " + hidden.Count + ", пустых узлов " + pruned + ", скриптов " + scripts);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void RemoveUnneeded()
    {
        foreach (var p in Unneeded)
        {
            if (!AssetDatabase.IsValidFolder(p) && AssetDatabase.LoadMainAssetAtPath(p) == null) continue;
            if (!AssetDatabase.DeleteAsset(p)) throw new System.Exception("DeleteAsset " + p);
            Debug.Log("[CharSetup] удалено: " + p);
        }
    }
}
