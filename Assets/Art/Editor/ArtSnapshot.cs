// Снимок сцены-витрины в PNG (проверка вида без запуска игры).
// Меню: Tools > P1 Art > Save Showcase Screenshot -> Logs/ArtShowcase.png
// Batchmode: -executeMethod ArtSnapshot.RunBatch (путь можно задать переменной окружения P1_SNAPSHOT)
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class ArtSnapshot
{
    const string ScenePath = "Assets/Scenes/ArtShowcase.unity";

    [MenuItem("Tools/P1 Art/Save Showcase Screenshot", priority = 20)]
    public static void SaveFromMenu()
    {
        string path = Path.GetFullPath("Logs/ArtShowcase.png");
        Render(path, 1920, 1080);
        EditorUtility.RevealInFinder(path);
    }

    public static void RunBatch()
    {
        try
        {
            string path = System.Environment.GetEnvironmentVariable("P1_SNAPSHOT");
            if (string.IsNullOrEmpty(path)) path = Path.GetFullPath("Logs/ArtShowcase.png");
            EditorSceneManager.OpenScene(ScenePath);
            // P1_SNAPSHOT_FOCUS="x,y,z" - крупный план: камера смотрит на точку с 15 м (сцена не сохраняется)
            string focus = System.Environment.GetEnvironmentVariable("P1_SNAPSHOT_FOCUS");
            if (!string.IsNullOrEmpty(focus) && Camera.main != null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var f = focus.Split(',');
                var target = new Vector3(float.Parse(f[0], ci), float.Parse(f[1], ci), float.Parse(f[2], ci));
                Camera.main.transform.position = target + new Vector3(0f, 9f, 12f);
                Camera.main.transform.LookAt(target);
            }
            Render(path, 1920, 1080);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void Render(string path, int w, int h)
    {
        var cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null) throw new System.Exception("В сцене нет камеры");

        // частицы в редакторе не играют - прокручиваем до выразительного момента
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            var parent = ps.transform.parent;
            if (parent != null && parent.GetComponent<ParticleSystem>() != null) continue;
            string root = ps.transform.root.name + "/" + (parent != null ? parent.name : ps.name);
            float t = root.Contains("Hit") ? 0.09f : root.Contains("Heal") ? 0.35f : 1.2f;
            ps.Simulate(t, true, true);
        }

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request))
            RenderPipeline.SubmitRenderRequest(cam, request);
        else
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
        }

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
        Debug.Log("[ArtSnapshot] " + path);
    }
}
