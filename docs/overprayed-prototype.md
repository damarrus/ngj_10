# OverPrayed — прототип

Сцена-прототип идеи **OverPrayed**. Точка входа для новой сессии: что уже есть, как играется, где код.

> Тестирование: **оператор тестит в play сам**. После правок — только проверка компиляции (`assets-refresh`, console без ошибок). Не входить в play / не симулировать ввод / не делать camera-render скрины, если не попросили. См. memory `skip-play-testing`.

---

## Сцена

`Assets/_Project/Scenes/OverPrayed.unity` — рабочая сцена прототипа.
Сделана копированием чистого шаблона `Template.unity` (Main Camera + Canvas + EventSystem, без геймплея).

**Камера неподвижна**, ortho size 5, центр (0,0). Видимая область ≈ **17.78 × 10** юнитов (16:9). Поле под размер камеры, игрок не уходит за экран.

### Корневые объекты сцены
- **Main Camera** — статична, LetterboxCamera + URP data.
- **Field** — газон-фон (tiled SpriteRenderer 18×10, sortingOrder -100).
- **Plinth** — каменный диск центра, **отключён** (`SetActive(false)`): центр больше не игровой.
- **Player** — инстанс `Player.prefab`, старт (0, -2).
- **Altars** — 5 инстансов `Altar.prefab` **ровным рядом вверху** (y=3.8, шаг X=2.0, x −4..+4). **Самодостаточные**: каждый ведёт своё 1 задание + квадрат-зона сдачи под ним + бордер-прогресс + шкала-гнев (radial-ринг вокруг иконки кары) + огонь (интенсивность по гневу). Child `Fire` (AltarFire).
- **Wall** — горизонтальный невидимый барьер (один BoxCollider2D, y≈2.25, во всю ширину) между заданиями и зонами сдачи. Верх (алтари+задания) непроходим, **зоны сдачи доступны снизу**.
- **LevelManager** — wiring алтарей: считает сдачи до цели (12), запускает кару алтаря пока он гневается (rage), мини-кару при таймауте (`Burst`), следит что Run-задание макс у одного алтаря. (Заменил `AltarManager`. `CenterAltar` удалён.)

Раскладка на колонку (offset от алтаря): иконка-кары+гнев-ринг (y+0.95) → алтарь (3.8) → задание-пузырь (−1.0) → зона сдачи (−2.1, квадрат `_zoneSize` 1.5×1.0). Поле спавна y<1.95 (`FieldSpawner._maxSpawnY`), игрок старт (0,−2).
- **SheepSpawner** / **TreeSpawner** / **BerrySpawner** — `FieldSpawner` (kind Sheep / Tree / Berry).
- **Effects** — родитель для рантайм-спавна (метеоры, лужи, бревна, овцы, деревья, кары).
- **Canvas**, **EventSystem** — инфра (UI-текст вёрстки пока нет).

---

## Геймплей (как играется)

Топ-даун. WASD — движение (физика, Rigidbody2D). **E — единая клавиша взаимодействия**: приоритет — таргет-интерактабл (поднять предмет / сдать алтарю), иначе (если в руках предмет и таргета нет) — **бросить** предмет на землю перед игроком.

### Подсветка (feedback на E)
- **Подсказка "E"** (world TMP над игроком) видна когда есть любое E-действие (таргет ИЛИ есть что бросить). Цвет: `_promptHighlightColor` (жёлтый) для осмысленных таргетов (поднять/сдать), `_promptColor` (белый) для голого «бросить».
- **Сам предмет** подсвечивается (тинт спрайта `Carryable._highlightColor`) пока он — текущий таргет подбора (игрок рядом, руки пусты). Один таргет за раз; при смене/уходе тинт снимается.

