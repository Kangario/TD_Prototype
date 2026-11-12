// Атмосфера Arcane-стиля: ветер (по нему плывут облачка ArcaneCloudField) и тени облаков на земле.
// Ставит глобальные параметры шейдеров P1/Arcane и P1/ArcaneCloud. Один на сцену.
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("P1 Art/Arcane Atmosphere")]
public class ArcaneAtmosphere : MonoBehaviour
{
    [Header("Ветер (облачка и их тени)")]
    [Tooltip("Скорость по X и Z, м/с")]
    public Vector2 wind = new Vector2(0.55f, 0.2f);

    [Header("Тени облаков на земле")]
    [Range(0f, 1f)] public float cloudShadowStrength = 0.4f;
    [Tooltip("Размер тайла шума, м (пятна примерно в 3-4 раза меньше)")]
    [Min(1f)] public float cloudShadowTile = 70f;
    [Tooltip("Какая доля земли под тенью")]
    [Range(0f, 1f)] public float coverage = 0.32f;
    [Range(0.01f, 0.5f)] public float softness = 0.05f;
    [Tooltip("Высота облаков, м: тени смещаются по лучу солнца")]
    public float cloudHeight = 35f;

    static readonly int CloudShadowId = Shader.PropertyToID("_P1_CloudShadow");
    static readonly int CloudShadow2Id = Shader.PropertyToID("_P1_CloudShadow2");
    static readonly int WindId = Shader.PropertyToID("_P1_Wind");

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void OnDisable()
    {
        Shader.SetGlobalVector(CloudShadowId, Vector4.zero);
        Shader.SetGlobalVector(CloudShadow2Id, Vector4.zero);
        Shader.SetGlobalVector(WindId, Vector4.zero);
    }

    public void Apply()
    {
        // шум A у текстуры кисти ~ среднее 0.5, разброс 0.2: порог по покрытию
        float threshold = Mathf.Lerp(0.95f, 0.2f, coverage);
        Shader.SetGlobalVector(CloudShadowId, new Vector4(1f / Mathf.Max(cloudShadowTile, 1f), cloudShadowStrength, threshold, softness));
        Shader.SetGlobalVector(CloudShadow2Id, new Vector4(wind.x, wind.y, cloudHeight, 0f));
        Shader.SetGlobalVector(WindId, new Vector4(wind.x, wind.y, 0f, 0f));
    }
}
