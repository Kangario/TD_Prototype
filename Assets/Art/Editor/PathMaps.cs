// Сцены-карты по схеме путей (рисунок-прототип): два портала, пути сходятся у развилки и идут к ратуше.
//   Map_RockyTerrain - дорога из плит по скалистой земле, между путями выступы и валуны, по краю обрывы и горы;
//   Map_RockTrails   - сами пути - скальные тропы над пропастью: столбы на поворотах и развилке, между ними
//                      настилы-мосты, портал и ратуша на парящих площадках, внизу слои тумана.
// Схема в метрах - массивы ниже (1 px рисунка ~ 0.07 м). Контрольные точки кривой врагов - Path/Route_Left и
// Path/Route_Right (от портала до выхода ратуши, не реже 3 м), их потом прочитает LevelAuthoring.
// Свет и пост-обработку ставит ArcaneLook.SetupScene, затем для карт два отличия: солнце светит со стороны камеры
// (камера TD смотрит в +Z, а ArcaneLook настроен на витрину с камерой в -Z), и размытие дальнего плана начинается
// дальше (Assets/Settings/PP_PathMaps.asset) - иначе вся схема в кадре, а ратуша вверху кадра размыта.
// Меню: Tools > P1 Art > Path Maps. Повторный запуск пересобирает сцену целиком - ручные правки в ней пропадут.
// Batchmode: -executeMethod PathMaps.RunBatch
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

public static class PathMaps
{
    public const string SceneDir = "Assets/Scenes/Maps";
    public const string RockyScene = SceneDir + "/Map_RockyTerrain.unity";
    public const string TrailsScene = SceneDir + "/Map_RockTrails.unity";

    const string EnvDir = "Assets/Art/Environment";
    const string PrefabDir = "Assets/Art/Prefabs/Environment";
    const string MatDir = "Assets/Art/Materials";
    const string RockGroundPath = MatDir + "/M_Arcane_RockGround.mat";
    const string LightingPath = "Assets/Scenes/ArtShowcase_Lighting.lighting";
    const string ProfilePath = "Assets/Settings/PP_Arcane.asset";
    const string MapProfilePath = "Assets/Settings/PP_PathMaps.asset";
    static readonly Vector3 SunFrom = new Vector3(-0.68f, 0.64f, -0.36f);   // слева и со стороны камеры
    static readonly string[] KitPrefixes = { "SM_Mtn_", "SM_Ruin_", "SM_Web_", "SM_Trail_" };

    // ------------------------------------------------------------------ схема путей
    // Вид сверху как на рисунке: X вправо, Z вверх по рисунку (от камеры). Все точки на сетке x.5 м -
    // плиты дороги 1x1 м ложатся по 3 в ширину ровно по оси пути.
    public static readonly Vector2 PortalLeft = new Vector2(-38.5f, -3.5f);    // левый кружок
    public static readonly Vector2 PortalRight = new Vector2(38.5f, -20.5f);   // правый кружок
    public static readonly Vector2 Junction = new Vector2(-6.5f, -15.5f);      // развилка: пути сходятся
    public static readonly Vector2 HallExit = new Vector2(-9.5f, 18.5f);       // квадрат: выход Exit_Front ратуши
    static readonly Vector2 StepA = new Vector2(-6.5f, 10.5f), StepB = new Vector2(-9.5f, 10.5f);   // ступенька

    static readonly Vector2[] LeftLeg = { PortalLeft, new Vector2(-14.5f, -3.5f), new Vector2(-14.5f, -15.5f), Junction };
    static readonly Vector2[] RightLeg =
    {
        PortalRight, new Vector2(9.5f, -20.5f), new Vector2(9.5f, 1.5f), new Vector2(1.5f, 1.5f),
        new Vector2(1.5f, -15.5f), Junction
    };
    static readonly Vector2[] HallLeg = { Junction, StepA, StepB, HallExit };
    static Vector2[][] Legs => new[] { LeftLeg, RightLeg, HallLeg };

    /// Пути врагов от портала до ратуши (ломаные по схеме).
    public static Vector2[] RouteLeft => LeftLeg.Concat(HallLeg.Skip(1)).ToArray();
    public static Vector2[] RouteRight => RightLeg.Concat(HallLeg.Skip(1)).ToArray();

    const float PathPointStep = 3f;    // Catmull-Rom через частые точки держится ломаной, углы скругляет в пределах дороги
    const float RoadHalf = 1.5f;       // дорога из плит 3 м (логике нужно pathWidth 2)
    const float CamPitch = 55f, CamFov = 40f;
    static readonly int GroundLayer = 6;   // ProjectSetup.GroundLayer