**Цель/петля:** игра делится на **уровни** (цель = сдать N заданий суммарно по всем алтарям). **Каждый алтарь сам ведёт своё задание:**
- Тип задания: Sheep/Log/Berry (сдать в свою квадрат-зону под алтарём, нужно N сдач) ИЛИ **Run** (пробежка: спавнятся `_runZoneCount` зон на поле, пробегаешь через каждую → +шаг). Run не дублируется — макс 1 алтарь (`LevelManager.CanAssignRun`).
- **Бордер пузыря = прогресс** (0→1, radial fill зелёный). **Цвет пузыря = таймер** (жёлтый→красный к дедлайну).
- **Шкала гнева — float 0..`_angerMax`(100)**, красный radial-ринг вокруг иконки кары над алтарём. Изменения: +`_angerPerSec`(1)/сек пассивно, таймаут +`_angerOnFail`(10), сдал задание −`_angerOnDeliver`(10).
- Таймаут (`_taskTime`): пузырь краснеет, висит `_failHold`, исчезает, +гнев, **+мини-кара** (одноразовый Burst этого алтаря), пауза `_refillPause`, новое задание.
- Гнев полон → **кара включается** на `_rageDuration`(18с), ринг утекает (гнев пассивно не растёт во время rage), потом гнев→0, тухнет.
- **Огонь** (`AltarFire`) — интенсивность (размер+alpha+скорость flicker) ∝ гнев. Тлеет при низком, разгорается к 100%, пыхает в rage.

Текущий контент: **1 уровень** — 12 сдач. Кары по гневу (не постоянные): у каждого алтаря своя (Wind/Meteors/Darkness/Lightning/Puddles).

**Мини-кары (`PunishmentFactory.Burst`):** одноразовый «вкус» при таймауте. Wind=импульс игроку (`PlayerController.Push`)+краткий stream; Meteors=1 метеор; Lightning=1 молния; Puddles=1 лужа (5с); Darkness=нет one-shot → лобаем метеор. Самоочистка через `TimedDestroy`.

**Новые скрипты:** `TaskType` (Sheep/Log/Berry/Run + хелперы), `RunZone` (зона пробежки, событие `Entered`, проц. ring, pop-out), `TimedDestroy` (отложенный Destroy для burst). `Altar` — вся per-altar логика (задание/зона/таймер/гнев-float/бордер/огонь/run/IInteractable + события Fulfilled/RageStarted/RageEnded/MiniPunish). `CenterAltar` удалён.

### Ресурсы
- **Овца** — спавнится на поле (scale-in pop, не в центре). **Убегает** от игрока: в радиусе 2 бежит прочь, чем ближе игрок — тем быстрее (3→8), **не догнать**. Возле края экрана уклоняется вдоль границы (не застревает в углу). Брать в руки НЕЛЬЗЯ. Доставка = **загнать телом** в зону запрашивающего овцу алтаря (радиус 0.9) → засчёт, овца исчезает.
- **Бревно** — добывается из **дерева**: подойти к зоне слева дерева и постоять 1 сек, и к зоне справа 1 сек (порядок неважен, кружок-прогресс над каждой зоной). Обе готовы → дерево роняет бревно, исчезает. Бревно **берётся в руки** (E, висит над головой) и сдаётся алтарю (E рядом, если алтарь просит бревно).
- **Ягодки** — спавнятся на поле (как деревья, через `FieldSpawner` kind Berry), `Carryable(Berry)`. **Берутся в руки** (E) и сдаются алтарю (E рядом, как бревно). Доставка обобщена в `Altar.CanInteract` — любой carryable-тип (бревно/ягоды), кроме овцы.

### Алтари (5, ряд вверху)
Каждый **самодостаточен**, ведёт 1 задание за раз (`Altar.cs`, см. петлю выше). Кара привязана к алтарю 1:1 (`Altar._punishment`), включается **по гневу** (не по расписанию): Altar_0=Ветер, 1=Метеориты, 2=Темнота, 3=Молнии, 4=Лужи. Иконка кары над алтарём (windstreak/meteor/darkness/lightning/puddle спрайты).

