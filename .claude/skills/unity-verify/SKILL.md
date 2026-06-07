---
name: unity-verify
description: Verify a Unity change actually works after editing scripts, scenes, components, or wiring via MCP. Use after any .cs edit, gameobject/component modify, prefab change, or inspector wiring — to catch compile errors, null refs, and "wiring said success but didn't apply" before moving on. Trigger on: finishing an edit, "does this work", "check it", before commit, after assets-refresh, after gameobject-component-modify.
---

# Unity verify — close the loop, don't edit blind

A tool returning "success" is NOT proof the change took effect. Escalate cheapest→
most expensive; stop at the first level that's enough for the change you made.

## L1 — Compile check (always, after any .cs change)

After `script-update-or-create` or editing `.cs` on disk + `assets-refresh`:
- `console-get-logs` filtered to errors. `script-update-or-create` Roslyn-validates
  *syntax* before writing, but **semantic/type errors only surface on recompile** —
  so still check the console.
- Confirm compile finished: `editor-application-get-state` → `IsCompiling: false`.
- Beware STALE warnings: Unity console keeps old compiler warnings until a NEW
  compile, and `console-clear-logs` only clears the MCP cache, not Unity's. If a
  warning's line number doesn't match the current file (Read it), it's stale.

## L2 — Runtime smoke (for logic / lifecycle / null-ref risk)

1. `console-clear-logs` first — isolates this run from accumulated noise.
2. Enter Play: `editor-application-set-state {"isPlaying":true}`. Wait a few sec.
   ⚠️ Play does NOT always start without window focus — `IsPlaying` can stay false
   while set-state reports success. Verify with `editor-application-get-state`; if
   still false, ask the operator to press Play (Ctrl+P). (See unity-mcp-notes.md.)
3. Stop: `{"isPlaying":false}`.
4. `console-get-logs` → look for NullReferenceException / lifecycle errors.
   A null serialized ref usually means **wiring never persisted**, not bad code
   (see L-wiring) — don't "fix" working code chasing it.
5. Scene edits (`create`/`modify`/`duplicate`) FAIL in Play Mode
   ("This cannot be used during play mode"). Check `IsPlaying` before scene edits;
   stop play and wait for `IsPlaying:false`.

## L-wiring — Read back after every inspector wiring (success ≠ applied)

After `gameobject-component-modify` with `{"instanceID":N}` refs, the biggest
source of "I wired it but it's null":
- `gameobject-component-get` and confirm the field actually holds the reference.
- instanceID is **session-volatile** — re-`gameobject-find` by path/name for a
  fresh ID each session; never reuse a cached one. Survives Play stop, NOT domain
  reload / Editor restart.

## L3 — Visual check (for anything visible)

Only this catches layout/visual/looks-wrong. BUT: `screenshot-game-view` in EDIT
MODE without window focus = STALE/dead frame (or "render texture not available") —
your scene edits won't show, leading to false conclusions. Trust only:
- **Play Mode** MCP screenshot (live frame), or
- a screenshot the **operator** sends from their focused Game View.

See `unity-ui` skill + `docs/ui-conventions.md` for the full screenshot rule.

## L-tests — EditMode for pure logic (cheap, worth it even in jam)

- `tests-run` (EditMode) for pure logic: score, state transitions. Milliseconds.
- PlayMode tests are seconds and rarely pay off in jam scope — skip unless needed.
- PRECONDITION: `tests-run` aborts if any open scene is `IsDirty`. `scene-save`
  first (`scene-list-opened` to check).

## Pick the level
- Pure-logic .cs edit → L1 + maybe L-tests.
- Component/MonoBehaviour behavior → L1 + L2.
- Inspector wiring → L-wiring (read back), then L2 to confirm runtime init.
- Anything visible → add L3.
- Before commit touching scene/prefab → scene-save (dirty = in-memory only).
