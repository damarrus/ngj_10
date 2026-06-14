# Лидерборд — устройство и решения

Топ-100. Бэкенд — **Supabase** (Postgres + PostgREST).
Клиент — чистый `UnityWebRequest`, без SDK (важно для размера WebGL-билда).

## Icarus: что ранжируем (3 поля)

Игра пишет на каждой смерти и на достижении солнца. Три ранжируемых поля:

1. **Максимальная высота** (`max_height`, целые метры, округление вверх `CeilToInt`)
   — берётся из `RunStats.MaxHeightMeters` (пик над точкой спавна за ран).
2. **Время до пика** (`time_to_max`, миллисекунды `int`) — `RunStats.TimeToMaxMs`,
   считается от старта рана (первый взлёт) до момента, когда был поставлен пик.
3. **Число выполненных ачивок** (`achievements`, `int`) — `AchievementManager.UnlockedCount`.

**Правила записи (клиент, в `LeaderboardReporter` — не серверный триггер):**
- Высота больше прежней → пишем высоту И её время (даже если время хуже).
- Та же высота, время меньше → обновляем только время.
- Ачивок больше → обновляем число.
- Персональный best держим целиком в PlayerPrefs (`lb.best.height/time/ach`),
  потому что upsert перетирает всю строку — шлём накопленный полный best.

**Сортировка:** `max_height` ↓, при равенстве `time_to_max` ↑, затем `achievements` ↓,
затем `name` ↑. Большинство долетает до верха уровня → равная высота → гонка по времени.

**Поток на конец рана:** смерть (`Die()` — выход за границы / R) или победа (`Win()` —
солнце) → `LevelController.RunFinished` → `LeaderboardReporter` снимает 3 поля с RunStats +
AchievementManager, сравнивает с best, делает условный upsert. Один путь, без дублей.

`LeaderboardReporter` висит на объекте **Icarus** (рядом с `AchievementReporter` —
тот же bridge-паттерн: уровень и клиент лидерборда не знают друг о друге). Ссылки
`_level`/`_stats` заведены в инспекторе.

> UI пока НЕ сделан — заготовлен только код. `FetchTop` уже возвращает все 4 поля
> (`name` + 3 ранжируемых) в `ScoreEntry`.

## Принятые решения (почему так)

- **Supabase, не UGS/PlayFab.** Готовый REST над Postgres, CORS из коробки, free
  tier, ноль SDK в билде. UGS/PlayFab тащат вес SDK + аккаунты — оверкилл для джема.
- **Идентичность игрока — локальный uid.** При первом запуске генерим `uid` (GUID)
  и имя «adjective + animal» (`Lazy Llama`), храним в PlayerPrefs (WebGL → IndexedDB).
  Без логина. Имя можно менять. Скор привязан к uid.
- **Дубли имён ок.** Уникальность имени не enforce-им — uid разводит строки.
- **Upsert по uid** — одна строка на игрока, храним лучший скор. Условие «только если
  больше» — на клиенте (сравнить с PlayerPrefs), не серверным триггером.
- **Offline → борд скрыт, виден только счёт.** Нет конфига / нет сети / ошибка
  запроса → панель не показывается. Требование: игра без интернета работает.

## Серверная часть (Supabase, делается руками в дашборде)

1. Создать проект на supabase.com (free).
2. При создании включить **Enable automatic RLS** (подстраховка; SQL всё равно
   включает RLS явно).
3. SQL Editor → выполнить:

```sql
create table public.scores (
  uid          text primary key,
  name         text not null,
  score        int  not null,          -- legacy single-number board; Icarus mirrors max_height сюда
  max_height   int  not null default 0, -- целые метры (округл. вверх) — первичный сорт desc
  time_to_max  int  not null default 0, -- миллисекунды до пика — тай-брейк asc
  achievements int  not null default 0, -- число выполненных ачивок — второй тай-брейк desc
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);
create index scores_rank_idx
  on public.scores (max_height desc, time_to_max asc, achievements desc, name asc);

alter table public.scores enable row level security;
create policy "anon read"   on public.scores for select to anon using (true);
create policy "anon insert" on public.scores for insert to anon with check (true);
create policy "anon update" on public.scores for update to anon using (true) with check (true);
```

