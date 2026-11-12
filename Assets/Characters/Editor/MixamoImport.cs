// Импорт анимаций Mixamo. Всё, что лежит в Assets/Characters/Animations/Mixamo (с подпапками), становится
// Humanoid-клипом с аватаром X Bot и поэтому играет на любом Humanoid-персонаже (ретаргет Mecanim).
// Скачивать с Mixamo: FBX for Unity, 30 fps, X Bot. In Place НЕ нужен: смещение остаётся в root motion,
// Animator с выключенным Apply Root Motion держит персонажа на месте, а UnitAnimator знает родную скорость клипа.
// Имена файлов: <Набор>_<Слот>_<NN>.fbx (Sword_Attack_01). idle/walk/run в имени -> клип зацикливается.
// Настройки ставятся только новым файлам / клипам без своих настроек - ручные правки в инспекторе не затираются.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MixamoImport : AssetPostprocessor
{
    public const string ClipDir = "Assets/Characters/Animations/Mixamo/";
    public const string AvatarModel = "Assets/Characters/Mixamo/X Bot.fbx";

    // клипы с этими словами в имени файла зацикливаются
    static readonly string[] LoopWords = { "idle", "walk", "run", "jog", "sprint", "strafe", "loop", "breath" };

    // Обрезка длинных клипов, секунды исходника. Атаки: удар/выстрел через ~0.3 с от начала (в логике урон
    // проходит на тике атаки, длинный замах дал бы задержку). Навыки: до ~1.5 с.
    public static readonly Dictionary<string, Vector2> Trims = new Dictionary<string, Vector2>
    {
        { "Sword_Attack_01", new Vector2(0.47f, 1.37f) },
        { "Sword_Attack_02", new Vector2(0.47f, 1.37f) },
        { "Sword_Attack_03", new Vector2(0.27f, 1.17f) },
        { "Bow_Attack_01",   new Vector2(3.35f, 4.30f) },   // прицел -> выпуск (~3.95) -> отдача
        { "Staff_Attack_01", new Vector2(0.83f, 1.73f) },
        { "Staff_Attack_02", new Vector2(0.97f, 1.87f) },
        { "Staff_Attack_03", new Vector2(0.63f, 1.53f) },
        { "Wand_Attack_01",  new Vector2(0.50f, 1.40f) },
        { "Wand_Attack_02",  new Vector2(0.20f, 1.10f) },
        { "Wand_Attack_03",  new Vector2(0.63f, 1.53f) },
        { "Fists_Attack_01", new Vector2(0.70f, 1.60f) },
        { "Hero_Cast_01",    new Vector2(0.00f, 1.30f) },
        { "Sword_Cast_01",   new Vector2(1.20f, 2.90f) },
        { "Staff_Cast_01",   new Vector2(0.50f, 2.00f) },
        { "Staff_Cast_02",   new Vector2(0.00f, 1.50f) },
        { "Boss_Stomp_01",   new Vector2(1.00f, 2.60f) },
    };

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ClipDir) || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.importAnimation = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importBlendShapes = false;
        mi.importCameras = false;
        mi.importLights = false;
        context.DependsOnArtifact(AvatarModel);
        var avatar = LoadAvatar();
        if (avatar != null && avatar.isHuman)
        {
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = avatar;
        }
        else
        {
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            Debug.LogWarning("[MixamoImport] нет аватара " + AvatarModel + ", " + assetPath + " получит свой аватар");
        }
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(ClipDir)) return;
        var mi = (ModelImporter)assetImporter;
        if (mi.clipAnimations.Length > 0) return;
        mi.clipAnimations = ConfigureClips(mi, mi.defaultClipAnimations, assetPath);
    }

    public static string ClipName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        foreach (var prefix in new[] { "X Bot_", "Y Bot_", "X Bot@", "Y Bot@" })
            if (name.StartsWith(prefix)) name = name.Substring(prefix.Length);
        return name;
    }

    /// Имя клипа = имя файла, петля для idle/walk/run, обрезка по Trims.
    /// Поворот и высота запекаются в позу, смещение XZ - нет (уходит в root motion, Animator его не применяет).
    public static ModelImporterClipAnimation[] ConfigureClips(ModelImporter mi, ModelImporterClipAnimation[] clips, string path)
    {
        string name = ClipName(path);
        bool loop = LoopWords.Any(w => name.ToLowerInvariant().Contains(w));
        var takes = mi.importedTakeInfos ?? new TakeInfo[0];
        foreach (var c in clips)
        {
            c.name = clips.Length == 1 ? name : name + "_" + c.takeName;
            c.loopTime = loop;
            c.loopPose = loop;
            c.lockRootRotation = true;
            c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;
            c.keepOriginalPositionXZ = false;
            if (Trims.TryGetValue(name, out var t))
            {
                float rate = takes.Where(k => k.name == c.takeName).Select(k => k.sampleRate).FirstOrDefault();
                if (rate <= 0f) rate = 30f;
                float start = c.firstFrame;   // секунды в Trims отсчитаны от начала дубля
                c.firstFrame = start + Mathf.Round(t.x * rate);
                c.lastFrame = Mathf.Min(c.lastFrame, start + Mathf.Round(t.y * rate));
            }
        }
        return clips;
    }

    public static Avatar LoadAvatar() =>
        AssetDatabase.LoadAllAssetsAtPath(AvatarModel).OfType<Avatar>().FirstOrDefault();

    public static AnimationClip[] LoadClips(string path) =>
        AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
}
