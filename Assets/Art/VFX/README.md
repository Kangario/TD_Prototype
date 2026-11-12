# VFX-кит: заготовки для Shuriken (Unity 6, URP, WebGL)

Здесь нет готовых анимированных эффектов, только простые детали. Анимацию делают модули частиц: Size / Color / Rotation over Lifetime и Texture Sheet Animation.

Все текстуры **белые или серые с альфой**, цвет задают частицы (Start Color, Color over Lifetime). Серые полутона внутри текстур нужны для двухтонового toon-вида после покраски.

Генератор: `Art/_source/build_vfx_kit.py`. Запуск: `blender -b --factory-startup --python build_vfx_kit.py`, флаг `--no-render` пропускает превью.

Превью лежат в `Preview/`:
- `kit_textures.png` — все текстуры;
- `kit_flipbooks.png` и `kit_flipbooks.mp4` — флипбуки;
- `kit_meshes.png` — меши;
- `kit_demo.png` — 8 примеров, как детали складываются в эффекты.

---

## Что внутри

### Textures/: спрайты 256 px, Clamp
| Текстура | Что это | Где |
|---|---|---|
| `T_FX_Glow` | круг-свечение, 3 ступени | ядро вспышек, сфера опыта, магический шар |
| `T_FX_GlowSmooth` | мягкое свечение без ступеней | подсветка под эффектом, тонкие ореолы |
| `T_FX_Dot` | точка | искорки, угольки, хвосты |
| `T_FX_Ring` | тонкое кольцо | маркер приказа, расходящиеся кольца |
| `T_FX_Shockwave` | кольцо с затуханием внутрь | ударные волны (Horizontal Billboard) |
| `T_FX_Spark` | ромб-штрих, вытянут вдоль U | искры удара (Stretched Billboard) |
| `T_FX_Star4` | 4-лучевая звёздочка | блёстки, магия, повышение уровня |
| `T_FX_Star5` | 5-лучевая звезда | звёздочка уровня навыка, оглушение |
| `T_FX_Plus` | плюс | лечение |
| `T_FX_Burst` | лучистая вспышка | кадр удара, крит |
| `T_FX_Puff` | мультяшное облачко с контуром | пыль, дым (одиночный спрайт) |
| `T_FX_Shard` | осколок с гранями | осколки, кристаллы |
| `T_FX_Chevron` | стрелка-уголок | приказ «идти» |
| `T_FX_Cross` | крестик | приказ «атаковать», отказ |
| `T_FX_ScreenEdge` | вспышка по краю экрана (для UI) | удар по ратуше |

### Textures/: декали на землю, 512 px, Clamp
| Текстура | Где |
|---|---|
| `T_FX_Decal_Selection` | кружок выделения |
| `T_FX_Decal_Range` | радиус взаимодействия |
| `T_FX_Decal_RangeDashed` | дальность атаки, зона залпа |
| `T_FX_Decal_MagicCircle` | повышение уровня, молитва, аура |
| `T_FX_Decal_Target` | предупреждение метеора, цель атаки |
| `T_FX_Decal_Cracks` | трещины. Уже тёмные: **не красить**, менять только альфу |
| `T_FX_Decal_Scorch` | подпалина. Тоже тёмная, не красить |

### Textures/: маски для мешей, 256 px
| Текстура | UV | Где |
|---|---|---|
| `T_FX_Mask_Beam` | U по кругу (Repeat), V вверх: низ яркий, вверх ступенчато гаснет | Cylinder, Funnel, Beam3, Dome |
| `T_FX_Mask_Wave` | U по кругу (Repeat), V от центра наружу: яркая кромка снаружи | Ring (ударная волна, световое кольцо) |
| `T_FX_Mask_Trail` | **U=0 голова** (ярко, широко), U=1 хвост | Spiral, SlashArc, Trail Renderer, модуль Trails |
| `T_FX_Mask_Hex` | соты, Repeat по U и V | Dome / Sphere (щит) |

### Textures/: шумы 256 px, Repeat, линейные (sRGB off)
`T_FX_Noise_Smooth`, `T_FX_Noise_Cells`, `T_FX_Noise_Streaks`. В обычных частицах они не нужны. Они понадобятся для Shader Graph (раздел в конце): растворение, искажение, бегущие полосы.

