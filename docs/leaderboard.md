# Лидерборд — устройство и решения

Топ-100 на экране конца игры. Бэкенд — **Supabase** (Postgres + PostgREST).
Клиент — чистый `UnityWebRequest`, без SDK (важно для размера WebGL-билда).

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
  uid        text primary key,
  name       text not null,
  score      int  not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index scores_score_idx on public.scores (score desc);

alter table public.scores enable row level security;
create policy "anon read"   on public.scores for select to anon using (true);
create policy "anon insert" on public.scores for insert to anon with check (true);
create policy "anon update" on public.scores for update to anon using (true) with check (true);
```

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
  корутин). `SubmitScore(score, onDone)` (upsert), `FetchTop(limit, onResult, onError)`,
  `IsAvailable`. Всё best-effort: ошибка → callback провала → UI прячет борд.
- **Gameplay/LeaderboardView.cs** — ЛОГИКА (не строит UI). `[SerializeField]` ссылки
  на scene-объекты (panel, content, rowPrefab, nameInput, renameButton). На
  `GameHud.GameOverShown(int)` → submit → fetch → спавн строк из rowPrefab.
- **Gameplay/LeaderboardRow.cs** — строка (rank/name/score TMP), метод `Set`.

UI собран **на сцене вручную** (см. `docs/ui-conventions.md` — почему не кодом).
`LeaderboardView`/`Row` висят на Canvas / row-шаблоне, ссылки заведены в инспекторе.

### REST-детали

- Fetch топа: `GET {url}/rest/v1/scores?select=name,score&order=score.desc&limit=100`.
- Submit (upsert): `POST {url}/rest/v1/scores?on_conflict=uid`, заголовок
  `Prefer: resolution=merge-duplicates`, тело `{uid,name,score}`.
- Заголовки на каждый запрос: `apikey: <anonKey>`, `Authorization: Bearer <anonKey>`.
- **JsonUtility не парсит top-level массив** — ответ-массив оборачивать
  `{"items": <array>}` и парсить wrapper-класс с полем `items`.

## Поток на game-over

`BalloonGameController.EndRound` → `GameHud.ShowGameOver(score)` поднимает
`GameOverShown(score)` → `LeaderboardView` ловит → submit скор → fetch топ-100 →
рисует строки. Один путь, без дублей обработки.

## Хвосты / TODO

- Тест-строки в БД (`selftest-001` и т.п. от ручных проверок) — удалить в Supabase
  Table Editor перед релизом.