**Кары (полная версия, при rage — `_rageDuration`):**
| Кара | Описание |
|---|---|
| **Ветер** | постоянный снос игрока в случайном направлении, летящие линии-потоки |
| **Метеориты** | падают под лёгким случайным углом (`_maxAngleOffset`), ease-in ускорение, красная зона-телеграф растёт+пульсирует, при ударе — сочный взрыв (бело-горячее ядро→огонь) + игрок мигает красным |
| **Темнота** | чёрный оверлей с дырой вокруг игрока, fade-in, следует за ним |
| **Молнии** | снаряды с края экрана, целят в упреждённую позицию игрока + разброс, мигание при попадании |
| **Лужи** | 4 статичных лужи, замедляют игрока до 0.45× пока внутри |

Мини-версии (при таймауте задания) — см. `PunishmentFactory.Burst` выше.

Кары **стакаются** (несколько сразу).

---

## Структура кода

`Assets/_Project/Scripts/Gameplay/` (namespace `Ngj10.Gameplay`):

### Игрок / взаимодействие
- **PlayerController.cs** — WASD через `Rigidbody2D.linearVelocity`, clamp в экране. Несёт ресурс (`CarriedItem : Carryable`): `PickUp`/`OfferCarriedItem`/`DropCarriedItem` (drop = unparent в мир, re-enable collider+rb, sortingOrder→0, позиция `_rb + _dropOffset`). Единый E-хендлер: interactable.Interact иначе DropCarriedItem. `UpdatePrompt` (show + highlight-цвет TMP) + `UpdateItemHighlight` (тинт таргет-Carryable, 1 за раз). Hooks для кар: `SetExternalVelocity`/`ClearExternalVelocity` (ветер), `Blink` (попадания), `ApplySlow` (лужи, min-wins, reset в FixedUpdate), `Position`, `MoveSpeed`. Подсказка "E" над головой (поднимается выше когда несёт). `_interactPromptText` авто-граб в Awake если не проставлен.
- **IInteractable.cs** — `Position`, `CanInteract(player)`, `Interact(player)`. Ближайший доступный ищется в `FindBestInteractable` (OverlapCircle).
- **Carryable.cs** — ресурс который носят (`ResourceType`: Log/Berry). IInteractable pickup (CanInteract = руки пусты). `SetHighlighted(bool)` — тинт спрайта `_highlightColor` пока таргет подбора (идемпотентно, хранит base-цвет).
- **ResourceType.cs** — enum `{Sheep, Log}`.

### Ресурсы на поле
- **Sheep.cs** — flee AI: убегает в радиусе `_detectRadius` (2), скорость зависит от близости игрока (Lerp `_minSpeed`→`_maxSpeed`, 0 на краю радиуса → max вплотную). Возле края экрана направление бегства подмешивает отталкивание от стен (`EdgeAvoidance`, ramp с `_edgeAvoidDistance` 1.5) → овца скользит вдоль границы, не упирается в угол. Бродит вокруг **home-точки** в радиусе `_wanderRadius` (1); home = позиция овцы пока убегает/толкают (где остановилась — там новый home). Dynamic rb + solid collider. НЕ Carryable/IInteractable — засчёт делает алтарь.
- **Tree.cs** — 2 зоны (left/right), стой 1с в каждой (radial-fill), обе готовы → spawn Log, Destroy дерева.
- **FieldSpawner.cs** — спавнит Sheep или Tree (enum `_kind`) до лимита, scale-in, избегает центра (`_centerKeepout` радиус 3, ретрай).
- **ScalePopIn.cs** — scale 0→1 ease-out-back при появлении, потом self-destruct.