### Flipbooks/: 512 px, сетка 4×4, кадр 128 px, кадр 0 — левый верхний
| Флипбук | Цвет | Длина |
|---|---|---|
| `T_FX_FB_Smoke_4x4` | серый, красить (дым, пыль, облачко) | разовый |
| `T_FX_FB_Explosion_4x4` | **цветной**, не красить | разовый |
| `T_FX_FB_Fire_4x4` | **цветной**, не красить | **цикл** без шва |
| `T_FX_FB_Impact_4x4` | белый, красить | разовый |
| `T_FX_FB_MagicBurst_4x4` | белый, красить | разовый |
| `T_FX_FB_Slash_4x4` | белый, красить | разовый |

В разовых флипбуках последний кадр пустой, частица исчезает сама.

### Meshes/: FBX для Renderer → Mesh
Меши вписаны в куб 1 м, поэтому **Start Size = размер в метрах**. Vertex color и материалов в мешах нет, цвет полностью идёт от частиц.

| Меш | Треуг. | Опора | UV |
|---|---|---|---|
| `SM_FX_Quad` | 2 | центр, лежит на земле лицом вверх | 0..1 |
| `SM_FX_Ring` | 96 | центр, на земле, r 0.1–0.5 | U по кругу, V наружу |
| `SM_FX_Cylinder` | 128 | низ, открытый | U по кругу, V вверх |
| `SM_FX_Funnel` | 256 | низ, воронка расширяется вверх | U по кругу, V вверх |
| `SM_FX_Dome` | 264 | низ, полусфера | U по кругу, V от края к макушке |
| `SM_FX_Sphere` | 224 | центр | U по кругу, V снизу вверх |
| `SM_FX_Beam3` | 6 | низ, 3 скрещённых квада (столб света) | U поперёк, V вверх |
| `SM_FX_Spiral` | 128 | низ, лента в 2 витка | U вдоль ленты (0 — верхний конец), V поперёк |
| `SM_FX_SlashArc` | 64 | центр, плоский полумесяц перед собой (+Z) | U=0 справа (голова), U=1 слева |
| `SM_FX_Shard` | 12 | центр | — |
| `SM_FX_Debris_01..03` | 80 | центр, UV в атлас `T_Atlas` (камень) | материал мира |
| `SM_FX_Coin` | 88 | центр, ребром вперёд, UV в атлас (золото) | материал мира |

Готовые меши из других папок, которые пригодятся: `Weapons/FBX/SM_Arrow.fbx` (залп, след стрелы), `Weapons/FBX/SM_MagicBall.fbx`, `Environment/FBX/SM_Tombstone_01/02.fbx` (гибель героя).

---

## Подключение в Unity

1. Скопируй `Art/VFX` в `Assets/`. Скрипт `Editor/VfxKitImporter.cs` сам выставит импорт:
   - **текстуры `T_FX_*`:** Alpha Is Transparency; шумы линейные и Repeat; маски Repeat по U;
   - **меши `SM_FX_*`:** без материалов и анимаций, Read/Write включён (так требует Shuriken в режиме Mesh).

   Если файлы попали в проект раньше скрипта, сделай им Reimport.
2. Материалы на шейдере `Universal Render Pipeline/Particles/Unlit`, по одному на текстуру:
   - **Alpha:** Surface Type = Transparent, Blending Mode = Alpha, Color Mode = Multiply. Подходит для дыма, пыли, декалей, трещин, флипбуков огня и взрыва.
   - **Additive:** Blending Mode = Additive. Подходит для свечения, искр, звёздочек, магии. На светлой траве аддитив выцветает, поэтому основу лучше делать Alpha, а аддитив — только ядро.
   - **Для мешей:** Render Face = **Both**.
   - **Обломки и монетка:** материал мира (атлас `T_Atlas`).
3. Renderer частиц:
   - спрайты — **Billboard**;
   - искры — **Stretched Billboard** (Speed Scale 0.03–0.06);
   - всё, что лежит на земле — **Horizontal Billboard**;
   - меши — **Mesh** с Render Alignment = **World** (иначе кольца на земле разворачиваются к камере).
4. Флипбуки: модуль **Texture Sheet Animation** → Mode Grid, Tiles 4×4, Whole Sheet.
   - Разовые: Time Mode = Lifetime.
   - Огонь: Time Mode = FPS 12–16 и Start Frame = Random 0..15, чтобы языки не шли в ногу.
   - В материале можно включить Flip-Book Frame Blending. Для toon лучше выключить: ступенчатая смена кадров выглядит рисованнее.

