# Unity efficiency — ловушки, которые жрут итерации

Шпаргалка «как не наступить дважды». Дополняет CLAUDE.md (философия, конвенции) и
`docs/unity-mcp-notes.md` (механика моста). Здесь — поведение Unity/WebGL/архитектуры,
что ломает работу не сразу, а потом. Проверено по докам Unity 6 / IvanMurzak-MCP.

Скиллы, которые автоматически триггерятся по задаче (живут в `.claude/skills/`):
- **`unity-ui`** — постройка/итерация UI (RectTransform, layout, TMP, prefab-template).
- **`unity-verify`** — проверка после правок (L1 compile → L2 play → L3 screenshot,
  read-back после wiring).

Этот файл — справочник, читать когда задача попадает в раздел ниже.

---

## 1. MonoBehaviour / lifecycle

- **Подписка событий: `OnEnable` ↔ `OnDisable` парой, всегда.** Подписка в
  `Awake`/`Start` без отписки = НЕ утечка, а **двойной вызов**: при каждом
  re-enable добавляется ещё один обработчик. Симптом — «сработало дважды».
  (В `LeaderboardView.cs` уже сделано правильно — копировать паттерн.)
- **`Awake` — для себя, `Start` — для чужих объектов.** Порядок `Awake`/`OnEnable`
  между объектами НЕ гарантирован; `Start` идёт после всех `Awake`. Кросс-ссылки
  на другие объекты искать в `Start`, не в `Awake`. Script Execution Order — крайняя
  мера.
- **`[SerializeField] private`, не `public`** для тюнинга в инспекторе. Magic numbers
  геймплея → `[SerializeField]`. (Правило CLAUDE.md.)
- **`RequireComponent` вместо россыпи defensive `GetComponent`-null-чеков** — гарантия
  наличия на add-time, убирает класс NullRef.
- **Null serialized-ref = wiring не записался, а не баг кода.** Не «чинить» рабочий
  код, гоняясь за NullRef — это пустой слот инспектора. Проверка — read-back
  (`unity-verify`, раздел L-wiring).

## 2. Enter Play Mode без Domain Reload (быстрая итерация Unity 6)

Если domain reload выключен ради скорости — две ловушки:
- **Static-поля и static-события ПЕРЕЖИВАЮТ между Play-сессиями.** Старые подписчики
  остаются, счётчики держат старое значение. Симптом — «работает 1 раз, на 2-й Play
  ломается», выглядит недетерминированно. Фикс: сбрасывать статику явно через
  `[RuntimeInitializeOnLoadMethod]`, не полагаться на reload.
- **Private-поля, изменённые в рантайме, могут НЕ сброситься** к инспектор-значениям
  на повторном Play. В реальном билде поведение отличается. Не закладываться на
  «editor сбросит».

## 3. WebGL — кусает поздно (главный таргет проекта)

- **Один C#-поток.** Нет `System.Threading.Thread`, нет `Task.Run`-оффлоада. Только
  корутины / `Awaitable` / async на main thread. Threaded-код компилится в Editor,
  умирает в браузере.
- **Persistence → IndexedDB через PlayerPrefs / persistentDataPath, НЕ флашится
  мгновенно.** Звать `PlayerPrefs.Save()`. Данные «в памяти» теряются при закрытии
  вкладки, если не сброшены в IndexedDB. Браузер может ограничить размер.
- **Звук — только по user-gesture** (autoplay-блок браузера). Не рассчитывать на
  звук до первого клика/клавиши. «Нет звука на загрузке» — норма, не баг.
- **IL2CPP стрипает неиспользуемые типы → NullRef/missing-type ТОЛЬКО в WebGL-билде.**
  Не reflection-heavy код. Если рефлексия нужна — `link.xml` для сохранения типов.
  В Editor не воспроизводится → вылезает после билда.
- **Сеть — только `UnityWebRequest`/fetch.** Нет raw-сокетов (sandbox браузера).
- **Размер билда = время загрузки.** Сжимать спрайты, стрипать неиспользуемые пакеты,
  включить code stripping. Долгая загрузка убивает первое впечатление от jam-entry.

## 4. Performance hygiene (WebGL, jam-объём)

- **GC в WebGL — раз в кадр (стек должен быть пуст).** Аллокации копятся внутри кадра,
  статтер бьёт залпом. Правило: НОЛЬ аллокаций в `Update` — без LINQ, без `new` в
  hot-loop, без boxing, без склейки строк. В WebGL бьёт сильнее, чем на нативе.
