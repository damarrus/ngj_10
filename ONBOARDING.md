# Onboarding — ngj_10 (для Claude Code)

> Этот файл — инструкция **для Claude**. Новый разработчик склонировал репозиторий
> и запустил Claude Code в папке проекта. Claude: проведи его по шагам ниже,
> выполняя команды и проверяя результат на каждом этапе. Не вываливай всё разом —
> веди по одному шагу, дожидаясь готовности (установка Unity/пакетов долгая).

Проект: Unity 2D геймджем-болванка (URP, WebGL-таргет). Подробности — `CLAUDE.md`.
Работа с Editor идёт через MCP-мост **IvanMurzak/Unity-MCP**. Грабли моста —
`docs/unity-mcp-notes.md` (прочитай перед серией MCP-команд).

---

## Что уже в репозитории (git)
- Весь код (`Assets/_Project/Scripts/`), сцены, SO-ассеты ачивок.
- `Packages/manifest.json` — уже содержит пакет моста `com.ivanmurzak.unity.mcp`
  + OpenUPM scoped-registry. Unity подтянет при открытии.
- `.claude/skills/` — обёртки MCP-tools.

## Чего НЕТ (локально-специфично, не в git — надо создать)
- `.mcp.json` — конфиг MCP для Claude Code (в `.gitignore`: содержит токен/пути).
- `Library/` — кэш Unity, включая скачиваемый exe моста.
- `UserSettings/AI-Game-Developer-Config.json` — настройки плагина (режим связи).

---

## Шаг 1. Предусловия

Проверь наличие. Если чего-то нет — дай ссылку, не ставь сам.
- **Unity Hub** + **Unity 6000.4.9f1** (точная версия из `ProjectSettings/ProjectVersion.txt`).
  Поставить через Hub. Открыть проект Hub'ом один раз (импорт пакетов — долго,
  предупреди юзера).
- **Node.js** (для `npx unity-mcp-cli`). Проверка: `node -v`.

```
node -v
cat ProjectSettings/ProjectVersion.txt
```

## Шаг 2. Открыть проект в Unity

Пусть юзер откроет проект в Unity Hub (нужной версией). Unity:
- подтянет пакет моста из OpenUPM,
- плагин **сам скачает** серверный exe в `Library/mcp-server/win-x64/`.

Дождаться полного импорта (Console без спиннера компиляции). Проверь, что exe появился:
```
ls Library/mcp-server/win-x64/unity-mcp-server.exe
```
(нет файла → плагин ещё качает или не импортнулся; подожди / переоткрой Unity.)

## Шаг 3. Переключить мост в локальный режим (Custom)

После установки плагин по умолчанию в режиме **Cloud** (шлёт на ai-game.dev,
требует аккаунт/токен — нам НЕ нужно). Переключаем на локальный:

1. В Unity: меню **`Window → AI Game Developer`**.
2. Секция **Connection**, переключатель `Custom | Cloud` → нажать **`Custom`**.
   Блок "Authorization Required" должен исчезнуть, "Unity: Connecting" → Connected.

Это **единственное** действие, которое юзер делает руками в окне Unity.
(Опционально, чтобы прописать локальный host/token в конфиг до клика:
`npx unity-mcp-cli bootstrap-local --url http://localhost:26778 --token <token-из-окна>`
— но клик `Custom` в окне всё равно нужен, чтобы плагин поднял локальный сервер.)

## Шаг 4. Создать `.mcp.json` для Claude Code

Файла нет в git. Создай его в корне проекта (подставь реальный абсолютный путь репо;
exe из шага 2):

```json
{
  "mcpServers": {
    "ai-game-developer": {
      "command": "<АБСОЛЮТНЫЙ_ПУТЬ_РЕПО>/Library/mcp-server/win-x64/unity-mcp-server.exe",
      "args": ["--port=8080", "--plugin-timeout=10000", "--client-transport=stdio"]
    }
  }
}
```
(macOS/Linux — другой подкаталог вместо `win-x64`: `osx-arm64`/`osx-x64`/`linux-x64`;
сверь по факту в `Library/mcp-server/`.)

`.mcp.json` уже в `.gitignore` — коммитить его не надо (локальные пути/токен).

## Шаг 5. Проверить связь

```
npx unity-mcp-cli status
```
Цель: `Local MCP Server ... SUCCESS: Connected`.
- `connection refused` → плагин не в Custom-режиме (вернись к шагу 3) или Unity закрыт.
- `HTTP 401` на ai-game.dev → ещё в Cloud-режиме (шаг 3).

Затем перезапусти Claude Code, чтобы он прочитал новый `.mcp.json` и поднял
stdio-сервер. После рестарта: `claude mcp list | grep ai-game` → `✓ Connected`.

## Шаг 6. Дымовой тест

Через CLI (Claude может звать так же, как описано в `docs/unity-mcp-notes.md`):
```
npx unity-mcp-cli run-tool ping --input '{}'
npx unity-mcp-cli run-tool assets-refresh --input '{}'
```
Запусти игру и проверь, что ачивки грузятся:
```
npx unity-mcp-cli run-tool editor-application-set-state --input '{"isPlaying":true}'
# подожди пару секунд, потом:
npx unity-mcp-cli run-tool console-get-logs --input '{}'
# ищи "[Achievements] Loaded 3 definition(s)."
npx unity-mcp-cli run-tool editor-application-set-state --input '{"isPlaying":false}'
```
(`editor-application-*` может быть выключен в конфиге — включить:
`run-tool tool-set-enabled-state --input '{"tools":[{"name":"editor-application-set-state","enabled":true}]}'`.)

## Готово

Связь есть, ачивки грузятся. Play/правки через MCP работают **без кликов** в Unity
(фокус-throttle старого моста отсутствует). Дальше — обычная работа по `CLAUDE.md`.

### Если что-то не так
- Полный разбор моста, режимов и граблей формата запросов — `docs/unity-mcp-notes.md`.
- Установщик плагина иногда перезаписывает `.mcp.json` на cloud+токен — вернуть на
  локальный stdio (шаг 4).