### Если искра встала поперёк
`T_FX_Spark` нарисован вдоль оси U (горизонтально). Если в Stretched Billboard она растягивается поперёк, есть два варианта:
- включи в Renderer **Rotate With Stretch** и поставь Start Rotation 90°;
- либо поверни PNG на 90°.

---

## Как получить toon-вид, а не «мыльный»

- **Ступенчатая альфа.** В Color over Lifetime поставь у градиента **Mode = Fixed**. Значения меняются ступенями, как в освещении моделей. Mode = Blend даёт плавный переход для мягких эффектов.
- **Огибающая «проявление → пик → затухание»** (альфа в Color over Lifetime):
  - **резкая** (удар, искры, вспышка): Fixed, ступени 0–45% = 1.0, 45–75% = 0.6, 75–100% = 0.25. В режиме Fixed значение ключа действует слева от него, поэтому ключи ставятся на правые границы: 45% → 1.0, 75% → 0.6, 100% → 0.25;
  - **средняя** (магия, лечение, навыки): Blend, ключи 0% → 0, 12% → 1, 60% → 1, 100% → 0;
  - **плавная** (дым, пыль, ауры): Blend, ключи 0% → 0, 30% → 0.8, 100% → 0.
- **Size over Lifetime** с быстрым «выстрелом»: за 10–15% жизни 0 → 1.1, затем 1.0. Для ударных волн — ease-out 0.2 → 1.
- **Мало частиц, но крупных.** Стилизованный эффект — это 3–8 крупных элементов, а не облако из 200 точек. Для WebGL держи Max Particles ≲ 40–60 на эффект и не перекрывай экран прозрачными слоями.
- Soft Particles и Distortion для toon не нужны.

---

## Рецепты: раздел 7 тех дока

Длительности даны с расчётом «короче 1.5 с, кроме зацикленных». Цвета примерные.

**1. Кружок выделения.** Не частица, а `SM_FX_Quad` + `T_FX_Decal_Selection` (зелёный) на MeshRenderer: так проще включать, выключать и масштабировать. Масштаб ≈ 1.2–1.5 м. Пульс или медленный поворот (20°/с) — маленьким скриптом.

**2. Маркер приказа**, 0.5 с:
- Horizontal Billboard `T_FX_Ring`: Size over Lifetime 1.4 → 0.6, альфа резкая;
- поверх 1 Billboard: «идти» — `T_FX_Chevron` (зелёный) с Velocity Y 0.6 → 0 (подпрыгивает); «атаковать» — `T_FX_Cross` или `T_FX_Decal_Target` (красный).

**3. Радиусы у выделенного героя.** Два `SM_FX_Quad`:
- `T_FX_Decal_Range` (голубой) — радиус взаимодействия;
- `T_FX_Decal_RangeDashed` (красный) — дальность атаки.

Масштаб = 2 × радиус из конфига.

**4. Подсветка при перетаскивании навыка.** Обводку героя меняй через цвет оболочки обводки в материале героя (MaterialPropertyBlock): зелёный, жёлтый или красный. Дополнительно под ногами `T_FX_Ring` того же цвета с пульсом Size 1.0 ↔ 1.1 (Looping, 1 частица).

**5. Выдача навыка**, 0.7 с:
- Mesh `SM_FX_Cylinder` + `Mask_Beam` (цвет навыка), Start Size (1.2, 1.6, 1.2), Size over Lifetime Y 0 → 1 за 15%;
- 10–14 `T_FX_Star4`/`T_FX_Dot` вверх: Shape Circle r 0.4, Speed 1.5–3;
- Horizontal `T_FX_Shockwave` 0.3 → 1.6 м.

При повышении уровня навыка добавь 1 `T_FX_Star5` над головой: 0.6 м, Size 0 → 1.2 → 1, 1 с.

**6. Смена оружия**, 0.4 с: 3–4 частицы `T_FX_FB_Smoke_4x4` в руке (0.35–0.5 м, светло-серый) + 3 мелкие `T_FX_Star4`.

**7. Искры удара**, 0.3 с:
- 1 частица `T_FX_FB_Impact_4x4` 0.7 м (тёпло-жёлтый);
- 6–10 `T_FX_Spark` в Stretched Billboard: Shape Hemisphere, Speed 4–7, жизнь 0.2–0.3.