### Алтари / запросы / кары
- **Altar.cs** — состояние requesting + `RequestedResource` (рандом Sheep/Log/Berry) + `_startDelay`. Carryable (Log/Berry) = E-interact (IInteractable, тип сверяется с запросом). Sheep = пассивный засчёт в Update (скан овец в радиусе). События `Fulfilled`/`Expired`. Иконка по типу, radial-таймер.
- **AltarManager.cs** — владеет ритмом запросов всех 5 (Idle↔Requesting таймеры, паузы вразнобой) + жизнью кар (Begin/Tick/End 30с, стакаются). `PunishmentContext` (player/camera/prefabs) передаётся карам.
- **PunishmentType.cs** — enum `{Wind, Meteors, Darkness, Lightning, Puddles}`.
- **IPunishment.cs** — `Begin/Tick/End` + `PunishmentContext` + `PunishmentPrefabs` (Meteor/Lightning/Puddle/DarknessOverlay/WindStream).
- **PunishmentFactory.cs** — enum → new IPunishment.
- **Punishments/** — `WindPunishment`, `MeteorPunishment`, `DarknessPunishment`, `LightningPunishment`, `PuddlePunishment` (plain C#, спавнят свои объекты).

### Объекты кар (поведение на префабах)
- **Meteor.cs** — падение на цель, красная зона-телеграф растёт+пульсирует, при ударе blink игрока + спавн `ImpactFlash`.
- **ImpactFlash.cs** — оранжевый круг expand+fade, self-destruct.
- **Lightning.cs** — снаряд летит прямо, blink при попадании, lifetime.
- **WindStream.cs** — N полупрозрачных линий-штрихов дрейфуют по экрану в направлении ветра, зациклены.
- **Puddle.cs** — trigger-зона, `ApplySlow` игроку в OnTriggerStay2D.

---

## Префабы

`Assets/_Project/Prefabs/`:
- **Player.prefab** — SpriteRenderer + Rigidbody2D(dynamic, gravity 0) + CapsuleCollider2D + Animator + PlayerController + InteractPrompt (world TMP "E").
- **Altar.prefab** — спрайт + solid BoxCollider2D (непроходим) + trigger CircleCollider2D (interact) + Altar + RequestVisual (world Canvas: иконка ресурса + radial-таймер + фон-кольцо).
- **Sheep.prefab** — спрайт + dynamic Rigidbody2D + solid CircleCollider2D + Sheep + ScalePopIn.
- **Tree.prefab** — спрайт + Tree + LeftZone/RightZone (world Canvas radial-fill каждая) + ScalePopIn.
- **Log.prefab** — спрайт + Carryable(Log) + trigger collider + kinematic rb + ScalePopIn.
- **Berry.prefab** — спрайт (`berry.png`, сген. кодом) + Carryable(Berry) + trigger CircleCollider2D + kinematic rb + ScalePopIn. Зеркало Log.prefab.
- **Meteor / Lightning / Puddle / DarknessOverlay / WindStream / ImpactFlash** — объекты кар.

Спрайты: `Prefabs/Art/Sprites/` — **все сгенерены кодом** (CC0, рисуются в Texture2D скриптом), кроме `grass_field.png` (CC0 OpenGameArt, автор athile). Player PPU 128, props PPU 128, darkness PPU 23 (большое покрытие).

---

## Анимации

`Assets/_Project/Animations/`:
- `Player_Idle` (2fps), `Player_Run` (10fps), `Player.controller` (параметр Speed, Idle↔Run по порогу 0.01).

---

## Тех-стек / конвенции (из CLAUDE.md)

- Unity 6000.4.9f1, 2D URP. Цель — **WebGL**.
- Input System (new) — `Keyboard.current` напрямую, без .inputactions.
- namespace `Ngj10`, asmdef `Ngj10` (references: Unity.ugui, Unity.TextMeshPro, **Unity.InputSystem**).
- Связность через C# events (Altar→Manager), без event-bus/DI.
- Prefab-first: переиспользуемое — в префаб, на сцене инстанс.
- UI-текст — только TextMeshPro.
- `.unity`/`.prefab`/`.asset` правим **только через MCP** (script-execute / prefab-open), не руками.

### Грабли MCP (важно)
- **`scene-open` через MCP сломан** ("Requested scene is not valid or not found") — обходить через `script-execute` + `EditorSceneManager.OpenScene`.
- `console-get-logs` дампит огромный файл — фильтровать клиентски (PowerShell ConvertFrom-Json), не читать целиком.
- Wiring через `SerializedObject` в script-execute, `ApplyModifiedPropertiesWithoutUndo`. Префабы — `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`/`UnloadPrefabContents`.
- После удаления .cs — `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` чистит missing на префабах/сцене.

---

## Тюнинг-точки (инспектор / const)

- **LevelManager**: `_levels[0].RequestsToClear` (12), список алтарей + prefab-refs кар.
- **Altar** (задание): `_resourceSteps`(3), `_runZoneCount`(4), `_taskTime`(13), `_failHold`(1), `_refillPause`(1).
- **Altar** (гнев): `_angerMax`(100), `_angerPerSec`(1), `_angerOnFail`(10), `_angerOnDeliver`(10), `_rageDuration`(18).
- **Altar** (раскладка): `_angerOffset`/`_taskOffset`/`_zoneOffset`, `_zoneSize`(1.5×1.0), `_bubbleSize`, `_angerIconSize`, цвета.
- **Altar** (run): `_runArea`(0,−1.5), `_runSpread`(3.2).
- **AltarFire**: `_baseScale`(0.85), `_minScale`(0.3), `_flickerSpeed`, child `Fire` localPos (0,0.2).
- **RunZone**: `_radius`(0.7), `_pulseSpeed`, `_popTime`.
- **PunishmentFactory.Burst**: `WindGust`(6.5), размеры/время бёрстов — const там же.
- **Meteor**: `_maxAngleOffset`(3.5), `_fallDuration`. **ImpactFlash**: `_endScale`(4.2), цвета `_hotColor`/`_fireColor`.
- **PlayerController**: `_promptColor`/`_promptHighlightColor`/`_dropOffset`, `Push`/`SetExternalVelocity`/`ApplySlow`/`Blink`.
- **FieldSpawner**: `_maxOnField`, `_interval`, `_maxSpawnY`(1.95 — верх под алтари свободен), `_centerKeepout`(0).
- **Sheep**: `_detectRadius`2, `_minSpeed`3, `_maxSpeed`8, `_edgeAvoidDistance`1.5 (override на Sheep.prefab).
- **Tree**: `_holdTime`1, `_zoneRadius`0.6. **Puddle**: `_slowFactor`0.45.
- Расстановка алтарей (rowY 3.8 / stepX 2.0) + стена (y≈2.25) — заданы скриптом-расстановкой, не инспектором.

---

## Что НЕ сделано (осознанные упрощения прототипа)

- Кары **не убивают** — только мигание/замедление/снос. Нет HP/смерти/game over.
- Нет счёта, прогрессии между уровнями (1 уровень, `LevelCleared` срабатывает но дальше no-op), UI-HUD, звука, Boot/End интеграции.
- Прогресс зон дерева не сбрасывается при выходе.
- Враг-кара заменена на лужи временно.
- **Не проверено в play** (правки этой сессии): достаёт ли игрок до зон сдачи через стену; не лезут ли Run-зоны (`_runArea`/`_runSpread`) в зоны сдачи/за экран; гнев-ринг+иконки над алтарями (y≈4.75) на краю экрана; баланс гнева/таймеров/мини-кар.
- Darkness не имеет хорошей one-shot мини-версии → при таймауте лобается метеор (заглушка).

## TODO / следующая сессия
- Прогон в play, подкрутка раскладки (зоны сдачи vs стена, Run-зоны), таймингов гнева/задания.
- Возможно: HUD прогресса уровня (X/12), звук, переход на след. уровень.
- Коммит блока изменений сессии (per-altar модель, ряд, стены, мини-кары, гнев-огонь) — **ещё не закоммичено**.
