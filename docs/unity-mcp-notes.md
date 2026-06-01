# Unity + MCP — заметки по работе через мост

Мост: **IvanMurzak/Unity-MCP** (`com.ivanmurzak.unity.mcp` v0.77.0).
Unity 6000.4.9f1, URP 2D.

Переехали с CoderGamester/mcp-unity (2026-06-01) — у того `update_component`
вообще не проставлял `[SerializeField]`-ссылки (ставил `null`, врал "success";
баг в коде, фикс PR #106 не влит). Старые заметки про тот мост — в git-истории.

---

## Архитектура нового моста — ВАЖНО, иначе запутаешься

Два разных канала к Unity, оба должны быть живы:

1. **Локальный HTTP-сервер** плагина в Unity на `http://localhost:26778` (+ токен).
   Поднимается плагином, когда Connection-режим = **Custom/Local**.
2. **CLI `unity-mcp-cli`** — реально исполняет tool-вызовы, шлёт HTTP на (1).
   Skills в `.claude/skills/` — обёртки над CLI.

### Режим подключения (Cloud vs Custom) — корень всех бед

Окно `Window → AI Game Developer`, секция **Connection**, переключатель
`Custom | Cloud`:

- **Cloud** (дефолт после установки) — плагин шлёт на `https://ai-game.dev/mcp`,
  требует Authorize/Bearer-аккаунт. Локальный сервер 26778 **НЕ поднимается**.
  → всё даёт `connection refused` / `HTTP 401`. НЕ НАШ режим.
- **Custom** — локальный режим, плагин слушает `localhost:26778`. ← НУЖЕН ЭТОТ.

Если после рестарта/реимпорта вернулось в Cloud: жать **Custom** в окне.
Файл-конфиг: `UserSettings/AI-Game-Developer-Config.json` →
`"connectionMode"` должно быть `Custom` (не `Cloud`), `host` = localhost:26778.
Переключить из CLI (но плагин всё равно надо ткнуть в окне, чтобы перечитал):
`npx unity-mcp-cli bootstrap-local --url http://localhost:26778 --token <tok>`

### Claude Code `.mcp.json` (untracked, в .gitignore — локальный)

Указывает на локальный stdio-exe:
```json
{ "mcpServers": { "ai-game-developer": {
  "command": "C:/projects/ngj_10/Library/mcp-server/win-x64/unity-mcp-server.exe",
  "args": ["--port=8080","--plugin-timeout=10000","--client-transport=stdio"] } } }
```
ВНИМАНИЕ: установщик плагина перезаписывает `.mcp.json` на HTTP-cloud-вариант
с токеном. Если так — вернуть на локальный stdio (выше). Токен в git не коммитить
(`.mcp.json` в .gitignore).

`claude mcp list | grep ai-game` → должно быть `✓ Connected`.

---

## Проверка связи (делать при старте сессии)

```
npx unity-mcp-cli status
```
Хочешь видеть: `Local MCP Server ... SUCCESS: Connected`.
Если `connection refused` → плагин в Cloud-режиме или Unity не запущен.

---

## Вызов tools — через CLI

```
npx unity-mcp-cli run-tool <tool-name> --input '<json>' --raw
# крупный/многострочный JSON → --input-file file.json
```
exe в Library локально-специфичен; CLI сам берёт url/token из конфига проекта.

---

## ССЫЛКИ (refs) РАБОТАЮТ — проверено экспериментом

В отличие от старого моста, IvanMurzak резолвит object-references по **instanceID**
в живой объект (через `EditorUtility.EntityIdToObject`) и реально присваивает.

Тест (2026-06-01): WiringProbe c полями GameObject/Transform/MonoBehaviour,
связал на другой scene-объект → `object-get-data` показал реальные значения
(`targetGo: {instanceID: -43044}` = целевой объект), не null. Scene-to-scene OK.

### Формат связывания

`gameobject-component-modify`, `componentDiff.fields[]`, value = `{"instanceID": N}`:
```json
{
  "gameObjectRef": {"instanceID": -43040},
  "componentRef":  {"instanceID": -43048},
  "componentDiff": {
    "typeName": "Ngj10.Gameplay.Foo",
    "fields": [
      {"typeName":"UnityEngine.GameObject","name":"target","value":{"instanceID":-43044}}
    ]
  }
}
```
- null-ссылка: `"value": {"instanceID": 0}`.
- ВСЕГДА брать свежий instanceID в той же сессии (`gameobject-find` /
  `gameobject-create` возвращает его). InstanceID session-scoped, не переживает
  рестарт Editor. Долгоживущая альтернатива — `path`/`name` в GameObjectRef.

Вывод: **на новом мосту инспектор-связывание через MCP можно использовать.**
Code-driven (GetComponent/transform.Find/FindAnyObjectByType) остаётся валидным
запасным путём, но больше не обязателен из-за бага.

---

## Имена tools (новый мост)

Дефис-кейс, не snake. Основные:
- GameObject: `gameobject-create`, `gameobject-find`, `gameobject-modify`,
  `gameobject-destroy`, `gameobject-duplicate`, `gameobject-set-parent`
- Component: `gameobject-component-add` (param **`componentNames`** — массив!),
  `gameobject-component-get`, `gameobject-component-modify`,
  `gameobject-component-list-all`, `gameobject-component-destroy`
- Object: `object-get-data`, `object-modify`
- Scene: `scene-create/open/save/list-opened/set-active/unload/get-data`
- Assets: `assets-find`, `assets-get-data`, `assets-modify`, `assets-refresh`,
  prefab: `assets-prefab-create/open/save/close/instantiate`
- Script: `script-execute` (Roslyn), `script-read/update-or-create/delete`
  (часть выключена в конфиге — `tool-set-enabled-state` включает)
- Console: `console-get-logs`, `console-clear-logs`
- Прочее: `tests-run`, `reflection-method-find/call`, `type-get-json-schema`,
  `screenshot-*`, `profiler-*`, `ping`

Полный список + enabled-флаги: `AI-Game-Developer-Config.json` (`tools[]`),
или `npx unity-mcp-cli run-tool unity-tool-list`.

### Грабли формата запросов (реально набитые шишки)

Имена параметров на этом мосту НЕ совпадают с интуицией / со старым мостом.
Не угадывать — если ошибся, проверить схему:
`npx unity-mcp-cli run-tool unity-tool-list --input '{"includeInputs":"InputsWithDescription"}'`
(`includeInputs` — ENUM-строка: `None|Inputs|InputsWithDescription`, НЕ bool!
`true`/`All` → ошибка валидации).

Конкретные грабли (param → правильное значение):

- `gameobject-component-add`: компоненты в **`componentNames`** (массив строк),
  НЕ `componentName`/`componentNames` строкой. Иначе "No component names provided".
  Имя компонента — полное с namespace: `Ngj10.Gameplay.Foo`.

- `script-execute`: код в **`csharpCode`** (НЕ `code`). И это **full-code режим**:
  нужен класс с именем **`Script`** и **static**-методом `Main` (НЕ instance —
  иначе "Non-static method requires a target"). Шаблон:
  ```
  using UnityEngine;
  public class Script { public static string Main() { /* ... */ return "ok"; } }
  ```
  Возврат строки удобен — приходит в `result.value`. Многострочный код → `--input-file`.

- `editor-application-set-state`: параметр **`isPlaying`** (bool), НЕ `playmode`/`state`.
  `{"isPlaying":true}` стартует Play, `{"isPlaying":false}` стопает.
  (Доп. `isPaused`.) Запуск Play **БЕЗ кликов в Unity** работает (проверено).

- Ссылки на объекты везде — **`{"instanceID": N}`** (camelCase, большая D),
  НЕ `instanceId`/имя-строкой. `gameObjectRef`/`componentRef`/`objectRef` — все так.

- Многие tools выключены в `AI-Game-Developer-Config.json` (`"enabled": false`):
  `editor-application-*`, `script-read/update-or-create/delete`, `reflection-*`,
  `package-*`, `profiler-*`, `assets-copy/delete/move` и др. Если tool даёт
  "no-op"/не находится — проверить флаг и включить:
  `run-tool tool-set-enabled-state --input '{"tools":[{"name":"X","enabled":true}]}'`.

- Большие ответы (`console-get-logs`, `unity-tool-list`) CLI обрезает и пишет в файл —
  грепать файл, не пытаться читать всё. Фильтр `filter` в `unity-tool-list`
  работает слабо — проще получить весь дамп и грепнуть.

### Чтение результатов

- Успех tool ≠ успех операции. Старый мост врал "success" на пустом wiring.
  Для критичных операций (особенно ссылки) — **читать обратно** через
  `object-get-data` / `gameobject-component-get` и сверять реальное значение.

---

## Компиляция скриптов

После правки `.cs` файлами — `npx unity-mcp-cli run-tool assets-refresh --input '{}'`
(компилит, ждёт). Либо фокус Editor. `script-update-or-create` валидирует Roslyn'ом
до записи и сам рефрешит.

## Find* API в Unity 6

- `FindObjectOfType` / `FindFirstObjectByType` — **deprecated** (CS0618).
- `FindAnyObjectByType<T>()` — **OK**, без warning. Использовать его.

## Фокус-throttle — БОЛЬШЕ НЕ ПРОБЛЕМА

Главная грабля старого моста (WS-таймаут без фокуса Editor, просили клик перед
каждой серией команд). На новом мосту (локальный HTTP `localhost:26778`) **ушла**.
Проверено: refresh, create/modify/destroy объектов, wiring, создание ассетов,
`script-execute`, **запуск/остановка Play** — всё прошло БЕЗ единого клика в Unity,
окно вне фокуса. Просить оператора кликать в Unity больше не нужно.

Единственное действие, что требует окна Unity руками — переключение
Connection-режима Cloud→Custom (один раз после установки/реимпорта плагина).
