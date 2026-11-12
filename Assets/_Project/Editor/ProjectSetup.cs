// Каркас проекта по TDD (4.2 и задачи 9.В): настройки WebGL-сборки, слои для лучей визуала, сцена Game в сборке.
// Меню: Tools > P1 > Setup Project. Повторный запуск безопасен. Batchmode: -executeMethod ProjectSetup.RunBatch
//
// Где что лежит (раздел 4.2, с поправкой на уже собранный арт):
//   Assets/_Project/Logic        Game.Logic.asmdef - чистый C#, noEngineReferences
//   Assets/_Project/Logic.Tests  Game.Logic.Tests.asmdef - EditMode-тесты и headless-прогон
//   Assets/_Project/View         Game.View.asmdef - MonoBehaviour, ввод, UI, эффекты (ссылается на Logic и Game.Characters)
//   Assets/_Project/Data         ScriptableObjects: баланс, юниты, оружие, навыки, волны, уровень
//   Assets/_Project/UI, Audio, Scenes
//   Assets/Art                   арт из Blender (sync_unity.ps1 + Tools > P1 Art > Setup All)
//   Assets/Characters            персонажи AA, клипы Mixamo, наборы анимаций (Tools > P1 Characters)
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProjectSetup
{
    public const string GameScene = "Assets/_Project/Scenes/Game.unity";

    // Луч из камеры: по земле (приказ «идти») и по коллайдерам юнитов (выделение, «атаковать»)
    public const int GroundLayer = 6, UnitsLayer = 7;
    static readonly (int index, string name)[] Layers = { (GroundLayer, "Ground"), (UnitsLayer, "Units") };

    [MenuItem("Tools/P1/Setup Project", priority = 0)]
    public static void SetupAll()
    {
        SetupWebGL();
        SetupLayers();
        SetupGameScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectSetup] OK");
    }

    public static void RunBatch()
    {
        try { SetupAll(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // ------------------------------------------------------------------ WebGL (раздел 9, Пт)
    static void SetupWebGL()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;   // itch.io: Gzip +
        PlayerSettings.WebGL.decompressionFallback = true;                      // Decompression Fallback, иначе не запустится
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.defaultWebScreenWidth = 1280;                            // страница itch.io 1280x720
        PlayerSettings.defaultWebScreenHeight = 720;
        Debug.Log("[ProjectSetup] WebGL: Gzip + Decompression Fallback, stripping Medium, 1280x720, исключения: " +
                  PlayerSettings.WebGL.exceptionSupport + " (для релиза поставить None)");
    }

    // ------------------------------------------------------------------ слои
    static void SetupLayers()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        foreach (var (index, name) in Layers)
        {
            var p = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(p.stringValue) && p.stringValue != name)
            {
                Debug.LogWarning("[ProjectSetup] слой " + index + " уже занят: " + p.stringValue);
                continue;
            }
            p.stringValue = name;
        }
        tm.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[ProjectSetup] слои: " + string.Join(", ", Layers.Select(l => l.index + "=" + LayerMask.LayerToName(l.index))));
    }

    // ------------------------------------------------------------------ сцена игры
    static void SetupGameScene()
    {
        CharacterSetup.EnsureFolder(Path.GetDirectoryName(GameScene).Replace('\\', '/'));
        if (!File.Exists(GameScene))
        {
            var prevActive = EditorSceneManager.GetActiveScene();
            bool untitled = Enumerable.Range(0, EditorSceneManager.sceneCount)
                .Any(i => string.IsNullOrEmpty(EditorSceneManager.GetSceneAt(i).path));
            if (untitled && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 16f, -12f), Quaternion.Euler(55f, 0f, 0f));
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            new GameObject("Level");   // земля, дорога, ратуша, портал, декор
            new GameObject("Path");    // контрольные точки кривой (LevelAuthoring)

            EditorSceneManager.SaveScene(scene, GameScene);
            if (!untitled)
            {
                EditorSceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log("[ProjectSetup] создана сцена " + GameScene);
        }

        // в сборку идёт только Game; витрины арта остаются в списке выключенными
        var others = EditorBuildSettings.scenes.Where(s => s.path != GameScene).Select(s => new EditorBuildSettingsScene(s.path, false));
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true) }.Concat(others).ToArray();
        Debug.Log("[ProjectSetup] сцены сборки: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => (s.enabled ? "+" : "-") + s.path)));
    }
}
