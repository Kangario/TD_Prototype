// Дальность теней только для своей камеры: на время её рендера подменяет Shadow Distance ассета URP и сразу
// возвращает. Нужна обзорной камере карты троп - она дальше 150 м (настройка PC), и без этого ратуша вверху кадра
// остаётся без теней. Другие камеры и сцены настройку ассета не замечают, сам ассет не меняется.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraShadowDistance : MonoBehaviour
{
    [Min(1f)] public float distance = 320f;

    Camera cam;
    float saved = -1f;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
        Restore();
    }

    void Begin(ScriptableRenderContext ctx, Camera c)
    {
        if (c != cam || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
        if (saved < 0f) saved = urp.shadowDistance;
        urp.shadowDistance = distance;
    }

    void End(ScriptableRenderContext ctx, Camera c)
    {
        if (c == cam) Restore();
    }

    void Restore()
    {
        if (saved < 0f || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
        urp.shadowDistance = saved;
        saved = -1f;
    }
}
