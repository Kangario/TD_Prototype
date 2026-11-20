// Сцены-карты по схеме путей (рисунок-прототип): два портала, пути сходятся у развилки и идут к ратуше.
//   Map_RockyTerrain - дорога из плит по скалистой земле, между путями выступы и валуны, по краю обрывы и горы;
//   Map_RockTrails   - сами пути - скальные тропы над пропастью: та же схема, увеличенная в 2.5 раза. Столбы на
//                      поворотах и развилке, на длинных участках промежуточные опоры, между ними настилы-мосты,
//                      по тропам плитка, сбоку островки под героев (5 разных моделей), порталы и ратуша на парящих
//                      площадках, вокруг паутина, острова и обломки, внизу ровная пелена тумана вместо облачков.
// Схема в метрах - массивы ниже (1 px рисунка ~ 0.07 м). Контрольные точки кривой врагов - Path/Route_Left и
// Path/Route_Right (от портала до выхода ратуши, не реже 3 м), их потом прочитает LevelAuthoring.
// Места под героев на тропах - HeroSlots/Slot_NN (центр верха островка, y = 0; пустышки, в игре не видны).
// Свет и пост-обработку ставит ArcaneLook.SetupScene, затем для карт два отличия: солнце светит со стороны камеры
// (камера TD смотрит в +Z, а ArcaneLook настроен на витрину с камерой в -Z), и свой профиль поверх PP_Arcane
// (Assets/Settings/PP_PathMaps.asset - размытие дальше; PP_RockTrails.asset - размытие ещё дальше, без зерна
// и хроматической аберрации).
// Меню: Tools > P1 Art > Path Maps. Повторный запуск пересобирает сцену целиком - ручные правки в ней пропадут.
// Batchmode: -executeMethod PathMaps.RunBatch (обе) или PathMaps.RunBatchTrails (только тропы)
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
    const string TrailsVeilPath = MatDir + "/M_Trails_Veil.mat";
    const string LightingPath = "Assets/Scenes/ArtShowcase_Lighting.lighting";
    const string ProfilePath = "Assets/Settings/PP_Arcane.asset";
    const string MapProfilePath = "Assets/Settings/PP_PathMaps.asset";
    const string TrailsProfilePath = "Assets/Settings/PP_RockTrails.asset";
    static readonly Vector3 SunFrom = new Vector3(-0.68f, 0.64f, -0.36f);   // слева и со стороны камеры
    static readonly string[] KitPrefixes = { "SM_Mtn_", "SM_Ruin_", "SM_Web_", "SM_Trail_" };

    // ------------------------------------------------------------------ схема путей
    /// Схема: точки поворотов в метрах, вид сверху как на рисунке (X вправо, Z вверх по рисунку - от камеры).
    public class Scheme
    {
        public Vector2 PortalLeft, PortalRight, Junction, HallExit, StepA, StepB;
        public Vector2[] LeftLeg, RightLeg, HallLeg;
        public Vector2[][] Legs => new[] { LeftLeg, RightLeg, HallLeg };
        /// Пути врагов от портала до ратуши (ломаные по схеме).
        public Vector2[] RouteLeft => LeftLeg.Concat(HallLeg.Skip(1)).ToArray();
        public Vector2[] RouteRight => RightLeg.Concat(HallLeg.Skip(1)).ToArray();

        public static Scheme Build(Vector2 portalLeft, Vector2 portalRight, Vector2 junction, Vector2 hallExit,
                                   Vector2 stepA, Vector2 stepB, Vector2[] leftMid, Vector2[] rightMid)
        {
            var s = new Scheme
            {
                PortalLeft = portalLeft, PortalRight = portalRight, Junction = junction, HallExit = hallExit,
                StepA = stepA, StepB = stepB
            };
            s.LeftLeg = new[] { portalLeft }.Concat(leftMid).Concat(new[] { junction }).ToArray();
            s.RightLeg = new[] { portalRight }.Concat(rightMid).Concat(new[] { junction }).ToArray();
            s.HallLeg = new[] { junction, stepA, stepB, hallExit };
            return s;
        }

        public Scheme Scaled(float k) => Build(PortalLeft * k, PortalRight * k, Junction * k, HallExit * k, StepA * k,
                                               StepB * k, LeftLeg.Skip(1).Take(LeftLeg.Length - 2).Select(p => p * k).ToArray(),
                                               RightLeg.Skip(1).Take(RightLeg.Length - 2).Select(p => p * k).ToArray());
    }

    // Скалистая карта: все точки на сетке x.5 м - плиты дороги 1x1 м ложатся по 3 в ширину ровно по оси пути.
    public static readonly Scheme Rocky = Scheme.Build(
        new Vector2(-38.5f, -3.5f),                    // левый кружок - портал
        new Vector2(38.5f, -20.5f),                    // правый кружок - портал
        new Vector2(-6.5f, -15.5f),                    // развилка: пути сходятся
        new Vector2(-9.5f, 18.5f),                     // квадрат: выход Exit_Front ратуши
        new Vector2(-6.5f, 10.5f), new Vector2(-9.5f, 10.5f),   // ступенька
        new[] { new Vector2(-14.5f, -3.5f), new Vector2(-14.5f, -15.5f) },
        new[] { new Vector2(9.5f, -20.5f), new Vector2(9.5f, 1.5f), new Vector2(1.5f, 1.5f), new Vector2(1.5f, -15.5f) });

    // Тропы: та же схема в 2.5 раза больше - участки длиннее (левый путь ~200 м, правый ~300 м), между
    // параллельными тропами 20 м под пятачки героев. Порталы - точки выхода врагов, сам портал стоит за ней.
    public const float TrailsScale = 2.5f;
    public static readonly Scheme Trails = Rocky.Scaled(TrailsScale);

    // Текущая схема для общих помощников (Segments, DistToRoads, камера, точки пути).
    static Scheme S = Rocky;

    // старые имена (схема скалистой карты)
    public static Vector2 PortalLeft => Rocky.PortalLeft;
    public static Vector2 PortalRight => Rocky.PortalRight;
    public static Vector2 Junction => Rocky.Junction;
    public static Vector2 HallExit => Rocky.HallExit;
    public static Vector2[] RouteLeft => Rocky.RouteLeft;
    public static Vector2[] RouteRight => Rocky.RouteRight;

    const float PathPointStep = 3f;    // Catmull-Rom через частые точки держится ломаной, углы скругляет в пределах дороги
    const float RoadHalf = 1.5f;       // дорога из плит 3 м (логике нужно pathWidth 2)
    const float CamPitch = 55f, CamFov = 40f;
    static readonly int GroundLayer = 6;   // ProjectSetup.GroundLayer

    // тропы
    const float PortalBack = 6.5f;     // портал стоит за точкой выхода врагов (дальше от камеры), лицом к камере
    const float PlatformShift = 3.2f;  // центр площадки портала - между точкой выхода и порталом
    const float DeckHalf = 2.5f;       // половина ширины настила вместе с козырьком травы
    const float IsletOverlap = 0.7f;   // островок заходит на кромку настила
    const float PaveAlong = 1.5f;      // ряд плитки вдоль дороги
    const float PaveY = -0.03f;        // плитка утоплена: встаёт и на настил (верх -0.06), и на узел (верх 0)
    const float MaxSpan = 18f;         // длиннее - ставится промежуточная опора
    const float VeilY = -40f;          // поверхность пелены: островки и настилы видны целиком, в пелену уходят столбы
    const float VeilThickness = 45f;   // ниже на столько пелена уже сплошная
    static readonly Color AbyssMist = new Color(0.56f, 0.68f, 0.84f);       // фон пропасти, как было с облачками
    static readonly Color VeilColor = new Color(0.66f, 0.76f, 0.90f);
    static readonly string[] Islets = { "Trail_Islet_01", "Trail_Islet_02", "Trail_Islet_03", "Trail_Islet_04", "Trail_Islet_05" };

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

    public static void RunBatchTrails()
    {
        try
        {
            Prepare();
            BuildRockTrails();
            AssetDatabase.SaveAssets();
        }
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
        S = Rocky;
        var m = NewMap(21, false);
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

        Finish(m, RockyScene, MapProfile(), null);
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
    public static void BuildRockTrails()
    {
        S = Trails;
        var m = NewMap(37, true);
        var trails = Group("Trails", m.Level);
        var paving = Group("Paving", m.Level);
        var islets = Group("HeroIslets", m.Level);
        var backdrop = Group("Backdrop", m.Level);
        var slots = new GameObject("HeroSlots").transform;

        // площадки: под порталом (портал в глубине, точка выхода врагов перед ним) и под ратушей
        var supports = new Dictionary<Vector2, (GameObject go, float insert)>();
        foreach (var spawn in new[] { S.PortalLeft, S.PortalRight })
            supports[spawn] = (Place(P("Trail_Platform_01"), trails, V3(spawn) + Vector3.forward * PlatformShift, 0f), 2.2f);
        supports[S.HallExit] = (Place(P("Trail_Platform_02"), trails, m.HallCenter, 180f), 2.2f);

        // столбы на поворотах, на развилке большой, на ступеньке перед ратушей - один длинный на обе точки
        foreach (var v in S.Legs.SelectMany(l => l).Distinct())
        {
            if (supports.ContainsKey(v) || v == S.StepA || v == S.StepB) continue;
            string name = v == S.Junction ? "Trail_Node_03" : "Trail_Node_0" + (m.Rng.Next(2) + 1);
            float yaw = 90f * m.Rng.Next(4) + Rand(m.Rng, -4f, 4f);
            supports[v] = (Place(P(name), trails, V3(v), yaw), 1.6f);
        }
        var step = Place(P("Trail_Node_04"), trails, V3((S.StepA + S.StepB) * 0.5f), m.Rng.Next(2) * 180f);
        supports[S.StepA] = (step, 1.6f);
        supports[S.StepB] = (step, 1.6f);

        // настилы-мосты; длинный участок делится промежуточными опорами на пролёты не длиннее MaxSpan
        var decks = KitNames("Trail_Deck_").Select(n => (name: n, len: float.Parse(n.Substring("Trail_Deck_".Length)))).ToArray();
        var piers = new List<(Vector2 p, Vector2 a, Vector2 b)>();
        var spans = new List<(Vector2 a, Vector2 b, Vector2 sa, Vector2 sb)>();   // открытые пролёты и их участок
        foreach (var (a, b) in Segments())
        {
            var (ga, ia) = supports[a];
            var (gb, ib) = supports[b];
            if (ga == gb) continue;                          // ступенька внутри одного столба
            var d2 = (b - a).normalized;
            var d = V3(d2);
            float len = Vector2.Distance(a, b);
            float e0 = EdgeAlong(ga, V3(a), d), e1 = len - EdgeAlong(gb, V3(b), -d);
            float free = e1 - e0;
            const float pierLen = 6.4f;
            int k = Mathf.Max(0, Mathf.CeilToInt((free - MaxSpan) / (MaxSpan + pierLen)));
            float span = (free - k * pierLen) / (k + 1);
            var chain = new List<(GameObject go, Vector3 origin, float insert)> { (ga, V3(a), ia) };
            for (int i = 0; i < k; i++)
            {
                var p = a + d2 * (e0 + span * (i + 1) + pierLen * (i + 0.5f));
                var pier = Place(P("Trail_Node_0" + (m.Rng.Next(2) + 1)), trails, V3(p), 90f * m.Rng.Next(4) + Rand(m.Rng, -3f, 3f));
                pier.name = "Pier_" + pier.name;
                chain.Add((pier, V3(p), 1.6f));
                piers.Add((p, a, b));
            }
            chain.Add((gb, V3(b), ib));
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                var (g0, o0, n0) = chain[i];
                var (g1, o1, n1) = chain[i + 1];
                var start = o0 + d * (EdgeAlong(g0, o0, d) - n0);
                var end = o1 - d * (EdgeAlong(g1, o1, -d) - n1);
                float need = Vector3.Dot(end - start, d);
                if (need < 1f) continue;
                var deck = decks.OrderBy(t => Mathf.Abs(Mathf.Log(need / t.len))).First();
                float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg + 180f * m.Rng.Next(2);
                var go = Place(P(deck.name), trails, (start + end) * 0.5f, yaw);
                go.transform.localScale = new Vector3(need / deck.len, 1f, 1f);
                var sa = start + d * n0;
                var sb = end - d * n1;
                spans.Add((a, b, new Vector2(sa.x, sa.z), new Vector2(sb.x, sb.z)));
            }
        }

        LayPaving(m, paving);
        var taken = HeroIslets(m, islets, slots, supports, piers, spans);
        TrailsBackdrop(m, backdrop, taken);

        Finish(m, TrailsScene, TrailsProfile(), scene =>
        {
            // пропасть: тот же светлый фон и дымка вдали, что были, но облачков нет - снизу ровная пелена.
            // Камера в 2.5 раза дальше - и дымка начинается в 2.5 раза дальше.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = AbyssMist;
            RenderSettings.fogStartDistance = 150f * TrailsScale;
            RenderSettings.fogEndDistance = 480f * TrailsScale;
            m.Cam.backgroundColor = AbyssMist;
            m.Cam.farClipPlane = 650f * TrailsScale;
            m.Cam.gameObject.AddComponent<CameraShadowDistance>().distance = 420f;
            MistVeil();
        });
    }

    /// Плитка: на каждом повороте, развилке и у начала/конца пути квадрат 3x3 м, между ними ряды 1.5 м
    /// (2 и 3 плиты вперевязку), ряды чуть растянуты, чтобы ровно лечь в участок.
    static void LayPaving(Map m, Transform parent)
    {
        var corners = new HashSet<Vector2>();
        int row = 0;
        foreach (var (a, b) in Segments())
        {
            foreach (var v in new[] { a, b })
                if (corners.Add(v))
                    Place(P("Trail_Paving_Corner_01"), parent, V3(v) + Vector3.up * PaveY, 90f * m.Rng.Next(4)).name = "Paving_Corner";
            var d2 = (b - a).normalized;
            var d = V3(d2);
            float len = Vector2.Distance(a, b) - 3f;
            if (len < 0.5f) continue;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / PaveAlong));
            float k = len / (n * PaveAlong);
            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++, row++)
            {
                string name = row % 2 == 0 ? (m.Rng.NextDouble() < 0.15 ? "Trail_Paving_03" : "Trail_Paving_01") : "Trail_Paving_02";
                var pos = V3(a) + d * (1.5f + (i + 0.5f) * PaveAlong * k) + Vector3.up * PaveY;
                var go = Place(P(name), parent, pos, yaw + 180f * m.Rng.Next(2));
                go.transform.localScale = new Vector3(k, 1f, 1f);
            }
        }
    }

    /// Островки под героев: во внутренних углах поворотов (герой оттуда достаёт два участка), у промежуточных
    /// опор и посреди длинных пролётов. Пять разных моделей, поворот случайный; островок придвигается к тропе по
    /// своей реальной кромке, чтобы зайти на настил на IsletOverlap. От чужих троп, порталов, ратуши и друг от друга -
    /// с зазором. Центр верха - HeroSlots/Slot_NN (пустышка для логики, в игре не видна).
    static List<(Vector2 p, float r)> HeroIslets(Map m, Transform parent, Transform slots,
                                                Dictionary<Vector2, (GameObject go, float insert)> supports,
                                                List<(Vector2 p, Vector2 a, Vector2 b)> piers,
                                                List<(Vector2 a, Vector2 b, Vector2 sa, Vector2 sb)> spans)
    {
        var cands = new List<(Vector2 basePt, Vector2 away, (Vector2 a, Vector2 b)[] own, int prio)>();
        // внутренние углы поворотов
        foreach (var leg in S.Legs)
            for (int i = 1; i + 1 < leg.Length; i++)
            {
                var v = leg[i];
                if (v == S.StepA || v == S.StepB || v == S.Junction) continue;
                var u1 = (leg[i - 1] - v).normalized;
                var u2 = (leg[i + 1] - v).normalized;
                if (Mathf.Abs(Vector2.Dot(u1, u2)) > 0.2f) continue;
                cands.Add((v + (u1 + u2) * 3.5f, (u1 + u2).normalized, new[] { (leg[i - 1], v), (v, leg[i + 1]) }, 0));
            }
        // у промежуточных опор с обеих сторон
        foreach (var (p, a, b) in piers)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            cands.Add((p + n * 3f, n, new[] { (a, b) }, 1));
            cands.Add((p - n * 3f, -n, new[] { (a, b) }, 1));
        }
        // посреди длинных пролётов
        foreach (var (a, b, sa, sb) in spans)
        {
            if (Vector2.Distance(sa, sb) < 13f) continue;
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            var mid = (sa + sb) * 0.5f;
            cands.Add((mid + n * 3f, n, new[] { (a, b) }, 2));
            cands.Add((mid - n * 3f, -n, new[] { (a, b) }, 2));
        }

        var all = Segments().ToList();
        var nodes = supports.Values.Select(s => s.go).Distinct().Where(g => g.name.StartsWith("Trail_Node"))
            .Select(g => XZ(g.transform.position)).Concat(piers.Select(q => q.p)).ToList();
        var platforms = supports.Values.Select(s => s.go).Distinct().Where(g => g.name.StartsWith("Trail_Platform"))
            .Select(g => (p: XZ(g.transform.position), r: g.name.EndsWith("02") ? 15.5f : 9.5f)).ToList();
        var accepted = new List<(Vector2 p, float r, Vector2 top)>();
        foreach (var cand in cands.OrderBy(t => t.prio))         // сортировка устойчивая: внутри приоритета - по пути
        {
            if (accepted.Any(q => (q.p - cand.basePt).magnitude < 10f)) continue;
            // модель: случайная, а если не влезла - самая маленькая
            var tryNames = new[] { Islets[m.Rng.Next(Islets.Length)], "Trail_Islet_03" }.Distinct();
            foreach (var name in tryNames)
            {
                var go = Place(P(name), parent, V3(cand.basePt + cand.away * 4f), Rand(m.Rng, 0f, 360f), Rand(m.Rng, 0.9f, 1.1f));
                var rim = TopRim(go, -0.6f);
                // придвинуть к своей тропе: ближайшая точка кромки - на DeckHalf - IsletOverlap от оси
                for (int it = 0; it < 3; it++)
                {
                    float near = rim.Min(q => cand.own.Min(s => DistToSeg(q, s.a, s.b)));
                    float shift = (DeckHalf - IsletOverlap) - near;
                    if (Mathf.Abs(shift) < 0.05f) break;
                    var delta = cand.away * shift;
                    go.transform.position += V3(delta);
                    for (int q = 0; q < rim.Count; q++) rim[q] += delta;
                }
                var c = XZ(go.transform.position);
                float r = rim.Max(q => (q - c).magnitude);
                var own = new HashSet<(Vector2, Vector2)>(cand.own.Select(s => (s.a, s.b)));
                bool ok = all.Where(s => !own.Contains((s.a, s.b))).All(s => rim.All(q => DistToSeg(q, s.a, s.b) > DeckHalf + 1.2f))
                          && !NearBuildings(m, c, r)
                          && platforms.All(pl => (pl.p - c).magnitude > pl.r + r - 1f)
                          && nodes.All(nd => (nd - c).magnitude > 3.4f + r - 2.2f)
                          && accepted.All(q => (q.p - c).magnitude > q.r + r + 2.5f);
                if (!ok)
                {
                    Object.DestroyImmediate(go);
                    continue;
                }
                var top = TopRim(go, -0.3f);
                var centre = top.Aggregate(Vector2.zero, (s, q) => s + q) / Mathf.Max(1, top.Count);
                accepted.Add((c, r, centre));
                go.name = "Islet_" + accepted.Count.ToString("00");
                IsletProps(m, go.transform, centre, cand.away, r);
                break;
            }
        }

        var icon = EditorGUIUtility.IconContent("sv_icon_dot5_pix16_gizmo").image as Texture2D;
        for (int i = 0; i < accepted.Count; i++)
        {
            var marker = new GameObject("Slot_" + (i + 1).ToString("00"));
            marker.transform.SetParent(slots, false);
            marker.transform.position = V3(accepted[i].top);
            if (icon != null) EditorGUIUtility.SetIconForObject(marker, icon);
        }
        Debug.Log("[PathMaps] островков под героев: " + accepted.Count);
        return accepted.Select(q => (q.p, q.r)).ToList();
    }

    /// На части островков - куст или камень из набора проекта у дальнего от тропы края (середина свободна под героя).
    static void IsletProps(Map m, Transform islet, Vector2 centre, Vector2 away, float r)
    {
        if (m.Rng.NextDouble() > 0.55) return;
        bool bush = m.Rng.NextDouble() < 0.5;
        string name = bush ? "Bush_01" : "Rock_0" + (m.Rng.Next(3) + 1);     // только зелёный куст: ягоды и осень - пёстро
        float ang = Rand(m.Rng, -50f, 50f) * Mathf.Deg2Rad;
        var dir = new Vector2(away.x * Mathf.Cos(ang) - away.y * Mathf.Sin(ang), away.x * Mathf.Sin(ang) + away.y * Mathf.Cos(ang));
        var p = centre + dir * (r * 0.62f);
        Place(P(name), islet, V3(p), Rand(m.Rng, 0f, 360f), bush ? Rand(m.Rng, 1.3f, 1.9f) : Rand(m.Rng, 0.9f, 1.4f));
    }

    /// Точки верхней кромки модели (XZ) - для подгонки к тропе и проверки зазоров.
    static List<Vector2> TopRim(GameObject go, float minY, float maxY = 0.3f)
    {
        var pts = new List<Vector2>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var w = mf.transform.TransformPoint(v);
                if (w.y >= minY && w.y <= maxY) pts.Add(new Vector2(w.x, w.z));
            }
        }
        if (pts.Count == 0) pts.Add(XZ(go.transform.position));
        return pts;
    }

    /// Фон как был: старые столбы троп без настилов, парящие острова, обломки, скальная паутина и пики.
    /// Схема в 2.5 раза больше - расстановка раздвинута во столько же раз, дальние массивы во столько же увеличены,
    /// столбы и обломки - немного (они ближе к масштабу троп). Низ всего этого скрывает пелена.
    static void TrailsBackdrop(Map m, Transform parent, List<(Vector2 p, float r)> islets)
    {
        const float k = TrailsScale;
        var rng = new System.Random(371);
        var c = new Vector2(0f, 7.5f) * k;
        var taken = new List<(Vector2 p, float r)>(islets);
        // обломки старых троп: столбы без настилов, чуть ниже и с наклоном
        for (int attempt = 0, n = 0; attempt < 800 && n < 12; attempt++)
        {
            var p = c + new Vector2(Rand(rng, -62f, 62f), Rand(rng, -44f, 44f)) * k;
            if (DistToRoads(p) < 12f || NearBuildings(m, p, 10f) || taken.Any(q => (q.p - p).magnitude < q.r + 14f)) continue;
            float s = Rand(rng, 0.9f, 1.4f);
            var go = Place(P("Trail_Node_0" + (rng.Next(2) + 1)), parent, new Vector3(p.x, Rand(rng, -9f, -2f), p.y), 0f, s);
            go.transform.rotation = Quaternion.Euler(Rand(rng, -6f, 6f), Rand(rng, 0f, 360f), Rand(rng, -6f, 6f));
            taken.Add((p, 4f * s));
            n++;
        }
        // парящие острова по краю
        for (int i = 0; i < 7; i++)
        {
            float ang = (i + Rand(rng, -0.25f, 0.25f)) / 7f * 360f * Mathf.Deg2Rad;
            var p = c + new Vector2(Mathf.Cos(ang) * 74f, Mathf.Sin(ang) * 54f) * k;
            Place(P("Mtn_Island_01"), parent, new Vector3(p.x, Rand(rng, -14f, 12f), p.y), Rand(rng, 0f, 360f),
                  Rand(rng, 0.45f, 0.8f) * k * 0.8f);
        }
        // обломки в воздухе: над картой только ниже троп, выше - за краем
        string[] shards = { "Mtn_Shard_01", "Mtn_Shard_02", "Mtn_Shard_03", "Mtn_Shards_01" };
        for (int attempt = 0, n = 0; attempt < 900 && n < 24; attempt++)
        {
            var p = c + new Vector2(Rand(rng, -80f, 80f), Rand(rng, -58f, 58f)) * k;
            if (DistToRoads(p) < 9f || NearBuildings(m, p, 6f) || taken.Any(q => (q.p - p).magnitude < q.r + 6f)) continue;
            bool inside = Sq(p.x / (50f * k)) + Sq((p.y - c.y) / (36f * k)) < 1f;
            string name = shards[rng.Next(shards.Length)];
            float y = inside ? Rand(rng, -16f, -5f) : Rand(rng, -14f, 14f);
            float s = (name == "Mtn_Shards_01" ? Rand(rng, 0.5f, 0.85f) : Rand(rng, 0.8f, 2.0f)) * 1.6f;
            var go = Place(P(name), parent, new Vector3(p.x, y, p.y), 0f, s);
            go.transform.rotation = Quaternion.Euler(Rand(rng, -30f, 30f), Rand(rng, 0f, 360f), Rand(rng, -30f, 30f));
            n++;
        }
        TrailsGiants(m, parent, rng);
    }

    /// Великаны для масштаба: каменные соты (сеть-стена), кольца руин, развилка и мост-арка поднимаются из пелены,
    /// огромные парящие острова с деревьями в десятки метров, большие обломки. Стоят в поле вокруг карты (сзади за
    /// ратушей и по бокам), высота подбирается по камере: верх чуть выходит за край кадра - так видно, что они
    /// огромные, и они не заслоняют тропы (внизу кадра, у камеры, высокого ничего нет).
    static void TrailsGiants(Map m, Transform parent, System.Random rng)
    {
        var giants = Group("Giants", parent);
        var cam = m.Cam;
        cam.aspect = 16f / 9f;
        // высота, выше которой точка (x, z) уходит за верх кадра
        float VisTop(float x, float z)
        {
            float lo = VeilY, hi = 400f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (cam.WorldToViewportPoint(new Vector3(x, mid, z)).y < 1f) lo = mid; else hi = mid;
            }
            return lo;
        }
        void Rise(string model, float x, float z, float yaw, float scale, float over)
        {
            PlaceTop(P(model), giants, new Vector3(x, 0f, z), yaw, scale, VisTop(x, z) + over);
        }
        // каменные соты: длинная стена за ратушей и две по углам, развернуты к карте
        Rise("Web_Lattice_01", -10f, 150f, 180f, 2.3f, 25f);
        Rise("Web_Lattice_01", -185f, 95f, 130f, 1.8f, 20f);
        Rise("Web_Lattice_01", 190f, 80f, -130f, 1.8f, 20f);
        // кольца руин, развилка, мост-арка
        Rise("Ruin_Ring_01", -105f, 120f, 15f, 1.6f, 10f);
        Rise("Ruin_Ring_03", 120f, 125f, -25f, 1.5f, 5f);
        Rise("Web_Branch_01", 175f, 20f, -60f, 1.8f, 8f);
        Rise("Web_Bridge_01", -175f, 10f, 70f, 1.5f, -20f);
        // огромные парящие острова с деревьями-великанами: верх острова ниже верха кадра, кроны уходят за край
        void Isl(string model, float x, float z, float yaw, float scale, int trees, float below)
        {
            GiantIsland(giants, rng, model, new Vector3(x, Mathf.Max(VeilY + 18f, VisTop(x, z) - below), z), yaw, scale, trees);
        }
        Isl("Mtn_Island_01", -150f, 140f, 20f, 3.0f, 5, 30f);
        Isl("Mtn_Island_01", 160f, 150f, 200f, 2.6f, 4, 28f);
        Isl("Mtn_Island_01", 35f, 125f, 120f, 1.8f, 3, 26f);
        Isl("Trail_Platform_02", -170f, 40f, 0f, 1.4f, 3, 45f);
        Isl("Trail_Platform_01", 170f, -45f, 0f, 1.6f, 2, 60f);
        // большие обломки между ними, на разной высоте
        string[] shards = { "Mtn_Shard_01", "Mtn_Shard_02", "Mtn_Shard_03", "Mtn_Shards_01" };
        var spots = new[]
        {
            new Vector2(-60f, 150f), new Vector2(80f, 140f), new Vector2(-175f, 120f), new Vector2(180f, 115f),
            new Vector2(-150f, -20f), new Vector2(150f, 45f), new Vector2(-5f, 175f), new Vector2(205f, 160f)
        };
        foreach (var p in spots)
        {
            string name = shards[rng.Next(shards.Length)];
            float s = name == "Mtn_Shards_01" ? Rand(rng, 1.6f, 2.4f) : Rand(rng, 4f, 7f);
            float y = Mathf.Max(VeilY + 10f, VisTop(p.x, p.y) - Rand(rng, 15f, 45f));
            var go = Place(P(name), giants, new Vector3(p.x, y, p.y), 0f, s);
            go.transform.rotation = Quaternion.Euler(Rand(rng, -35f, 35f), Rand(rng, 0f, 360f), Rand(rng, -35f, 35f));
        }
        cam.ResetAspect();
    }

    /// Парящий остров (верх модели на y = pos.y) и на нём деревья в 30-60 м - зелёные лиственные и ели.
    static void GiantIsland(Transform parent, System.Random rng, string model, Vector3 pos, float yaw, float scale, int trees)
    {
        var island = Place(P(model), parent, pos, yaw, scale);
        var rim = TopRim(island, pos.y - 0.6f * scale, pos.y + 0.3f * scale);
        var c = XZ(pos);
        string[] kinds = { "Tree_04", "Tree_04", "Tree_01", "Tree_03", "Tree_05" };
        var used = new List<Vector2>();
        for (int i = 0, tries = 0; i < trees && tries < 60; tries++)
        {
            if (rim.Count == 0) break;
            var edge = rim[rng.Next(rim.Count)];
            var p = Vector2.Lerp(c, edge, Rand(rng, 0.1f, 0.6f));
            if (used.Any(q => (q - p).magnitude < 4f * scale)) continue;
            used.Add(p);
            Place(P(kinds[rng.Next(kinds.Length)]), parent, new Vector3(p.x, pos.y - 0.1f, p.y), Rand(rng, 0f, 360f),
                  Rand(rng, 6.5f, 9.5f));
            i++;
        }
    }

    /// Поставить так, чтобы верх модели оказался на высоте top.
    static GameObject PlaceTop(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale, float top)
    {
        var go = Place(prefab, parent, pos, yaw, scale);
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return go;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.up * (top - b.max.y);
        return go;
    }

    /// Пелена тумана: одна плоскость под тропами вместо облачков (шейдер P1/MistVeil растворяет в ней всё, что ниже).
    static void MistVeil()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Mist Veil";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(new Vector3(0f, VeilY, 7.5f * TrailsScale), Quaternion.Euler(90f, 0f, 0f));
        go.transform.localScale = new Vector3(3200f, 3200f, 1f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = TrailsVeil();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static Material TrailsVeil()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TrailsVeilPath);
        if (mat != null) return mat;                 // уже есть - правки в инспекторе не затираем
        var shader = Shader.Find("P1/MistVeil");
        if (shader == null) throw new System.Exception("Нет шейдера P1/MistVeil (Assets/Art/Shaders/P1_MistVeil.shader)");
        mat = new Material(shader) { name = "M_Trails_Veil" };
        mat.SetColor("_Color", VeilColor);
        mat.SetColor("_DeepColor", AbyssMist);
        mat.SetFloat("_Thickness", VeilThickness);
        mat.SetFloat("_NoiseScale", 0.004f);          // мягкие пятна ~250 м - пелена живая, но без ряби
        mat.SetFloat("_NoiseStrength", 0.07f);
        var atlas = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Toon_Atlas.mat");
        if (atlas != null && atlas.HasProperty("_BrushTex")) mat.SetTexture("_NoiseTex", atlas.GetTexture("_BrushTex"));
        AssetDatabase.CreateAsset(mat, TrailsVeilPath);
        return mat;
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
        public List<Vector2> PortalSpots = new List<Vector2>();
        public float PortalClear = 7f;
        public System.Random Rng;
    }

    /// trails: порталы лицом к камере за точкой выхода врагов (на скалистой карте - вдоль пути).
    static Map NewMap(int seed, bool trails)
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
        hall.transform.position = V3(S.HallExit) - off;
        m.HallCenter = hall.transform.position;
        var purple = new Color(0.85f, 0.32f, 1f);
        if (trails)
        {
            // портал в глубине площадки лицом к камере, враги выходят из воронки вперёд и сворачивают на дорогу
            m.PortalClear = 9f;
            foreach (var (spawn, name) in new[] { (S.PortalLeft, "Left"), (S.PortalRight, "Right") })
            {
                var pos = V3(spawn) + Vector3.forward * PortalBack;
                Place(P("Portal"), m.Buildings, pos, 180f).name = "Portal_" + name;
                Glow(m.Buildings, "Glow_Portal_" + name, pos + new Vector3(0f, 3.9f, -2f), purple, 9f, 15f);
                m.PortalSpots.Add(new Vector2(pos.x, pos.z));
            }
        }
        else
        {
            // порталы: лицевая сторона (+Z модели) вдоль пути
            Place(P("Portal"), m.Buildings, V3(S.PortalLeft), 90f).name = "Portal_Left";
            Place(P("Portal"), m.Buildings, V3(S.PortalRight), -90f).name = "Portal_Right";
            Glow(m.Buildings, "Glow_Portal_Left", V3(S.PortalLeft) + new Vector3(1.5f, 2.2f, 0f), purple, 9f, 14f);
            Glow(m.Buildings, "Glow_Portal_Right", V3(S.PortalRight) + new Vector3(-1.5f, 2.2f, 0f), purple, 9f, 14f);
            m.PortalSpots.Add(S.PortalLeft);
            m.PortalSpots.Add(S.PortalRight);
        }
        Glow(m.Buildings, "Glow_TownHall", V3(S.HallExit) + new Vector3(0f, 3f, 1f), new Color(1f, 0.6f, 0.3f), 6f, 10f);

        PathPoints(m);
        // на тропах в кадре и островки по бокам, и поле вокруг карты под фон-великанов (сзади шире)
        if (trails) FitCamera(m, DeckHalf + 12f, 12f, 30f);
        else FitCamera(m, RoadHalf + 1f, 0f, 0f);
        return m;
    }

    static void Finish(Map m, string path, VolumeProfile mapProfile, System.Action<Scene> after)
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
        vol.sharedProfile = mapProfile;
        after?.Invoke(m.Scene);
        EditorSceneManager.SaveScene(m.Scene);
        Debug.Log("[PathMaps] сцена сохранена: " + path);
    }

    /// Контрольные точки кривой врагов: Path/Route_Left, Path/Route_Right, P00 - у портала, последняя - у ратуши.
    static void PathPoints(Map m)
    {
        var icon = EditorGUIUtility.IconContent("sv_icon_dot13_pix16_gizmo").image as Texture2D;
        foreach (var (name, route) in new[] { ("Route_Left", S.RouteLeft), ("Route_Right", S.RouteRight) })
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
    /// pad - поле вокруг схемы по бокам и спереди, backPad - сзади (за ратушей).
    static void FitCamera(Map m, float roadMargin, float pad, float backPad)
    {
        var cam = m.Cam;
        var pts = new List<Vector3>();
        void Add(Vector3 p, float r, float h)
        {
            pts.Add(p + Vector3.left * r); pts.Add(p + Vector3.right * r);
            pts.Add(p + Vector3.back * r); pts.Add(p + Vector3.forward * r);
            pts.Add(p + Vector3.up * h);
        }
        foreach (var v in S.Legs.SelectMany(l => l)) Add(V3(v), roadMargin, 0f);
        foreach (var p in m.PortalSpots) Add(V3(p), 7f, 9f);
        Add(m.HallCenter, 10f, 14f);
        if (pad > 0f || backPad > 0f)
        {
            var bb = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) bb.Encapsulate(p);
            pts.Add(new Vector3(bb.min.x - pad, 0f, bb.min.z - pad));
            pts.Add(new Vector3(bb.max.x + pad, 0f, bb.min.z - pad));
            pts.Add(new Vector3(bb.min.x - pad, 0f, bb.max.z + backPad));
            pts.Add(new Vector3(bb.max.x + pad, 0f, bb.max.z + backPad));
        }

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
            for (; d < 800f; d += 0.5f)
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
        foreach (var leg in S.Legs)
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

    static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static float DistToRoads(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var (a, b) in Segments()) best = Mathf.Min(best, DistToSeg(p, a, b));
        return best;
    }

    /// Порталы и ратуша с выходами (двор 14.4 м, выходы на 9.2 м от центра) - без декора.
    static bool NearBuildings(Map m, Vector2 p, float r)
    {
        if (m.PortalSpots.Any(q => (p - q).magnitude < m.PortalClear + r)) return true;
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
        var dof = AddHidden<DepthOfField>(p);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(150f);
        dof.gaussianEnd.Override(320f);
        dof.gaussianMaxRadius.Override(1f);
        dof.highQualitySampling.Override(false);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    /// Поверх PP_Arcane для троп: цветокор и тон Arcane те же, но камера обзора в 2.5 раза дальше - размытие
    /// дальнего плана начинается во столько же раз дальше; зерно и хроматическая аберрация выключены (цветной шум).
    static VolumeProfile TrailsProfile()
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(TrailsProfilePath);
        if (p != null) return p;
        p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, TrailsProfilePath);
        var dof = AddHidden<DepthOfField>(p);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(150f * TrailsScale);
        dof.gaussianEnd.Override(320f * TrailsScale);
        dof.gaussianMaxRadius.Override(1f);
        dof.highQualitySampling.Override(false);
        AddHidden<FilmGrain>(p).intensity.Override(0f);
        AddHidden<ChromaticAberration>(p).intensity.Override(0f);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    static T AddHidden<T>(VolumeProfile p) where T : VolumeComponent
    {
        var c = p.Add<T>(false);
        c.name = typeof(T).Name;
        c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
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
    static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
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