Если таблица `scores` уже была (старый одно-числовой борд) — миграция вместо create:

```sql
alter table public.scores
  add column max_height   int not null default 0,
  add column time_to_max  int not null default 0,
  add column achievements int not null default 0;
create index scores_rank_idx
  on public.scores (max_height desc, time_to_max asc, achievements desc, name asc);
```

`score` оставлен — Icarus-клиент мирорит в него `max_height`, чтобы старый борд Game-сцены
(читает `ScoreEntry.score`) не сломался.

4. Settings → API → скопировать **Project URL** + **anon public** ключ (НЕ
   service_role — тот секретный, в билд нельзя).
5. Вписать URL+key в `LeaderboardConfig.asset` (Inspector).

### Известная дыра (для джема оставлена)

`anon update` policy `using(true)` пускает **любого** с anon-ключом (а он публичен в
WebGL-билде) править ЛЮБУЮ строку, не только свою. Читерство возможно. Для джема ок.
Строгий фикс требует Supabase anonymous auth + `auth.uid()` в policy — оверкилл.

## Клиентская часть (код)

`Assets/_Project/Scripts/`:

- **Core/Leaderboard/PlayerIdentity.cs** — static. `Uid`, `Name`, `Rename(string)`.
  Генератор имён из двух массивов (adjective × animal).
- **Core/Leaderboard/LeaderboardConfig.cs** — ScriptableObject (ProjectUrl, AnonKey,
  Table). Лежит в `Assets/_Project/Resources/LeaderboardConfig.asset`, грузится
  `Resources.Load`. anon key публичен by design (гейтит RLS) — в билде норм.
- **Core/Leaderboard/LeaderboardClient.cs** — lazy-singleton MonoBehaviour (хост
  корутин). `SubmitRun(maxHeight, timeToMaxMs, achievements, onDone)` (Icarus-upsert),
  `SubmitScore(score, onDone)` (legacy одно-число), `FetchTop(limit, onResult, onError)`,
  `IsAvailable`. `ScoreEntry` = `name` + `score` (legacy) + `max_height/time_to_max/achievements`.
  Всё best-effort: ошибка → callback провала → UI прячет борд.
- **Gameplay/LeaderboardReporter.cs** — мост Icarus-рана к клиенту. Слушает
  `LevelController.RunFinished`, снимает высоту/время/ачивки, ведёт условный best в
  PlayerPrefs, делает upsert. Висит на объекте **Icarus**, ссылки `_level`/`_stats`
  в инспекторе. (Аналог `AchievementReporter`.)
- **Gameplay/LeaderboardView.cs** / **LeaderboardRow.cs** — UI старого Game-борда
  (читает `ScoreEntry.score`). Для Icarus UI пока НЕ переиспользован — будет отдельно.

### REST-детали

- Fetch топа: `GET {url}/rest/v1/scores?select=name,score,max_height,time_to_max,achievements`
  `&order=max_height.desc,time_to_max.asc,achievements.desc,name.asc&limit=100`.
- Submit (upsert): `POST {url}/rest/v1/scores?on_conflict=uid`, заголовок
  `Prefer: resolution=merge-duplicates`, тело `{uid,name,score,max_height,time_to_max,achievements}`.
- Заголовки на каждый запрос: `apikey: <anonKey>`, `Authorization: Bearer <anonKey>`.
- **JsonUtility не парсит top-level массив** — ответ-массив оборачивать
  `{"items": <array>}` и парсить wrapper-класс с полем `items`.

## Поток на конец рана (Icarus)

`LevelController.Die()` (смерть) / `Win()` (солнце) → событие `RunFinished` →
`LeaderboardReporter` снимает 3 поля с `RunStats` + `AchievementManager`, сравнивает с
PlayerPrefs-best, условный upsert. `RunFinished` поднимается ДО респавна — пока RunStats
держит значения завершившегося рана. Один путь, без дублей.

## Хвосты / TODO

- Тест-строки в БД (`selftest-001` и т.п. от ручных проверок) — удалить в Supabase
  Table Editor перед релизом.
