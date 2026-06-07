---
name: unity-ui
description: Building or iterating Unity uGUI / TextMeshPro UI — RectTransform anchoring, ScrollRect/layout, spawning rows/panels, screenshot validation. Use BEFORE touching UI to avoid the slow re-iteration loop the leaderboard burned time on. Trigger on: "add a panel/button/list", "fix layout", "anchor this", "leaderboard row", "menu UI", anything touching RectTransform/TMP/LayoutGroup/ScrollRect.
---

# Unity UI — read the conventions, don't re-derive them

This project already paid for the UI lessons (the leaderboard). The full,
battle-tested rules live in **`docs/ui-conventions.md`** — READ IT before any UI
work. This skill is just the trigger + the short version so you don't re-litigate
decisions that are already settled.

## The settled rules (full detail in docs/ui-conventions.md)

1. **UI is built ON THE SCENE by hand, code holds only logic.** Do NOT generate UI
   from code — no `AddComponent<Image>()`, no setting `anchorMin/Max`/`offsetMin/Max`
   at runtime, no spawning TMP from a builder script as the persistent path.
   Code = logic + `[SerializeField]` refs to ready scene objects.
   - Why: can't tune in Play Mode (every size/font tweak = recompile); fine layout
     by code is blind iteration; the leaderboard burned many iterations before
     switching to scene objects.

2. **Repeated elements = `Instantiate` an inactive scene/prefab template**, then
   `SetActive(true)` on the clone. Template is laid out on the scene, not built in
   code. Pattern: `LeaderboardRow` / `LeaderboardView` — code only does `Set(...)`
   and spawn.

3. **A builder/generator script is a one-shot crutch — delete it right after.**
   Re-running it spawns duplicates and overwrites hand layout.

4. **Screenshot validation — Play Mode or operator only.** `screenshot-game-view`
   in EDIT MODE returns a STALE/dead frame (or "render texture not available") when
   the Unity window isn't focused — scene edits are real but the screenshot doesn't
   show them → false conclusions. Trust only: Play-Mode MCP screenshot, or ask the
   operator for a live Game-View screenshot. (See ui-conventions.md "Скриншот".)

5. **If you DO move a RectTransform via MCP** (точечно): anchor/pivot BEFORE
   position/size; center-anchor → `sizeDelta` = real size; split-anchor →
   `offsetMin/Max` not `sizeDelta`; never read layout size in the same frame.
   Full rules in ui-conventions.md.

## ScrollRect gotcha (already hit)
Content wider than Viewport → right column clipped by `RectMask2D`. Keep Content
horizontal edges flush to Viewport (`offsetMin.x=offsetMax.x=0`, stretch X). If
moving an inner element doesn't move the picture, the parent Row/Content is off-mask,
not the element. Full writeup: ui-conventions.md.

## TMP
- All UI text = `TextMeshProUGUI` (CLAUDE.md). `enableWordWrapping` deprecated →
  `textWrappingMode = TextWrappingModes.NoWrap`.
- Overlay above everything → own `Canvas` with `overrideSorting=true` + higher
  `sortingOrder` + own `GraphicRaycaster`, not `SetAsLastSibling`.

→ Before declaring UI done, verify per the **`unity-verify`** skill (and screenshot
rule #4 above).