    // ------------------------------------------------------------------ меню
    [MenuItem("Tools/P1 Art/Path Maps/Build Both", priority = 40)]
    public static void BuildBothMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll();
    }

    [MenuItem("Tools/P1 Art/Path Maps/Rocky Terrain", priority = 41)]
    public static void RockyMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Prepare();
        BuildRockyTerrain();
    }

    [MenuItem("Tools/P1 Art/Path Maps/Rock Trails", priority = 42)]
    public static void TrailsMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Prepare();
        BuildRockTrails();
    }

    public static void RunBatch()
    {
        try { BuildAll(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void BuildAll()
    {
        Prepare();
        BuildRockyTerrain();
        BuildRockTrails();
        AssetDatabase.SaveAssets();
    }

    static void Prepare()
    {
        Cache.Clear();
        EnsureKitPrefabs();
        EnsureFolder(SceneDir);
    }

    // ================================================================== сцена 1: скалистая местность
    public static void BuildRockyTerrain()
    {
        var m = NewMap(21);
        var road = Group("Road", m.Level);
        var ground = Group("Ground", m.Level);
        var rocks = Group("Rocks", m.Level);

        // земля - тайлы 24 м с каменными цветами треугольников; закрывает кадр с запасом на прокрутку камеры
        var rockMat = RockGround();
        var cover = GroundFootprint(m.Cam, 40f);
        for (float x = Snap(cover.xMin); x <= cover.xMax; x += 24f)
            for (float z = Snap(cover.yMin); z <= cover.yMax; z += 24f)
            {
                var t = Place(P("Ground_Tile"), ground, new Vector3(x, 0f, z), 0f);
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = rockMat;
                SetLayer(t, GroundLayer);
            }

        // дорога - плиты 1x1 м, по 3 в ширину, на поворотах квадрат
        foreach (var c in RoadCells())
            Place(P("RoadTile_0" + (m.Rng.Next(8) + 1)), road, new Vector3(c.x + 0.5f, 0f, c.y + 0.5f), 90f * m.Rng.Next(4));

        var placed = new List<(Vector2 p, float r)>();
        var center = new Vector2(0f, 7.5f);
        // пояс скал вокруг карты: ближний - обрывы и плато, дальний - горы повыше; к камере только низкое
        RockRing(m, rocks, placed, center, new Vector2(54f, 38f), 14f, NearNorth, NearSide, NearSouth, 3.5f);
        RockRing(m, rocks, placed, center, new Vector2(86f, 62f), 24f, FarNorth, FarSide, null, 3.5f);
        // скальные группы между путями: выступ и валуны вокруг, у дороги не ближе 2.5 м - там стоят герои
        RockClusters(m, rocks, placed, center, new Vector2(56f, 40f), 24);
        // редкие одиночные валуны и камешки у обочин
        Scatter(m, rocks, placed, Boulders, 26, center, new Vector2(64f, 46f), RoadHalf + 1.2f, 3f);
        Pebbles(m, rocks, 55);

        Finish(m, RockyScene, null);
    }

    struct Piece
    {
        public string name;
        public float r, minS, maxS, h;   // r - радиус в плане и h - высота при масштабе 1
        public Piece(string name, float r, float minS, float maxS, float h = 0f)
        {
            this.name = name; this.r = r; this.minS = minS; this.maxS = maxS; this.h = h;
        }
    }

    static readonly Piece[] NearNorth =
    {
        new Piece("Mtn_Wall_01", 40f, 0.35f, 0.45f), new Piece("Mtn_Plateau_01", 30f, 0.35f, 0.45f),
        new Piece("Mtn_Butte_01", 14f, 0.45f, 0.6f), new Piece("Cliff_04", 10f, 1.1f, 1.4f), new Piece("Cliff_05", 12f, 1.0f, 1.3f)
    };
    static readonly Piece[] NearSide =
    {
        new Piece("Mtn_Slab_01", 34f, 0.28f, 0.36f), new Piece("Mtn_Plateau_01", 30f, 0.28f, 0.36f),
        new Piece("Cliff_01", 9f, 1.0f, 1.3f), new Piece("Cliff_03", 7f, 1.0f, 1.3f), new Piece("Cliff_05", 12f, 0.9f, 1.2f),
        new Piece("Mtn_Butte_01", 14f, 0.35f, 0.45f)
    };
    static readonly Piece[] NearSouth =
    {
        new Piece("Cliff_06", 6f, 0.9f, 1.2f), new Piece("Cliff_07", 4f, 1.0f, 1.5f), new Piece("Cliff_08", 6f, 1.0f, 1.3f),
        new Piece("Cliff_01", 9f, 0.6f, 0.8f)
    };
    static readonly Piece[] FarNorth =
    {
        new Piece("Mtn_Peak_01", 55f, 0.4f, 0.55f), new Piece("Mtn_Wall_01", 40f, 0.6f, 0.75f),
        new Piece("Mtn_Plateau_01", 30f, 0.55f, 0.7f), new Piece("Mtn_Butte_01", 14f, 0.7f, 0.9f)
    };
    static readonly Piece[] FarSide =
    {
        new Piece("Mtn_Slab_01", 34f, 0.45f, 0.55f), new Piece("Mtn_Plateau_01", 30f, 0.5f, 0.6f),
        new Piece("Mtn_Wall_01", 40f, 0.5f, 0.6f), new Piece("Mtn_Butte_01", 14f, 0.6f, 0.8f)
    };
    static readonly Piece[] Formations =
    {
        new Piece("Cliff_06", 6f, 0.9f, 1.3f, 4.5f), new Piece("Cliff_05", 12f, 0.5f, 0.7f, 13f),
        new Piece("Cliff_01", 9f, 0.6f, 0.8f, 9f), new Piece("Cliff_03", 7f, 0.6f, 0.9f, 8f),
        new Piece("Cliff_07", 4f, 1.0f, 1.5f, 3.5f), new Piece("Cliff_08", 6f, 0.8f, 1.2f, 4f),
        new Piece("Cliff_04", 10f, 0.45f, 0.6f, 17f), new Piece("Cliff_02", 5.5f, 0.6f, 0.8f, 7f)
    };
    static readonly Piece[] Boulders =
    {
        new Piece("Rock_01", 1.4f, 0.8f, 1.5f, 1.5f), new Piece("Rock_02", 1.6f, 0.8f, 1.4f, 0.8f),
        new Piece("Rock_03", 1.0f, 0.8f, 1.6f, 2.8f), new Piece("Rock_04", 1.2f, 0.8f, 1.5f, 1.0f)
    };

    static void RockRing(Map m, Transform parent, List<(Vector2 p, float r)> placed, Vector2 c, Vector2 radii, float spacing,
                         Piece[] north, Piece[] side, Piece[] south, float roadGap)
    {
        float perim = Mathf.PI * (3f * (radii.x + radii.y) - Mathf.Sqrt((3f * radii.x + radii.y) * (radii.x + 3f * radii.y)));
        int n = Mathf.Max(6, Mathf.RoundToInt(perim / spacing));
        for (int i = 0; i < n; i++)
        {
            float ang = (i + Rand(m.Rng, -0.3f, 0.3f)) / n * 360f;
            Piece[] set = ang > 35f && ang < 145f ? north : ang > 215f && ang < 325f ? south : side;
            if (set == null) continue;
            var piece = set[m.Rng.Next(set.Length)];
            float s = Rand(m.Rng, piece.minS, piece.maxS);
            float r = piece.r * s;
            var dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            var p = c + new Vector2(dir.x * radii.x, dir.y * radii.y) + dir * (r * 0.5f);
            // не залезать на дорогу и к зданиям: отодвигаем наружу
            for (int k = 0; k < 40 && (DistToRoads(p) < RoadHalf + roadGap + r * 0.8f || NearBuildings(m, p, r * 0.8f)); k++)
                p += dir * 1.5f;
            float yaw = Mathf.Atan2(c.x - p.x, c.y - p.y) * Mathf.Rad2Deg + Rand(m.Rng, -25f, 25f);   // лицом к карте
            Place(P(piece.name), parent, new Vector3(p.x, 0f, p.y), yaw, s);
            placed.Add((p, r));
        }
    }

    /// Группа: крупный выступ и 3-6 валунов вокруг. Выступ не закрывает дорогу от камеры (наклон 55°: за скалой
    /// высотой h не видно ~0.7h земли).
    static void RockClusters(Map m, Transform parent, List<(Vector2 p, float r)> placed, Vector2 c, Vector2 radii, int count)
    {
        var centers = new List<Vector2>();
        for (int attempt = 0; attempt < count * 80 && centers.Count < count; attempt++)
        {
            var piece = Formations[m.Rng.Next(Formations.Length)];
            float s = Rand(m.Rng, piece.minS, piece.maxS);
            float r = piece.r * s * 0.85f, h = piece.h * s;
            var p = c + new Vector2(Rand(m.Rng, -radii.x, radii.x), Rand(m.Rng, -radii.y, radii.y));
            if (Sq((p.x - c.x) / radii.x) + Sq((p.y - c.y) / radii.y) > 1f) continue;
            if (DistToRoads(p) < RoadHalf + 2.5f + r || NearBuildings(m, p, r + 1f)) continue;
            if (Hides(p, r, h) || centers.Any(q => (q - p).sqrMagnitude < 13f * 13f)) continue;
            if (placed.Any(q => (q.p - p).sqrMagnitude < Sq(q.r + r + 1f))) continue;
            Place(P(piece.name), parent, new Vector3(p.x, 0f, p.y), Rand(m.Rng, 0f, 360f), s);
            placed.Add((p, r));
            centers.Add(p);
            int sat = 3 + m.Rng.Next(4);
            for (int k = 0, tries = 0; k < sat && tries < 60; tries++)
            {
                var b = Boulders[m.Rng.Next(Boulders.Length)];
                float bs = Rand(m.Rng, b.minS, b.maxS * 1.2f), br = b.r * bs;
                float ang = Rand(m.Rng, 0f, Mathf.PI * 2f);
                var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (r + br + Rand(m.Rng, 0f, 2.5f));
                if (DistToRoads(q) < RoadHalf + 1.2f + br || NearBuildings(m, q, br)) continue;
                if (placed.Any(o => (o.p - q).sqrMagnitude < Sq(o.r + br + 0.3f))) continue;
                Place(P(b.name), parent, new Vector3(q.x, 0f, q.y), Rand(m.Rng, 0f, 360f), bs);
                placed.Add((q, br));
                k++;
            }
        }
    }

    /// Закроет ли скала дорогу за собой (дальше от камеры, по +Z).
    static bool Hides(Vector2 p, float r, float h)
    {
        float shadow = r + 0.7f * h;
        foreach (var (a, b) in Segments())
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                var q = Vector2.Lerp(a, b, t);
                if (q.y > p.y && Mathf.Abs(q.x - p.x) < r + RoadHalf && q.y - p.y < shadow + RoadHalf) return true;
            }
        return false;
    }

    static void Scatter(Map m, Transform parent, List<(Vector2 p, float r)> placed, Piece[] set, int count, Vector2 c,
                        Vector2 radii, float roadGap, float gap)
    {
        int done = 0;
        for (int attempt = 0; attempt < count * 60 && done < count; attempt++)
        {
            var piece = set[m.Rng.Next(set.Length)];
            float s = Rand(m.Rng, piece.minS, piece.maxS);
            float r = piece.r * s;
            var p = c + new Vector2(Rand(m.Rng, -radii.x, radii.x), Rand(m.Rng, -radii.y, radii.y));
            if (Sq((p.x - c.x) / radii.x) + Sq((p.y - c.y) / radii.y) > 1f) continue;
            if (DistToRoads(p) < roadGap + r || NearBuildings(m, p, r)) continue;
            if (placed.Any(q => (q.p - p).sqrMagnitude < Sq(q.r + r + gap))) continue;
            Place(P(piece.name), parent, new Vector3(p.x, 0f, p.y), Rand(m.Rng, 0f, 360f), s);
            placed.Add((p, r));
            done++;
        }
    }

    /// Камешки вдоль обочин - острые обломки, наполовину в земле.
    static void Pebbles(Map m, Transform parent, int count)
    {
        var segs = Segments().ToList();
        var taken = new List<Vector2>();
        string[] names = { "Mtn_Shard_01", "Mtn_Shard_02", "Mtn_Shard_03" };
        for (int attempt = 0; attempt < count * 40 && taken.Count < count; attempt++)
        {
            var (a, b) = segs[m.Rng.Next(segs.Count)];
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (m.Rng.Next(2) == 0 ? -1f : 1f);
            var p = Vector2.Lerp(a, b, (float)m.Rng.NextDouble()) + n * Rand(m.Rng, RoadHalf + 0.25f, RoadHalf + 1.2f);
            if (DistToRoads(p) < RoadHalf + 0.2f || NearBuildings(m, p, 0.5f)) continue;
            if (taken.Any(q => (q - p).sqrMagnitude < 1.8f * 1.8f)) continue;
            taken.Add(p);
            var go = Place(P(names[m.Rng.Next(names.Length)]), parent, new Vector3(p.x, 0.05f, p.y), 0f, Rand(m.Rng, 0.1f, 0.22f));
            go.transform.rotation = Quaternion.Euler(Rand(m.Rng, 0f, 360f), Rand(m.Rng, 0f, 360f), Rand(m.Rng, 0f, 360f));
        }
    }

    // ================================================================== сцена 2: скальные тропы над пропастью
    static readonly Color AbyssMist = new Color(0.56f, 0.68f, 0.84f);

    public static void BuildRockTrails()
    {
        var m = NewMap(37);
        var trails = Group("Trails", m.Level);
        var backdrop = Group("Backdrop", m.Level);

        // площадки под порталы и ратушу
        var supports = new Dictionary<Vector2, (GameObject go, float insert)>();
        supports[PortalLeft] = (Place(P("Trail_Platform_01"), trails, V3(PortalLeft), 90f + Rand(m.Rng, -8f, 8f)), 2.2f);
        supports[PortalRight] = (Place(P("Trail_Platform_01"), trails, V3(PortalRight), -90f + Rand(m.Rng, -8f, 8f)), 2.2f);
        supports[HallExit] = (Place(P("Trail_Platform_02"), trails, m.HallCenter, 180f), 2.2f);

        // столбы на поворотах, на развилке большой, на ступеньке перед ратушей - один длинный на обе точки
        foreach (var v in Legs.SelectMany(l => l).Distinct())
        {
            if (supports.ContainsKey(v) || v == StepA || v == StepB) continue;
            string name = v == Junction ? "Trail_Node_03" : "Trail_Node_0" + (m.Rng.Next(2) + 1);
            float yaw = 90f * m.Rng.Next(4) + Rand(m.Rng, -7f, 7f);
            supports[v] = (Place(P(name), trails, V3(v), yaw), 1.6f);
        }
        var step = Place(P("Trail_Node_04"), trails, V3((StepA + StepB) * 0.5f), m.Rng.Next(2) * 180f);
        supports[StepA] = (step, 1.6f);
        supports[StepB] = (step, 1.6f);

        // настилы-мосты между опорами: длина по месту, модуль ближайшей длины, растяжка по длине в пределах ~15%
        var decks = KitNames("Trail_Deck_").Select(n => (name: n, len: float.Parse(n.Substring("Trail_Deck_".Length)))).ToArray();
        foreach (var (a, b) in Segments())
        {
            var (ga, ia) = supports[a];
            var (gb, ib) = supports[b];
            if (ga == gb) continue;                          // ступенька внутри одного столба
            var d = V3(b - a).normalized;
            var start = V3(a) + d * (EdgeAlong(ga, V3(a), d) - ia);
            var end = V3(b) - d * (EdgeAlong(gb, V3(b), -d) - ib);
            float need = Vector3.Dot(end - start, d);
            if (need < 1f) continue;
            var deck = decks.OrderBy(k => Mathf.Abs(Mathf.Log(need / k.len))).First();
            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg + 180f * m.Rng.Next(2);
            var go = Place(P(deck.name), trails, (start + end) * 0.5f, yaw);
            go.transform.localScale = new Vector3(need / deck.len, 1f, 1f);
        }

        TrailsBackdrop(m, backdrop);
        Finish(m, TrailsScene, scene =>
        {
            // пропасть: светлый туманный фон, слабая дымка вдали, слои облаков под тропами
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = AbyssMist;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 480f;
            m.Cam.backgroundColor = AbyssMist;
            m.Cam.farClipPlane = 650f;
            AbyssClouds(m);
        });
    }

    static void TrailsBackdrop(Map m, Transform parent)
    {
        var c = new Vector2(0f, 7.5f);
        var taken = new List<(Vector2 p, float r)>();
        // обломки старых троп: столбы без настилов, чуть ниже и с наклоном
        for (int attempt = 0, n = 0; attempt < 400 && n < 7; attempt++)
        {
            var p = c + new Vector2(Rand(m.Rng, -62f, 62f), Rand(m.Rng, -44f, 44f));
            if (DistToRoads(p) < 10f || NearBuildings(m, p, 8f) || taken.Any(q => (q.p - p).sqrMagnitude < 14f * 14f)) continue;
            var go = Place(P("Trail_Node_0" + (m.Rng.Next(2) + 1)), parent, new Vector3(p.x, Rand(m.Rng, -8f, -1.5f), p.y),
                           Rand(m.Rng, 0f, 360f), Rand(m.Rng, 0.8f, 1.15f));
            go.transform.rotation = Quaternion.Euler(Rand(m.Rng, -6f, 6f), Rand(m.Rng, 0f, 360f), Rand(m.Rng, -6f, 6f));
            taken.Add((p, 5f));
            n++;
        }
        // парящие острова по краю
        for (int i = 0; i < 6; i++)
        {
            float ang = (i + Rand(m.Rng, -0.25f, 0.25f)) / 6f * 360f * Mathf.Deg2Rad;
            var p = c + new Vector2(Mathf.Cos(ang) * 74f, Mathf.Sin(ang) * 54f);
            Place(P("Mtn_Island_01"), parent, new Vector3(p.x, Rand(m.Rng, -8f, 6f), p.y), Rand(m.Rng, 0f, 360f),
                  Rand(m.Rng, 0.45f, 0.8f));
        }
        // обломки в воздухе: над картой только ниже троп, выше - за краем
        string[] shards = { "Mtn_Shard_01", "Mtn_Shard_02", "Mtn_Shard_03", "Mtn_Shards_01" };
        for (int attempt = 0, n = 0; attempt < 600 && n < 18; attempt++)
        {
            var p = c + new Vector2(Rand(m.Rng, -80f, 80f), Rand(m.Rng, -58f, 58f));
            if (DistToRoads(p) < 6f || NearBuildings(m, p, 4f)) continue;
            bool inside = Sq(p.x / 50f) + Sq((p.y - c.y) / 36f) < 1f;
            string name = shards[m.Rng.Next(shards.Length)];
            float y = inside ? Rand(m.Rng, -16f, -4f) : Rand(m.Rng, -14f, 10f);
            float s = name == "Mtn_Shards_01" ? Rand(m.Rng, 0.5f, 0.85f) : Rand(m.Rng, 0.8f, 2.0f);
            var go = Place(P(name), parent, new Vector3(p.x, y, p.y), 0f, s);
            go.transform.rotation = Quaternion.Euler(Rand(m.Rng, -30f, 30f), Rand(m.Rng, 0f, 360f), Rand(m.Rng, -30f, 30f));
            n++;
        }
        // скальная паутина и развилки поднимаются из тумана вокруг карты, пики вдали
        Place(P("Web_Lattice_01"), parent, new Vector3(6f, -96f, 62f), 180f, 1.0f);
        Place(P("Web_Lattice_01"), parent, new Vector3(-82f, -90f, 6f), 90f, 0.9f);
        Place(P("Web_Lattice_01"), parent, new Vector3(84f, -92f, -4f), -90f, 0.9f);
        Place(P("Web_Branch_01"), parent, new Vector3(-62f, -52f, 44f), 30f, 0.9f);
        Place(P("Web_Branch_01"), parent, new Vector3(64f, -50f, 34f), -20f, 0.8f);
        Place(P("Mtn_Peak_01"), parent, new Vector3(-130f, -60f, 170f), 20f, 1.4f);
        Place(P("Mtn_Peak_01"), parent, new Vector3(150f, -70f, 190f), 160f, 1.6f);
    }

    static void AbyssClouds(Map m)
    {
        var atmo = new GameObject("Abyss Mist").transform;   // вне Level: облака строят меш сами, не static
        var mist = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Arcane_Mist.mat");
        var clouds = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Arcane_Clouds.mat");
        var c = new Vector3(0f, 0f, 7.5f);
        Clouds(atmo, "Mist_Under_Trails", mist, c + Vector3.up * -10f, ArcaneCloudField.Area.Box, new Vector2(190f, 140f),
               new Vector2(0f, 5f), 36, new Vector2(12f, 22f), 0.5f, 0.55f, Color.white, 3);
        Clouds(atmo, "Clouds_Abyss_Mid", clouds, c + Vector3.up * -24f, ArcaneCloudField.Area.Box, new Vector2(210f, 160f),
               new Vector2(0f, 6f), 55, new Vector2(18f, 32f), 0.55f, 0.7f, new Color(0.9f, 0.94f, 1f), 5);
        Clouds(atmo, "Clouds_Abyss_Deep", clouds, c + Vector3.up * -44f, ArcaneCloudField.Area.Box, new Vector2(240f, 190f),
               new Vector2(0f, 8f), 60, new Vector2(28f, 46f), 0.55f, 0.9f, new Color(0.82f, 0.88f, 1f), 9);
        Clouds(atmo, "Clouds_Ring", clouds, c, ArcaneCloudField.Area.Ring, Vector2.zero,
               new Vector2(-12f, 8f), 40, new Vector2(14f, 26f), 0.7f, 0.85f, Color.white, 13, 82f, 125f);
    }

    static void Clouds(Transform parent, string name, Material mat, Vector3 pos, ArcaneCloudField.Area area, Vector2 box,
                       Vector2 height, int count, Vector2 size, float flatness, float opacityMin, Color tint,
                       int seed, float inner = 0f, float outer = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var f = go.AddComponent<ArcaneCloudField>();
        f.area = area;
        f.boxSize = box;
        f.innerRadius = inner;
        f.outerRadius = outer;
        f.arcFrom = 0f;
        f.arcTo = 360f;
        f.height = height;
        f.count = count;
        f.size = size;
        f.flatness = flatness;
        f.opacityMin = opacityMin;
        f.tint = tint;
        f.seed = seed;
        f.Rebuild();
    }

    /// Насколько опора выступает от точки пути в сторону dir (по вершинам верхней кромки рядом с осью пути).
    static float EdgeAlong(GameObject support, Vector3 origin, Vector3 dir, float lateral = 1.2f)
    {
        var n = Vector3.Cross(Vector3.up, dir);
        float best = 0f;
        foreach (var mf in support.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var tr = mf.transform;
            foreach (var v in mesh.vertices)
            {
                var w = tr.TransformPoint(v);
                if (w.y < -0.7f || w.y > 0.25f) continue;
                var d = w - origin;
                if (Mathf.Abs(Vector3.Dot(d, n)) > lateral) continue;
                best = Mathf.Max(best, Vector3.Dot(d, dir));
            }
        }
        return best;
    }

    // ================================================================== общее для обеих сцен
    class Map
    {
        public Scene Scene;
        public Transform Level, Buildings, Path;
        public Camera Cam;
        public Vector3 HallCenter;
        public System.Random Rng;
    }

    static Map NewMap(int seed)
    {
        var m = new Map { Rng = new System.Random(seed) };
        m.Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lighting = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (lighting != null) Lightmapping.lightingSettings = lighting;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        m.Cam = camGo.AddComponent<Camera>();
        m.Cam.fieldOfView = CamFov;
        m.Cam.nearClipPlane = 0.5f;
        m.Cam.farClipPlane = 450f;
        camGo.AddComponent<AudioListener>();

        m.Level = new GameObject("Level").transform;
        m.Buildings = Group("Buildings", m.Level);
        m.Path = new GameObject("Path").transform;

        // ратуша: выход Exit_Front ровно в конце путей, ворота смотрят на дорогу (-Z)
        var hall = Place(P("TownHall"), m.Buildings, Vector3.zero, 180f);
        var exit = FindDeep(hall.transform, "Exit_Front");
        var off = exit != null ? exit.position - hall.transform.position : new Vector3(0f, 0f, -9.2f);
        off.y = 0f;
        hall.transform.position = V3(HallExit) - off;
        m.HallCenter = hall.transform.position;
        // порталы: лицевая сторона (+Z модели) вдоль пути
        Place(P("Portal"), m.Buildings, V3(PortalLeft), 90f).name = "Portal_Left";
        Place(P("Portal"), m.Buildings, V3(PortalRight), -90f).name = "Portal_Right";
        Glow(m.Buildings, "Glow_Portal_Left", V3(PortalLeft) + new Vector3(1.5f, 2.2f, 0f), new Color(0.85f, 0.32f, 1f), 9f, 14f);
        Glow(m.Buildings, "Glow_Portal_Right", V3(PortalRight) + new Vector3(-1.5f, 2.2f, 0f), new Color(0.85f, 0.32f, 1f), 9f, 14f);
        Glow(m.Buildings, "Glow_TownHall", V3(HallExit) + new Vector3(0f, 3f, 1f), new Color(1f, 0.6f, 0.3f), 6f, 10f);

        PathPoints(m);
        FitCamera(m);
        return m;
    }

    static void Finish(Map m, string path, System.Action<Scene> after)
    {
        foreach (Transform t in m.Level) MarkStatic(t.gameObject);
        EditorSceneManager.SaveScene(m.Scene, path);
        ArcaneLook.SetupScene(m.Scene, AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath));
        if (RenderSettings.sun != null)
        {
            RenderSettings.sun.transform.rotation = Quaternion.LookRotation(-SunFrom.normalized);
            EditorUtility.SetDirty(RenderSettings.sun);
        }
        var vol = new GameObject("Map Volume").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 2f;
        vol.sharedProfile = MapProfile();
        after?.Invoke(m.Scene);
        EditorSceneManager.SaveScene(m.Scene);
        Debug.Log("[PathMaps] сцена сохранена: " + path);
    }

    /// Контрольные точки кривой врагов: Path/Route_Left, Path/Route_Right, P00 - у портала, последняя - у ратуши.
    static void PathPoints(Map m)
    {
        var icon = EditorGUIUtility.IconContent("sv_icon_dot13_pix16_gizmo").image as Texture2D;
        foreach (var (name, route) in new[] { ("Route_Left", RouteLeft), ("Route_Right", RouteRight) })
        {
            var r = Group(name, m.Path);
            var pts = Resample(route, PathPointStep);
            for (int i = 0; i < pts.Count; i++)
            {
                var go = new GameObject("P" + i.ToString("00"));
                go.transform.SetParent(r, false);
                go.transform.position = pts[i];
                if (icon != null) EditorGUIUtility.SetIconForObject(go, icon);
            }
        }
    }

    static List<Vector3> Resample(Vector2[] line, float step)
    {
        var pts = new List<Vector3>();
        for (int i = 0; i + 1 < line.Length; i++)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(line[i], line[i + 1]) / step - 0.01f));
            for (int k = 0; k < n; k++) pts.Add(V3(Vector2.Lerp(line[i], line[i + 1], k / (float)n)));
        }
        pts.Add(V3(line[line.Length - 1]));
        return pts;
    }

    /// Камера TD: наклон 55°, вся схема с порталами и ратушей в кадре 16:9, схема по центру кадра.
    static void FitCamera(Map m)
    {
        var cam = m.Cam;
        var pts = new List<Vector3>();
        void Add(Vector3 p, float r, float h)
        {
            pts.Add(p + Vector3.left * r); pts.Add(p + Vector3.right * r);
            pts.Add(p + Vector3.back * r); pts.Add(p + Vector3.forward * r);
            pts.Add(p + Vector3.up * h);
        }
        foreach (var v in Legs.SelectMany(l => l)) Add(V3(v), RoadHalf + 1f, 0f);
        Add(V3(PortalLeft), 7f, 9f);
        Add(V3(PortalRight), 7f, 9f);
        Add(m.HallCenter, 10f, 14f);

        var rot = Quaternion.Euler(CamPitch, 0f, 0f);
        var fwd = rot * Vector3.forward;
        cam.transform.rotation = rot;
        cam.aspect = 16f / 9f;
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        var target = new Vector3(b.center.x, 0f, b.center.z);
        for (int iter = 0; iter < 6; iter++)
        {
            float d = 20f;
            for (; d < 500f; d += 0.5f)
            {
                cam.transform.position = target - fwd * d;
                if (pts.All(p => Inside(cam.WorldToViewportPoint(p), 0.03f))) break;
            }
            Vector2 lo = Vector2.one * 9f, hi = -Vector2.one * 9f;
            foreach (var p in pts)
            {
                var vp = cam.WorldToViewportPoint(p);
                lo = Vector2.Min(lo, vp);
                hi = Vector2.Max(hi, vp);
            }
            var off = (lo + hi) * 0.5f - new Vector2(0.5f, 0.5f);
            float worldPerVp = 2f * d * Mathf.Tan(CamFov * 0.5f * Mathf.Deg2Rad);
            target += Vector3.right * off.x * worldPerVp * cam.aspect
                      + Vector3.forward * off.y * worldPerVp / Mathf.Sin(CamPitch * Mathf.Deg2Rad);
        }
        cam.ResetAspect();
    }

    static bool Inside(Vector3 vp, float margin) =>
        vp.z > 0f && vp.x > margin && vp.x < 1f - margin && vp.y > margin && vp.y < 1f - margin;

    /// Прямоугольник земли (XZ), который видит камера, плюс запас.
    static Rect GroundFootprint(Camera cam, float margin)
    {
        cam.aspect = 16f / 9f;
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var c in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
        {
            var ray = cam.ViewportPointToRay(new Vector3(c.x, c.y, 0f));
            float t = ray.direction.y < -0.05f ? -ray.origin.y / ray.direction.y : 300f;
            var p = ray.origin + ray.direction * Mathf.Min(t, 300f);
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
            z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
        }
        cam.ResetAspect();
        return Rect.MinMaxRect(x0 - margin, z0 - margin, x1 + margin, z1 + margin);
    }

    static IEnumerable<(Vector2 a, Vector2 b)> Segments()
    {
        var seen = new HashSet<(Vector2, Vector2)>();
        foreach (var leg in Legs)
            for (int i = 0; i + 1 < leg.Length; i++)
                if (seen.Add((leg[i], leg[i + 1])))
                    yield return (leg[i], leg[i + 1]);
    }

    /// Клетки 1x1 м под плиты: по 3 вдоль оси каждого отрезка, концы продлены на клетку - углы закрыты квадратом.
    static HashSet<Vector2Int> RoadCells()
    {
        var cells = new HashSet<Vector2Int>();
        foreach (var (a, b) in Segments())
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            float len = Vector2.Distance(a, b);
            for (float s = -1f; s <= len + 1.01f; s += 1f)
                for (int k = -1; k <= 1; k++)
                {
                    var c = a + d * s + n * k;
                    cells.Add(new Vector2Int(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y)));
                }
        }
        return cells;
    }

    static float DistToRoads(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var (a, b) in Segments())
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    /// Порталы и ратуша с выходами (двор 14.4 м, выходы на 9.2 м от центра) - без декора.
    static bool NearBuildings(Map m, Vector2 p, float r)
    {
        if ((p - PortalLeft).magnitude < 7f + r || (p - PortalRight).magnitude < 7f + r) return true;
        float dx = p.x - m.HallCenter.x, dz = p.y - m.HallCenter.z;
        return Mathf.Abs(dx) < 10.5f + r && dz > -11f - r && dz < 9f + r;
    }

    // ------------------------------------------------------------------ материалы и префабы
    /// Поверх PP_Arcane: схема целиком в кадре, до ратуши ~110 м - размытие начинается дальше.
    static VolumeProfile MapProfile()
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MapProfilePath);
        if (p != null) return p;
        p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, MapProfilePath);
        var dof = p.Add<DepthOfField>(false);
        dof.name = nameof(DepthOfField);
        dof.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(150f);
        dof.gaussianEnd.Override(320f);
        dof.gaussianMaxRadius.Override(1f);
        dof.highQualitySampling.Override(false);
        AssetDatabase.AddObjectToAsset(dof, p);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    /// Земля скалистой карты: те же тайлы, что у травы, но камень и немного мха (цвета правятся в инспекторе).
    static Material RockGround()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(RockGroundPath);
        if (m != null) return m;
        var grass = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Arcane_Grass.mat");
        if (grass == null) throw new System.Exception("Нет " + MatDir + "/M_Arcane_Grass.mat (Tools > P1 Art > Arcane Look > Apply All)");
        m = new Material(grass) { name = "M_Arcane_RockGround" };
        m.SetColor("_TriColor0", new Color(0.53f, 0.52f, 0.53f));
        m.SetColor("_TriColor1", new Color(0.58f, 0.57f, 0.56f));
        m.SetColor("_TriColor2", new Color(0.49f, 0.49f, 0.53f));
        m.SetColor("_TriColor3", new Color(0.53f, 0.54f, 0.49f));
        AssetDatabase.CreateAsset(m, RockGroundPath);
        return m;
    }

    /// Модели набора гор и троп: материалы атласа и обводки, префаб в Prefabs/Environment (если ещё нет).
    static void EnsureKitPrefabs()
    {
        var toon = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Toon_Atlas.mat");
        var outline = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Outline.mat");
        if (toon == null || outline == null) throw new System.Exception("Нет M_Toon_Atlas / M_Outline (Tools > P1 Art > Setup All)");
        EnsureFolder(PrefabDir);
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { EnvDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!KitPrefixes.Any(name.StartsWith)) continue;
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            var map = mi.GetExternalObjectMap();
            bool changed = false;
            foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var target = mat.name.StartsWith("M_Outline") ? outline : toon;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name);
                if (map.TryGetValue(id, out var cur) && cur == target) continue;
                mi.AddRemap(id, target);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
            string prefabPath = PrefabDir + "/" + name.Substring(3) + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
        }
    }

    static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

    /// Префаб окружения по имени без SM_ (TownHall, Portal, Cliff_01, Mtn_Wall_01, Trail_Node_01...).
    static GameObject P(string name)
    {
        if (Cache.TryGetValue(name, out var go) && go != null) return go;
        go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");
        if (go == null) throw new System.Exception("Нет префаба " + PrefabDir + "/" + name + ".prefab");
        Cache[name] = go;
        return go;
    }

    static IEnumerable<string> KitNames(string prefix) =>
        Directory.GetFiles(PrefabDir, prefix + "*.prefab").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n);

    // ------------------------------------------------------------------ помощники
    static Vector3 V3(Vector2 v) => new Vector3(v.x, 0f, v.y);
    static float Sq(float x) => x * x;
    static float Snap(float v) => Mathf.Floor(v / 24f) * 24f;
    static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

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

    static void Glow(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static void MarkStatic(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponentInParent<Animator>() != null || t.GetComponent<Light>() != null) continue;   // портал анимирован
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
