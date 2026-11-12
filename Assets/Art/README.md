# Арт P1 в Unity

Исходники лежат в `P1_TD_s_geroyami/Art`: Blender-скрипты и FBX. В проект их копирует `Art/_source/sync_unity.ps1`.

## Что где

| Папка | Что |
|---|---|
| `Textures/T_Atlas.png` | общий рисованный атлас 1024 для всех моделей |
| `Characters`, `Weapons`, `Environment` | FBX как есть из Blender, обводка уже в меше (второй материал) |
| `Materials/` | `M_Toon_Atlas` (шейдер `P1/Toon`) и `M_Outline` (шейдер `P1/ToonOutline`) |
| `Prefabs/` | префабы-варианты всех моделей, на них удобно вешать свои компоненты |
| `Shaders/` | toon-шейдер и обводка под URP, работают на WebGL |
| `VFX/` | кит для частиц: текстуры, флипбуки, меши, `Materials/` под URP Particles, `Examples/` с тремя готовыми системами частиц. Подробности в `VFX/README.md` |
| `Editor/` | `ArtImport` (настройки импорта), `ArtSetup` (меню **Tools → P1 Art → Setup All**) |

Сцена-витрина: `Assets/Scenes/ArtShowcase.unity`. В ней карта, дорога, ратуша, анимированный портал, герои, декали выделения и радиусов, примеры частиц.

## Как устроен вид
- **Toon-шейдер** — порт материала из Blender:
  - 3 ступени света по солнцу (цвета ступеней задаются в материале);
  - блик там, где vertex color R;
  - тёплый ободок на освещённой стороне;
  - свечение там, где vertex color G/B.
- **Трава** (`SM_Ground_Tile`) — материал `M_Arcane_Grass`: без текстуры, 4 цвета в инспекторе (Color 1–4), 1 треугольник = 1 цвет. Центры треугольников при импорте пишет `ArtImport` в UV2, шейдер по ним выбирает цвет. Другие модели добавляются в `ArtImport.TriangleColorModels`.
- **Обводка** — inverted hull: оболочка экспортирована из Blender вторым сабмешем. Тени она не отбрасывает.
- **Солнце** — Directional Light, intensity 1, тени Hard. Светлее или темнее делается цветом ступеней в `M_Toon_Atlas`, а не интенсивностью света.
- **Ратуша:** дочерние пустышки `Spawn_01..03`, `Exit_Front/Left/Right` — точки спавна и выхода юнитов.
- **Портал:** зацикленный клип `Portal_Loop` (`Environment/Portal.controller`).

## Обновить арт
1. Пересобери ассеты в Blender (скрипты в `Art/_source`).
2. Запусти `powershell -ExecutionPolicy Bypass -File Art/_source/sync_unity.ps1`.
3. Открой Unity. Если появились новые модели или текстуры частиц, запусти **Tools → P1 Art → Setup All**. Повторный запуск безопасен: пересобирает материалы, префабы и витрину на тех же путях. Ручные правки в `ArtShowcase.unity` при этом перезапишутся.