- **Кешировать `GetComponent`/`Find` на init, НЕ звать per-frame.**
  `GameObject.Find`/`FindObjectOfType` — O(сцена), максимум раз в `Awake`/`Start`.
- **Пул — только когда реально спавнишь/убиваешь много** (пули, партиклы, pop-эффекты).
  Порог, не рефлекс. Когда нужен — обёртка над `UnityEngine.Pool`, не самопис
  (см. memory `object-pool-decision`).
- **Sprite Atlas — резать draw calls; следить за overdraw на прозрачном 2D.** Каждая
  смена материала/текстуры = draw call; стопка прозрачных квадов = тихий слив FPS.

## 5. Scene & prefab дисциплина (с MCP)

- **Никогда не править `.unity`/`.prefab`/`.asset` YAML руками** — GUID+fileID
  ломаются молча. Только MCP. (CLAUDE.md.)
- **Всё в префабы, сцены тонкие.** Правки бьют по файлу префаба, не сцены — меньше
  merge-конфликтов, параллельная работа без драки за один `.unity`.
- **Dirty-сцена = только в памяти.** Перед коммитом, трогающим сцену/префаб:
  `scene-list-opened` → `IsDirty:true` → `scene-save`. Иначе закоммитишь старый YAML.
- **`.meta` всегда вместе с ассетом** — там GUID, без него ссылки осиротеют по проекту.
- **instanceID session-volatile** — не кешировать между вызовами/сессиями,
  re-`gameobject-find` по path/name. Переживает stop Play, НЕ domain reload/рестарт.

## 6. AI-agent workflow (специфика Unity-MCP)

- **Success ≠ applied.** После `gameobject-component-modify` с `{"instanceID":N}` —
  `gameobject-component-get` и сверить, что поле реально держит ссылку. Главный
  источник «связал, а оно null». (Подробно — скилл `unity-verify`.)
- **`script-execute` (Roslyn one-shot) для разовых операций; файл `.cs` — только для
  постоянного кода.** Разовый запрос/батч-правка состояния сцены/проверка гипотезы →
  one-shot, не сорить scratch-скриптами и не гонять recompile.
- **Батчить правки** в один `gameobject-modify` — меньше round-trip, токенов,
  частичных применений.
- **`assets-find` с фильтром (`t:`/`l:`/`glob:`)** вместо скана; path-scoped чтение
  (`paths`/`viewQuery`) на get-data — не тащить весь блоб ради одного поля.
- **Поиск кода — делегировать subagent** (`Explore`/`cavecrew-investigator`), main-тред
  на имплементацию+wiring. Контекст — главный лимит.
- **Скриншот для проверки — только Play Mode или скрин оператора.** `screenshot-game-view`
  в Edit Mode без фокуса окна = мёртвый/кешированный кадр (правки не видны) → ложные
  выводы. Подробно — `docs/ui-conventions.md` + `docs/unity-mcp-notes.md`.
- **Play Mode через `editor-application-set-state` не всегда без кликов:** `IsPlaying`
  иногда остаётся false без фокуса окна (set-state «успех», playmode не стартует) →
  просить оператора нажать Play (Ctrl+P). См. `unity-mcp-notes.md`.

## 7. Архитектура под jam (что агент тащит хорошо)

- **C# events / `UnityEvent` для связи систем, НЕ ручные кросс-ссылки.** Владелец
  поднимает событие, потребитель подписывается. (CLAUDE.md core.)
- **ScriptableObject для конфига/тюнабельных.** Переживает загрузку сцен без
  синглтонов, редактируется в инспекторе, тюн без recompile. SO event-channels —
  только если реально нужен кросс-сценовый decoupling, не по дефолту.
- **Один `GameManager` (persistent), остальное локально.** Lazy self-create в
  `Instance` (memory `lazy-singleton-managers`). Не плодить менеджеры.
- **Не оверинженерить:** без DI/event-bus/ECS/абстрактных фабрик. Прямой код.
  (memory `no-architecture-patterns`.)
- **Один инпут → один обработчик.** Не мешать ручной polling мыши с UI `onClick` на
  один клик — корень frame-guard-костылей, которые CLAUDE.md запрещает.

---

Источники (на момент сбора, Unity 6000.x): Unity Manual (WebGL technical overview,
webgl-memory, configurable-enter-play-mode, YAML serialization, test-framework),
IvanMurzak/Unity-MCP repo+wiki, Unity blog (ScriptableObject architecture, scenes &
prefabs with VC), Claude Code best-practices/subagents.