**Крит:**
- размеры ×1.5;
- плюс `T_FX_FB_Slash_4x4` 1.6 м поперёк удара;
- плюс 1 `T_FX_Burst` (Size 0.4 → 1.2 → 0 за 0.15 с).

**8. Магический шар и вспышка попадания:**
- **шар:** на `SM_MagicBall` дочерние частицы — 1 `T_FX_GlowSmooth` (additive, 0.8 м, Looping) плюс модуль Trails с `Mask_Trail` или Emission Rate over Distance `T_FX_Dot`;
- **попадание**, 0.4 с:
  - `T_FX_FB_MagicBurst_4x4` 1.4 м;
  - Mesh `SM_FX_Sphere` + `Noise_Cells`: Size 0.2 → 1.2, альфа резкая;
  - Horizontal `T_FX_Shockwave`.

**9. След стрелы.** Trail Renderer на `SM_Arrow`: материал `Mask_Trail` additive, Time 0.12–0.18, Width 0.08 → 0, Texture Mode = Stretch.

**10. Сферы опыта.** Каждая сфера — маленький объект из пула: `T_FX_Glow` (0.35 м) + `T_FX_Dot` в ядре + Trail `Mask_Trail`. Полёт по дуге к герою задаёт скрипт (Bezier, 0.5–0.8 с), так как точку назначения знает логика. В конце — мини-вспышка `T_FX_Star4`.

**11. Повышение уровня**, 1.2 с:
- Horizontal `T_FX_Decal_MagicCircle` (золото): Size 0 → 2.6 м за 0.2 с, Rotation 60°/с;
- Mesh `SM_FX_Beam3` + `Mask_Beam`: Start Size (1.3, 3.2, 1.3), Size over Lifetime Y 0 → 1;
- Mesh `SM_FX_Spiral` + `Mask_Trail`: Rotation over Lifetime Y 360°/с, Velocity Y 1;
- 8–10 `T_FX_Star4`.

Надпись «LEVEL UP» делай через UI Toolkit или world-space TextMeshPro.

**12. Найм героя у ратуши**, 0.8 с:
- Mesh `SM_FX_Funnel` + `Mask_Beam` (золото): Size Y 0 → 1.8;
- 6 `T_FX_FB_Smoke_4x4` по кругу (Shape Circle, edge), светлые;
- Horizontal `T_FX_Shockwave`;
- `T_FX_Star4`.

**13. Гибель героя:**
- вспышка `T_FX_FB_Impact_4x4` (белый, 1.5 м);
- 6 Mesh `SM_FX_Shard`: Speed 3, Gravity 1, Rotation over Lifetime;
- 3 `T_FX_FB_Smoke_4x4`.

После этого префаб `SM_Tombstone_01` на 5 с: появление Scale 0 → 1.1 → 1 за 0.25 с, исчезновение — опустить в землю.

**14. Щит**, Looping, 1 частица:
- Mesh `SM_FX_Dome` + `Mask_Hex` (голубой, альфа 0.6), Size ≈ 2 м;
- Size over Lifetime 1 ↔ 1.03 для лёгкого «дыхания»;
- в момент наложения — разовый Horizontal `T_FX_Shockwave`.

Тоже можно сделать MeshRenderer'ом.

**15. Залп**, ~1 с:
- зона: `T_FX_Decal_RangeDashed` (красный, 0.3 с);
- **стрелы:** Renderer Mesh `SM_Arrow`:
  - Shape Circle радиусом с зону на высоте 8 м;
  - Velocity over Lifetime Y −18;
  - 3D Start Rotation остриём вниз;
  - Rate 25/с, 0.5 с;
- **удары о землю:** Collision (Plane), Sub Emitter на Collision — 1 мелкий `T_FX_FB_Smoke_4x4` (пыль) и 2 `T_FX_Spark`.

**16. Метеор:**
1. **0–0.4 с — предупреждение:** Horizontal `T_FX_Decal_Target` (красный), Size 0.6 → 1.0 зоны, альфа мигает (Fixed: 1 / 0.4 / 1 / 0.4).
2. **0.4–0.7 с — падение:** Mesh `SM_FX_Debris_01` (1 м) с дочерними частицами `T_FX_FB_Fire_4x4` и Trails.
3. **Взрыв:**
   - 2–3 `T_FX_FB_Explosion_4x4` по 2.5–3.5 м;
   - Mesh `SM_FX_Ring` + `Mask_Wave` (оранжевый): Size 1 → 5 м за 0.3 с;
   - 8 `SM_FX_Debris_*`: Speed 4–6 вверх-наружу, Gravity 2;
   - `T_FX_Decal_Scorch` и `T_FX_Decal_Cracks` гаснут за 2–3 с.

**17. Горение**, Looping, на враге:
- `T_FX_FB_Fire_4x4`: Rate 6/с, жизнь 0.6 с, Size 0.5–0.8, Shape Sphere r 0.3, Velocity Y 0.4;
- 2/с `T_FX_Dot` (оранжевые угольки, additive) вверх;
- 1/с тёмный `T_FX_FB_Smoke_4x4`.

**18. Лечение**, 0.8 с:
- Mesh `SM_FX_Cylinder` + `Mask_Beam` (зелёный), Start Size (1.4, 1.7, 1.4);
- 6–10 `T_FX_Plus` вверх: Speed 0.8–1.5, Size 0.2–0.35, альфа средняя;
- Horizontal `T_FX_GlowSmooth` на земле.

**19. Молитва (световое кольцо)**, 0.8 с:
- Mesh `SM_FX_Ring` + `Mask_Wave` (тёплый белый): Size 1 → радиус навыка ×2, ease-out;
- Horizontal `T_FX_Decal_MagicCircle` с альфой 0.4;
- 10 `T_FX_Star4` по кромке.

**20. Аура исцеления и благословение**, Looping:
- **аура:** Horizontal `T_FX_Decal_MagicCircle` (зелёный, альфа 0.35, Rotation 15°/с) + 2/с `T_FX_Plus` невысоко;
- **благословение (haste):** Mesh `SM_FX_Spiral` + `Mask_Trail` (золото, Rotation Y 240°/с) + у ног `T_FX_Spark` вверх (линии скорости).

**21. Смерть врага**, 0.8 с:
- 3–4 `T_FX_FB_Smoke_4x4` (0.8–1.2 м, серо-фиолетовый);
- 1 Mesh `SM_FX_Coin` (0.25 м, материал мира): Speed 3 вверх, Gravity 1.5, Rotation over Lifetime Y 720°/с;
- блеск `T_FX_Star4` на монетке.

**22. Удар по ратуше:**
- в точке удара: `T_FX_FB_Impact_4x4` (оранжево-красный, 2 м) + 4 `SM_FX_Debris_*` + 2 `T_FX_FB_Smoke_4x4`;
- красная вспышка по краю экрана: UI Toolkit, полноэкранный элемент с `T_FX_ScreenEdge` (tint красный), альфа 0 → 0.8 → 0 за 0.35 с.

**23. Босс:**
- **появление:**
  - из портала `T_FX_FB_Smoke_4x4` (тёмно-фиолетовый): Shape Cone вверх, Rate 30/с 1.5 с, Size 1.5–3 м;
  - Mesh `SM_FX_Funnel` + `Mask_Beam` (фиолетовый);
  - тряска камеры — Cinemachine Impulse или скрипт;
- **удар по земле**, 0.6 с:
  - `T_FX_Decal_Cracks` гаснет за 2 с;
  - Mesh `SM_FX_Ring` + `Mask_Wave` (пыльный беж): 1 → 6 м за 0.35 с;
  - 8–12 `SM_FX_Debris_*`: Speed 4–6, Gravity 2;
  - кольцо `T_FX_FB_Smoke_4x4` (Shape Circle edge, Speed наружу 2);
  - Horizontal `T_FX_Shockwave`.

---

## Дальше, если захочется красивее: Shader Graph

Стандартный Particles/Unlit не двигает UV и не растворяет. Один свой шейдер для частиц на Shader Graph (URP Unlit, Transparent) закрывает почти всё:
- **toon-растворение:** `alpha = step(Noise_Cells, порог)`, порог берётся из **Custom Data** (Custom1.x по времени жизни). В Renderer → Custom Vertex Streams добавь Custom1.xyzw.
- **бегущие маски:** `UV + Time × скорость` для `Mask_Beam` и `Noise_Streaks` на Cylinder и Beam3 — столбы «текут» вверх.
- **gradient map:** серую текстуру (`Puff`, `FB_Smoke`) отображать в 3 цвета через Sample Gradient. Так дым, пыль и огонь получаются из одной текстуры.
- **френель для щита:** яркий край `Dome`/`Sphere` плюс `Mask_Hex`.

Для WebGL всё это дёшево: один проход, без compute.
