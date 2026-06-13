#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Ngj10.Gameplay;

namespace Ngj10.EditorTools
{
    /// <summary>
    /// Level overview + editor for a LevelData asset. One window shows the whole
    /// map from above (all streams, hazards, start, goal, kill line), lets you
    /// drag elements on a pan/zoom canvas, tweak the selected element's
    /// parameters live, and build the level into the open scene to verify.
    /// </summary>
    public class MapEditorWindow : EditorWindow
    {
        private const float InspectorWidth = 280f;

        // Serialized so the picked level survives domain reloads / serialization
        // round-trips and isn't reset to null mid-session.
        [SerializeField] private LevelData _level;
        private SerializedObject _serialized;

        private Vector2 _panWorld = Vector2.zero; // world point at canvas center
        private float _pixelsPerUnit = 24f;

        private enum SelKind { None, Stream, Hazard, Burner, Zeus, Start, Goal, KillLine }
        private SelKind _selKind = SelKind.None; // primary (last clicked) — drives the inspector
        private int _selIndex = -1;

        // Full selection set (includes the primary). Shift/Ctrl+click toggles entries.
        private readonly List<(SelKind kind, int index)> _multi = new List<(SelKind, int)>();

        private bool IsSelected(SelKind kind, int index) => _multi.Contains((kind, index));

        private void SetSingleSelection(SelKind kind, int index)
        {
            _multi.Clear();
            if (kind != SelKind.None)
                _multi.Add((kind, index));
            _selKind = kind;
            _selIndex = index;
        }

        private void ToggleSelection(SelKind kind, int index)
        {
            var entry = (kind, index);
            if (_multi.Remove(entry))
            {
                // Removed the primary — promote the last remaining entry (or none).
                if (_selKind == kind && _selIndex == index)
                {
                    if (_multi.Count > 0)
                        (_selKind, _selIndex) = _multi[_multi.Count - 1];
                    else
                    {
                        _selKind = SelKind.None;
                        _selIndex = -1;
                    }
                }
            }
            else
            {
                _multi.Add(entry);
                _selKind = kind;
                _selIndex = index;
            }
        }

        private Vector2 _inspectorScroll;
        private Rect _canvasRect;

        [MenuItem("NGJ/Редактор карт")]
        public static void Open() => GetWindow<MapEditorWindow>("Редактор карт");

        private void OnEnable()
        {
            if (_level == null)
                _level = Selection.activeObject as LevelData;
            if (_level == null)
                _level = FindFirstLevelAsset();
            RebuildSerialized();
        }

        /// <summary>First LevelData asset in the project (by path) — the default map.</summary>
        private static LevelData FindFirstLevelAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:LevelData");
            if (guids.Length == 0)
                return null;
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);
            return AssetDatabase.LoadAssetAtPath<LevelData>(paths[0]);
        }

        private void RebuildSerialized()
        {
            _serialized = _level != null ? new SerializedObject(_level) : null;
        }

        private const float RowHeight = 22f;
        private const float ToolbarHeight = RowHeight * 2f; // two rows: asset ops, then element ops

        private void OnGUI()
        {
            HandleDeleteCommand();
            HandleClipboardCommands();

            // Toolbar drawn with manual GUI (not GUILayout) so it never opens a
            // layout group — the canvas uses raw GUI.BeginClip and mixing the two
            // layout systems in one OnGUI desyncs the GUIClip stack.
            DrawToolbar(new Rect(0f, 0f, position.width, ToolbarHeight));

            if (_level == null)
            {
                GUI.Label(new Rect(8f, ToolbarHeight + 8f, position.width - 16f, 40f),
                    "Укажите ассет LevelData выше (или выберите его в окне Project).\n" +
                    "Создать новый: кнопка «Новый уровень» или Assets > Create > Ngj10 > Level.");
                return;
            }

            Rect body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight);
            _canvasRect = new Rect(body.x, body.y, body.width - InspectorWidth, body.height);
            Rect inspectorRect = new Rect(_canvasRect.xMax, body.y, InspectorWidth, body.height);

            DrawCanvas(_canvasRect);
            DrawInspector(inspectorRect);
        }

        /// <summary>
        /// Claim the editor's Delete/SoftDelete command so it removes the selected
        /// stream/hazard instead of bubbling up to "delete the selected asset". Unity
        /// fires ValidateCommand first (we must Use() it to opt in), then ExecuteCommand.
        /// </summary>
        private void HandleDeleteCommand()
        {
            Event e = Event.current;

            // Plain KeyDown fallback: the command event doesn't always arrive
            // (depends on focus), so the Delete key works on the canvas directly.
            if (e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                && !EditorGUIUtility.editingTextField)
            {
                bool any = false;
                foreach (var (kind, _) in _multi)
                    if (kind == SelKind.Stream || kind == SelKind.Hazard
                        || kind == SelKind.Burner || kind == SelKind.Zeus)
                        any = true;
                if (any)
                {
                    _pending = PendingAction.DeleteSelected;
                    RunPendingAction();
                    e.Use();
                    Repaint();
                }
                return;
            }

            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand)
                return;
            if (e.commandName != "Delete" && e.commandName != "SoftDelete")
                return;

            bool canDelete = false;
            foreach (var (kind, _) in _multi)
                if (kind == SelKind.Stream || kind == SelKind.Hazard
                    || kind == SelKind.Burner || kind == SelKind.Zeus)
                    canDelete = true;
            if (!canDelete)
                return;

            if (e.type == EventType.ValidateCommand)
            {
                e.Use(); // tell Unity we'll handle it
                return;
            }

            // ExecuteCommand: defer the array mutation via _pending (same as the buttons).
            _pending = PendingAction.DeleteSelected;
            RunPendingAction();
            e.Use();
            Repaint();
        }

        // ── Clipboard: Ctrl+C / Ctrl+V / Ctrl+D on streams and hazards ─────────

        // JSON snapshot of the copied element — cheap deep copy that survives
        // selection changes and array mutations.
        private enum ClipKind { None, Stream, Hazard, Burner, Zeus }
        private ClipKind _clipKind;
        private string _clipJson;

        private void HandleClipboardCommands()
        {
            Event e = Event.current;
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand)
                return;

            bool isCopy = e.commandName == "Copy";
            bool isPaste = e.commandName == "Paste";
            bool isDuplicate = e.commandName == "Duplicate";
            if (!isCopy && !isPaste && !isDuplicate)
                return;
            if (_level == null)
                return;

            bool hasSelection = _selKind == SelKind.Stream || _selKind == SelKind.Hazard
                || _selKind == SelKind.Burner || _selKind == SelKind.Zeus;
            if ((isCopy || isDuplicate) && !hasSelection)
                return;
            if (isPaste && _clipKind == ClipKind.None)
                return;

            if (e.type == EventType.ValidateCommand)
            {
                e.Use(); // claim the command so Unity doesn't copy the asset itself
                return;
            }

            if (isCopy)
            {
                CopySelected();
            }
            else if (isPaste)
            {
                PasteClipboard();
            }
            else // duplicate = copy + paste in one step
            {
                CopySelected();
                PasteClipboard();
            }
            e.Use();
            Repaint();
        }

        [System.Serializable]
        private class ClipData
        {
            public List<StreamDef> Streams = new List<StreamDef>();
            public List<HazardDef> Hazards = new List<HazardDef>();
            public List<BurnerDef> Burners = new List<BurnerDef>();
            public List<ZeusDef> Zeuses = new List<ZeusDef>();
        }

        private void CopySelected()
        {
            var clip = new ClipData();
            foreach (var (kind, index) in _multi)
            {
                if (kind == SelKind.Stream)
                    clip.Streams.Add(_level.Streams[index]);
                else if (kind == SelKind.Hazard)
                    clip.Hazards.Add(_level.Hazards[index]);
                else if (kind == SelKind.Burner)
                    clip.Burners.Add(_level.Burners[index]);
                else if (kind == SelKind.Zeus)
                    clip.Zeuses.Add(_level.Zeuses[index]);
            }
            if (clip.Streams.Count == 0 && clip.Hazards.Count == 0
                && clip.Burners.Count == 0 && clip.Zeuses.Count == 0)
                return;
            _clipKind = ClipKind.Stream; // any non-None marks the clipboard as filled
            _clipJson = JsonUtility.ToJson(clip);
        }

        private void PasteClipboard()
        {
            // Offset each paste so copies don't stack exactly on the original;
            // re-snapshotting the pasted set makes repeated Ctrl+V step a ladder.
            var offset = new Vector2(1.5f, -1.5f);
            var clip = JsonUtility.FromJson<ClipData>(_clipJson);
            if (clip == null)
                return;

            Undo.RecordObject(_level, "Вставить элементы");
            _multi.Clear();
            _selKind = SelKind.None;
            _selIndex = -1;

            var streams = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            foreach (var def in clip.Streams)
            {
                def.Position += offset;
                streams.Add(def);
                _multi.Add((SelKind.Stream, streams.Count - 1));
                _selKind = SelKind.Stream;
                _selIndex = streams.Count - 1;
            }
            _level.Streams = streams.ToArray();

            var hazards = new List<HazardDef>(_level.Hazards ?? new HazardDef[0]);
            foreach (var def in clip.Hazards)
            {
                def.Position += offset;
                hazards.Add(def);
                _multi.Add((SelKind.Hazard, hazards.Count - 1));
                _selKind = SelKind.Hazard;
                _selIndex = hazards.Count - 1;
            }
            _level.Hazards = hazards.ToArray();

            var burners = new List<BurnerDef>(_level.Burners ?? new BurnerDef[0]);
            foreach (var def in clip.Burners)
            {
                def.Position += offset;
                burners.Add(def);
                _multi.Add((SelKind.Burner, burners.Count - 1));
                _selKind = SelKind.Burner;
                _selIndex = burners.Count - 1;
            }
            _level.Burners = burners.ToArray();

            var zeuses = new List<ZeusDef>(_level.Zeuses ?? new ZeusDef[0]);
            foreach (var def in clip.Zeuses)
            {
                def.Position += offset;
                zeuses.Add(def);
                _multi.Add((SelKind.Zeus, zeuses.Count - 1));
                _selKind = SelKind.Zeus;
                _selIndex = zeuses.Count - 1;
            }
            _level.Zeuses = zeuses.ToArray();

            _clipJson = JsonUtility.ToJson(clip); // positions already offset for the next paste
            Commit();
        }

        // ── Toolbar (manual GUI, no layout groups) ─────────────────────────────

        private void DrawToolbar(Rect rect)
        {
            // Row 1 — asset ops: pick / new / copy / build / import.
            Rect row1 = new Rect(rect.x, rect.y, rect.width, RowHeight);
            GUI.Box(row1, GUIContent.none, EditorStyles.toolbar);
            float x = row1.x + 2f;

            EditorGUI.BeginChangeCheck();
            var picked = (LevelData)EditorGUI.ObjectField(
                new Rect(x, row1.y + 1f, 200f, 18f), _level, typeof(LevelData), false);
            if (EditorGUI.EndChangeCheck())
            {
                _level = picked;
                SetSingleSelection(SelKind.None, -1);
                RebuildSerialized();
            }
            x += 204f;

            if (GUI.Button(new Rect(x, row1.y, 110f, RowHeight), "Новый уровень", EditorStyles.toolbarButton)) NewLevel();
            x += 112f;
            using (new EditorGUI.DisabledScope(_level == null))
            {
                if (GUI.Button(new Rect(x, row1.y, 110f, RowHeight), "Копировать", EditorStyles.toolbarButton)) CopyLevel();
                x += 112f;
                bool dirty = _level != null && EditorUtility.IsDirty(_level);
                if (GUI.Button(new Rect(x, row1.y, 110f, RowHeight),
                    dirty ? "Сохранить *" : "Сохранить", EditorStyles.toolbarButton))
                    AssetDatabase.SaveAssets();
            }

            // Row 2 — element ops: add stream / add circle / add hazard.
            Rect row2 = new Rect(rect.x, rect.y + RowHeight, rect.width, RowHeight);
            GUI.Box(row2, GUIContent.none, EditorStyles.toolbar);
            x = row2.x + 2f;
            using (new EditorGUI.DisabledScope(_level == null))
            {
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Поток", EditorStyles.toolbarButton)) AddStream();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 100f, RowHeight), "+ Форма ▾", EditorStyles.toolbarDropDown))
                    ShowShapeMenu();
                x += 102f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Круг", EditorStyles.toolbarButton)) AddCircle();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Развилка", EditorStyles.toolbarButton)) AddFork();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Препятствие", EditorStyles.toolbarButton)) AddHazard();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Горелка", EditorStyles.toolbarButton)) AddBurner();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Зевс", EditorStyles.toolbarButton)) AddZeus();
            }
        }

        // ── Canvas ───────────────────────────────────────────────────────────

        private void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.14f, 0.18f));
            GUI.BeginClip(rect);

            DrawGrid(rect);
            DrawKillLine(rect);
            DrawBounds(rect);
            DrawStreams();
            DrawHazards();
            DrawBurners();
            DrawZeuses();
            DrawStartGoal();
            DrawRotateHandle();

            GUI.EndClip();

            HandleCanvasInput(rect);
        }

        private void DrawGrid(Rect rect)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.06f);
            Vector2 min = ScreenToWorld(new Vector2(0f, rect.height));
            Vector2 max = ScreenToWorld(new Vector2(rect.width, 0f));

            // Adaptive grid step (power of 2) so line count stays bounded at any zoom.
            int step = 1;
            while ((max.x - min.x) / step > 40f) step *= 2;
            int x0 = Mathf.FloorToInt(min.x / step) * step;
            int y0 = Mathf.FloorToInt(min.y / step) * step;
            for (int x = x0; x <= max.x; x += step)
                Handles.DrawLine(WorldToScreen(new Vector2(x, min.y)), WorldToScreen(new Vector2(x, max.y)));
            for (int y = y0; y <= max.y; y += step)
                Handles.DrawLine(WorldToScreen(new Vector2(min.x, y)), WorldToScreen(new Vector2(max.x, y)));
            // Origin axes.
            Handles.color = new Color(1f, 1f, 1f, 0.18f);
            Vector2 ox0 = WorldToScreen(new Vector2(min.x, 0f));
            Vector2 ox1 = WorldToScreen(new Vector2(max.x, 0f));
            Handles.DrawLine(ox0, ox1);
            Vector2 oy0 = WorldToScreen(new Vector2(0f, min.y));
            Vector2 oy1 = WorldToScreen(new Vector2(0f, max.y));
            Handles.DrawLine(oy0, oy1);
        }

        private void DrawKillLine(Rect rect)
        {
            bool selected = _selKind == SelKind.KillLine;
            Vector2 min = ScreenToWorld(new Vector2(0f, rect.height));
            Vector2 max = ScreenToWorld(new Vector2(rect.width, 0f));
            Vector2 a = WorldToScreen(new Vector2(min.x, _level.KillY));
            Vector2 b = WorldToScreen(new Vector2(max.x, _level.KillY));
            if (selected)
            {
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(6f, a, b);
            }
            Handles.color = new Color(0.2f, 0.5f, 1f, 0.7f);
            Handles.DrawAAPolyLine(3f, a, b);
            GUI.Label(new Rect(a.x + 4f, a.y, 200f, 16f),
                selected ? "смерть (тащи вверх/вниз)" : "смерть");
        }

        // Camera framing in the editor is anchored on the Start point (where the
        // player spawns and, for UpOnly/SingleScreen, where the camera locks).
        // Matches LetterboxCamera's 16:9 aspect: halfW = CameraSize * 16/9.
        private const float CameraAspect = 16f / 9f;

        /// <summary>Draw the deadly side/top walls for the current mode, sized by CameraSize.</summary>
        private void DrawBounds(Rect rect)
        {
            if (_level.Mode == LevelMode.Free)
                return; // only the bottom kill line is deadly

            float halfH = _level.CameraSize;
            float halfW = halfH * CameraAspect;
            Vector2 c = _level.Start;

            Vector2 visMin = ScreenToWorld(new Vector2(0f, rect.height));
            Vector2 visMax = ScreenToWorld(new Vector2(rect.width, 0f));

            // SingleScreen edges kill; UpOnly edges are solid physical walls (you bump,
            // you don't die) — show them differently so it reads right.
            bool lethal = _level.Mode == LevelMode.SingleScreen;
            var wall = lethal ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.45f, 0.7f, 1f, 0.9f);
            Handles.color = wall;

            // Left / right walls (both modes). Extend to the visible range vertically
            // in UpOnly (infinite), or just the screen box in SingleScreen.
            float top = _level.Mode == LevelMode.SingleScreen ? c.y + halfH : visMax.y;
            float bottom = _level.Mode == LevelMode.SingleScreen ? c.y - halfH : visMin.y;

            Handles.DrawAAPolyLine(3f,
                WorldToScreen(new Vector2(c.x - halfW, bottom)),
                WorldToScreen(new Vector2(c.x - halfW, top)));
            Handles.DrawAAPolyLine(3f,
                WorldToScreen(new Vector2(c.x + halfW, bottom)),
                WorldToScreen(new Vector2(c.x + halfW, top)));

            if (_level.Mode == LevelMode.SingleScreen)
            {
                // Top wall too (bottom edge is the kill line, drawn separately).
                Handles.DrawAAPolyLine(3f,
                    WorldToScreen(new Vector2(c.x - halfW, top)),
                    WorldToScreen(new Vector2(c.x + halfW, top)));
            }

            GUI.Label(new Rect(WorldToScreen(new Vector2(c.x - halfW, top)).x + 4f,
                WorldToScreen(new Vector2(c.x - halfW, top)).y, 140f, 16f),
                lethal ? "стена смерти" : "стена (физическая)");
        }

        private void DrawStreams()
        {
            if (_level.Streams == null) return;
            for (int i = 0; i < _level.Streams.Length; i++)
            {
                StreamDef def = _level.Streams[i];
                List<Vector2> pts = StreamShapeBuilder.Build(def, out bool loop);
                if (pts.Count < 2) continue;

                bool selected = IsSelected(SelKind.Stream, i);
                Color c = def.VisualColor;
                c.a = selected ? 1f : 0.75f;
                Handles.color = c;

                // World-space path points (placement applied).
                int wpCount = pts.Count + (loop ? 1 : 0);
                var world = new Vector2[wpCount];
                for (int p = 0; p < pts.Count; p++)
                    world[p] = def.Position + Rotate(pts[p], def.Rotation);
                if (loop) world[pts.Count] = world[0];

                // Width band: edge lines at ±Width/2 along the path normal — shows the
                // capture zone. Faint fill colour, drawn under the centre line.
                DrawWidthBand(world, loop, def.Width, c);

                // Rounded centre line: subdivide the raw points with the same Catmull-Rom
                // the runtime uses, so corners read as smooth curves. Only the line is
                // subdivided — the band/arrows stay on the raw points, so this adds light
                // AA line calls (no disc-per-point fill, which is what hurt the canvas).
                List<Vector2> smooth = StreamShapeBuilder.Smooth(pts, loop, 3);
                int sCount = smooth.Count + (loop ? 1 : 0);
                var sWorld = new Vector2[sCount];
                var sScreen = new Vector3[sCount];
                for (int p = 0; p < smooth.Count; p++)
                {
                    sWorld[p] = def.Position + Rotate(smooth[p], def.Rotation);
                    sScreen[p] = WorldToScreen(sWorld[p]);
                }
                if (loop) { sWorld[smooth.Count] = sWorld[0]; sScreen[smooth.Count] = sScreen[0]; }

                if (selected)
                {
                    // White underlay so the selection reads instantly.
                    Handles.color = Color.white;
                    Handles.DrawAAPolyLine(7f, sScreen);
                }

                // Centre line segment-by-segment, coloured by flow direction
                // (up = green, down = red, sideways = yellow).
                float lineWidth = selected ? 4f : 2f;
                int lineSegs = loop ? smooth.Count : smooth.Count - 1;
                for (int p = 0; p < lineSegs; p++)
                {
                    Vector2 dir = (sWorld[p + 1] - sWorld[p]).normalized;
                    Handles.color = DirectionColor(dir);
                    Handles.DrawAAPolyLine(lineWidth, sScreen[p], sScreen[p + 1]);
                }

                // Direction arrows along the path; arrow length scales with the local
                // speed, so a ramp reads as darts growing toward the end.
                DrawFlowArrows(world, loop, def, c);

                // Selectable handle at the placement origin.
                Vector2 originScreen = WorldToScreen(def.Position);
                DrawHandleDot(originScreen, selected, c);
                GUI.Label(new Rect(originScreen.x + 8f, originScreen.y - 8f, 140f, 16f),
                    $"П{i}  скор {def.Speed:0.#}  шир {def.Width:0.#}");

                // Editable waypoints — only for a single-selected custom path.
                if (selected && _multi.Count == 1 && def.UsesCustomPoints)
                {
                    for (int p = 0; p < def.CustomPoints.Length; p++)
                    {
                        Vector2 ws = WorldToScreen(def.Position + Rotate(def.CustomPoints[p], def.Rotation));
                        bool pSel = _dragPointIndex == p;
                        // Dark outline under the dot so it stays visible over the band.
                        Handles.color = new Color(0f, 0f, 0f, 0.6f);
                        Handles.DrawSolidDisc(ws, Vector3.forward, (pSel ? 11f : 9f) + 2f);
                        Handles.color = pSel ? Color.white : Color.yellow;
                        Handles.DrawSolidDisc(ws, Vector3.forward, pSel ? 11f : 9f);
                    }
                }
            }
        }

        /// <summary>
        /// Draw the stream's width as a filled "sausage": a quad per segment plus a
        /// disc at each waypoint to round the joints, so the band reads as one solid
        /// capsule instead of crossing edge lines at sharp corners.
        /// </summary>
        private void DrawWidthBand(Vector2[] world, bool loop, float width, Color color)
        {
            if (width <= 0f || world.Length < 2)
                return;

            float half = width * 0.5f;
            var fill = new Color(color.r, color.g, color.b, color.a * 0.18f);
            Handles.color = fill;

            int segs = loop ? world.Length : world.Length - 1;
            for (int p = 0; p < segs; p++)
            {
                Vector2 a = world[p];
                Vector2 b = world[(p + 1) % world.Length];
                Vector2 dir = (b - a);
                if (dir.sqrMagnitude < 1e-6f) continue;
                Vector2 nrm = new Vector2(-dir.y, dir.x).normalized * half;

                Handles.DrawAAConvexPolygon(
                    WorldToScreen(a + nrm), WorldToScreen(b + nrm),
                    WorldToScreen(b - nrm), WorldToScreen(a - nrm));
            }

            // Round caps / joints: disc of radius half at every waypoint.
            int caps = loop ? world.Length : world.Length;
            for (int p = 0; p < caps; p++)
                Handles.DrawSolidDisc(WorldToScreen(world[p]), Vector3.forward, half * _pixelsPerUnit);
        }

        /// <summary>Direction arrows spaced along the path; arrow size scales with the
        /// local flow speed (ramped streams show growing darts toward the end).</summary>
        private void DrawFlowArrows(Vector2[] world, bool loop, StreamDef def, Color color)
        {
            int segs = loop ? world.Length : world.Length - 1;
            if (segs < 1)
                return;

            int stride = Mathf.Max(1, segs / 4);
            for (int p = 0; p < segs; p += stride)
            {
                Vector2 a = world[p];
                Vector2 b = world[(p + 1) % world.Length];
                Vector2 dir = (b - a).normalized;

                float t = segs > 1 ? (float)p / (segs - 1) : 0f;
                float speed = def.SpeedEnd > 0f ? Mathf.Lerp(def.Speed, def.SpeedEnd, t) : def.Speed;
                float worldLen = 0.4f + Mathf.Clamp(speed, 0f, 12f) * 0.12f;

                Vector2 baseWorld = (a + b) * 0.5f;
                DrawArrow(baseWorld + dir * worldLen, dir, DirectionColor(dir));
            }
        }

        /// <summary>Arrow colour by vertical direction: up = green, down = red, sideways = yellow.</summary>
        private static Color DirectionColor(Vector2 dir)
        {
            var up = new Color(0.35f, 1f, 0.45f);
            var down = new Color(1f, 0.3f, 0.3f);
            var side = new Color(1f, 0.9f, 0.35f);
            return dir.y >= 0f
                ? Color.Lerp(side, up, dir.y)
                : Color.Lerp(side, down, -dir.y);
        }

        private void DrawHazards()
        {
            if (_level.Hazards == null) return;
            for (int i = 0; i < _level.Hazards.Length; i++)
            {
                HazardDef def = _level.Hazards[i];
                bool selected = IsSelected(SelKind.Hazard, i);
                Color c = new Color(1f, 0.35f, 0.3f, selected ? 1f : 0.8f);
                Handles.color = c;

                Vector2 s = WorldToScreen(def.Position);
                float r = Mathf.Max(4f, def.Size * 0.5f * _pixelsPerUnit);
                if (selected)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(s, Vector3.forward, r + 3f);
                    Handles.DrawWireDisc(s, Vector3.forward, r + 4.5f);
                    Handles.color = c;
                }
                Handles.DrawWireDisc(s, Vector3.forward, r);

                // Patrol range.
                Handles.color = new Color(c.r, c.g, c.b, 0.4f);
                Vector2 end = WorldToScreen(def.Position + def.PatrolTravel);
                Handles.DrawDottedLine(s, end, 3f);

                DrawHandleDot(s, selected, c);
                GUI.Label(new Rect(s.x + 8f, s.y - 8f, 60f, 16f), "Оп" + i);
            }
        }

        private void DrawBurners()
        {
            if (_level.Burners == null) return;
            for (int i = 0; i < _level.Burners.Length; i++)
            {
                BurnerDef def = _level.Burners[i];
                bool selected = IsSelected(SelKind.Burner, i);
                Vector2 s = WorldToScreen(def.Position);
                var anchorColor = new Color(1f, 0.55f, 0.15f, selected ? 1f : 0.85f);

                // Each cone: two edge rays + a short arc joining their far ends.
                if (def.Cones != null)
                {
                    foreach (ConeDef cone in def.Cones)
                    {
                        Handles.color = new Color(1f, 0.45f, 0.12f, selected ? 0.9f : 0.55f);
                        float a0 = (cone.Angle - cone.HalfAngle) * Mathf.Deg2Rad;
                        float a1 = (cone.Angle + cone.HalfAngle) * Mathf.Deg2Rad;
                        Vector2 e0 = ConeEnd(def.Position, a0, cone.Length);
                        Vector2 e1 = ConeEnd(def.Position, a1, cone.Length);
                        Handles.DrawAAPolyLine(2f, s, e0);
                        Handles.DrawAAPolyLine(2f, s, e1);
                        // Arc across the mouth (a few segments).
                        const int seg = 8;
                        Vector2 prev = e0;
                        for (int k = 1; k <= seg; k++)
                        {
                            float a = Mathf.Lerp(a0, a1, k / (float)seg);
                            Vector2 p = ConeEnd(def.Position, a, cone.Length);
                            Handles.DrawAAPolyLine(2f, prev, p);
                            prev = p;
                        }

                        // Centerline + draggable tip handle (only when the burner is
                        // single-selected, so the canvas isn't cluttered otherwise).
                        if (selected && _multi.Count == 1)
                        {
                            Vector2 tip = WorldToScreen(ConeTipWorld(def.Position, cone));
                            Handles.color = new Color(1f, 0.75f, 0.3f, 0.6f);
                            Handles.DrawAAPolyLine(1.5f, s, tip);
                            DrawHandleDot(tip, true, new Color(1f, 0.85f, 0.4f, 1f));
                        }
                    }
                }

                if (selected)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(s, Vector3.forward, 7f);
                }
                DrawHandleDot(s, selected, anchorColor);
                GUI.Label(new Rect(s.x + 8f, s.y - 8f, 80f, 16f), "Горелка" + i);
            }
        }

        private void DrawZeuses()
        {
            if (_level.Zeuses == null) return;
            for (int i = 0; i < _level.Zeuses.Length; i++)
            {
                ZeusDef def = _level.Zeuses[i];
                bool selected = IsSelected(SelKind.Zeus, i);
                Vector2 s = WorldToScreen(def.Position);
                var color = new Color(1f, 0.92f, 0.2f, selected ? 1f : 0.85f);

                // A dotted bolt line from the anchor to each target area, and the
                // area's ellipse footprint.
                if (def.Areas != null)
                {
                    foreach (ZeusAreaDef area in def.Areas)
                    {
                        if (area == null) continue;
                        Vector2 c = WorldToScreen(def.Position + area.Offset);
                        Handles.color = new Color(1f, 0.95f, 0.35f, selected ? 0.9f : 0.5f);
                        Handles.DrawDottedLine(s, c, 4f);
                        DrawEllipse(c, area.RadiusX * _pixelsPerUnit, area.RadiusY * _pixelsPerUnit,
                            new Color(1f, 0.92f, 0.2f, selected ? 0.85f : 0.45f));

                        // On-canvas edit handles for the selected Zeus: centre (move),
                        // right edge (Radius X), top edge (Radius Y).
                        if (selected)
                        {
                            Vector2 hx = WorldToScreen(def.Position + area.Offset + new Vector2(area.RadiusX, 0f));
                            Vector2 hy = WorldToScreen(def.Position + area.Offset + new Vector2(0f, area.RadiusY));
                            DrawHandleDot(c, false, new Color(1f, 0.85f, 0.2f, 1f));
                            DrawHandleDot(hx, false, new Color(0.4f, 0.85f, 1f, 1f));
                            DrawHandleDot(hy, false, new Color(0.6f, 1f, 0.5f, 1f));
                        }
                    }
                }

                if (selected)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(s, Vector3.forward, 7f);
                }
                // Anchor: a small ring plus the handle dot.
                Handles.color = color;
                Handles.DrawWireDisc(s, Vector3.forward, 5f);
                DrawHandleDot(s, selected, color);
                GUI.Label(new Rect(s.x + 8f, s.y - 8f, 90f, 16f), "Зевс" + i);
            }
        }

        /// <summary>Outline an axis-aligned ellipse in screen space.</summary>
        private static void DrawEllipse(Vector2 center, float rx, float ry, Color color)
        {
            const int Segments = 32;
            Handles.color = color;
            Vector3 prev = center + new Vector2(rx, 0f);
            for (int i = 1; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                Vector3 p = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                Handles.DrawLine(prev, p);
                prev = p;
            }
        }

        private Vector2 ConeEnd(Vector2 originWorld, float angleRad, float length)
        {
            var tip = originWorld + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * length;
            return WorldToScreen(tip);
        }

        private void DrawStartGoal()
        {
            // Player-size silhouette under the start marker — scale reference.
            DrawPlayerSilhouette(_level.Start);

            // Start. Red when below the kill line — the player would die on spawn.
            bool startSel = IsSelected(SelKind.Start, -1);
            bool startDead = _level.Start.y < _level.KillY;
            Color sc = startDead ? new Color(1f, 0.25f, 0.25f, 1f) : new Color(0.4f, 1f, 0.5f, 1f);
            Vector2 ss = WorldToScreen(_level.Start);
            if (startSel)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(ss, Vector3.forward, 11f);
                Handles.DrawWireDisc(ss, Vector3.forward, 12.5f);
            }
            Handles.color = sc;
            Handles.DrawWireDisc(ss, Vector3.forward, 8f);
            DrawHandleDot(ss, startSel, sc);
            GUI.Label(new Rect(ss.x + 8f, ss.y - 8f, 200f, 16f),
                startDead ? "старт ПОД ЛИНИЕЙ СМЕРТИ!" : "старт");

            // Goal + radius.
            bool goalSel = IsSelected(SelKind.Goal, -1);
            Color gc = new Color(1f, 0.9f, 0.3f, 1f);
            Vector2 gs = WorldToScreen(_level.Goal);
            if (goalSel)
            {
                Handles.color = Color.white;
                float gr = _level.GoalRadius * _pixelsPerUnit;
                Handles.DrawWireDisc(gs, Vector3.forward, gr + 3f);
                Handles.DrawWireDisc(gs, Vector3.forward, gr + 4.5f);
            }
            Handles.color = gc;
            Handles.DrawWireDisc(gs, Vector3.forward, _level.GoalRadius * _pixelsPerUnit);
            DrawHandleDot(gs, goalSel, gc);
            GUI.Label(new Rect(gs.x + 8f, gs.y - 8f, 60f, 16f), "цель");
        }

        /// <summary>
        /// Icarus silhouette at real in-game size (wings open, span ~2.2 units):
        /// body + head + feather fans. Pure scale reference, not selectable on its
        /// own — it follows the Start point.
        /// </summary>
        private void DrawPlayerSilhouette(Vector2 worldPos)
        {
            var cream = new Color(0.96f, 0.91f, 0.82f, 0.55f);
            const float k = 0.8f; // in-game player visual is scaled to 0.8

            // Proportions match WingsVisual: slim body, small head, legs.
            Handles.color = cream;
            var body = new Vector3[12];
            for (int i = 0; i < body.Length; i++)
            {
                float a = i / (float)body.Length * 2f * Mathf.PI;
                body[i] = WorldToScreen(worldPos + new Vector2(Mathf.Cos(a) * 0.10f, Mathf.Sin(a) * 0.18f) * k);
            }
            Handles.DrawAAConvexPolygon(body);

            // Head.
            Handles.DrawSolidDisc(WorldToScreen(worldPos + new Vector2(0f, 0.245f) * k),
                Vector3.forward, 0.09f * k * _pixelsPerUnit);

            // Legs.
            for (int side = -1; side <= 1; side += 2)
                Handles.DrawAAPolyLine(2f,
                    WorldToScreen(worldPos + new Vector2(side * 0.05f, -0.16f) * k),
                    WorldToScreen(worldPos + new Vector2(side * 0.05f, -0.34f) * k));

            // Open feather fans: 4 strokes per wing.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int d = 0; d < 4; d++)
                {
                    float angle = (10f + d * 15f) * Mathf.Deg2Rad;
                    float len = (0.45f + d * 0.18f) * k;
                    var dir = new Vector2(Mathf.Cos(angle) * side, Mathf.Sin(angle));
                    Vector2 shoulder = worldPos + new Vector2(side * 0.04f, 0.06f) * k;
                    Handles.DrawAAPolyLine(2.5f,
                        WorldToScreen(shoulder),
                        WorldToScreen(shoulder + dir * len));
                }
            }
        }

        private void DrawArrow(Vector2 worldPos, Vector2 worldDir, Color color)
        {
            Handles.color = color;
            Vector2 tip = WorldToScreen(worldPos);
            Vector2 dir = new Vector2(worldDir.x, -worldDir.y).normalized; // screen Y is flipped
            Vector2 left = tip - dir * 10f + new Vector2(-dir.y, dir.x) * 5f;
            Vector2 right = tip - dir * 10f - new Vector2(-dir.y, dir.x) * 5f;
            Handles.DrawAAPolyLine(2f, left, tip, right);
        }

        private static void DrawHandleDot(Vector2 screen, bool selected, Color color)
        {
            float r = selected ? 6f : 4f;
            Handles.color = selected ? Color.white : color;
            Handles.DrawSolidDisc(screen, Vector3.forward, r);
        }

        // ── Canvas input: pan, zoom, select, drag ─────────────────────────────

        private bool _dragging;
        private bool _draggedThisPress;
        private int _dragPointIndex = -1;
        private int _dragConeIndex = -1; // tip handle of a cone on the single-selected burner

        private enum AreaHandle { None, Center, RadiusX, RadiusY }
        private int _dragAreaIndex = -1;            // area handle on the single-selected Zeus
        private AreaHandle _dragAreaHandle = AreaHandle.None;

        // Mouse rotation of the selection (single element around its origin, or a group
        // around its centroid). Snapshot start-pose so the drag is absolute, no drift.
        private bool _rotating;
        private float _rotStartAngle;
        private Vector2 _rotPivot;
        private readonly List<(SelKind kind, int index, Vector2 pos, float rot)> _rotSnapshot = new List<(SelKind, int, Vector2, float)>();
        private const float RotHandlePx = 64f;

        // Set on plain MouseDown over an already-selected element: if the press
        // ends without a drag, the selection collapses to just that element
        // (Explorer-style), otherwise the whole group was dragged.
        private (SelKind kind, int index)? _collapseOnMouseUp;

        private void HandleCanvasInput(Rect rect)
        {
            Event e = Event.current;
            // Delete is handled in HandleDeleteCommand (command event), not here.
            if (!rect.Contains(e.mousePosition) && e.type != EventType.MouseDrag)
                return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        Vector2 before = ScreenToWorld(e.mousePosition - rect.position);
                        _pixelsPerUnit = Mathf.Clamp(_pixelsPerUnit * (1f - e.delta.y * 0.03f), 4f, 200f);
                        Vector2 after = ScreenToWorld(e.mousePosition - rect.position);
                        _panWorld += before - after; // zoom toward cursor
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseDown:
                    // Right-click on a waypoint of the single-selected drawn path = delete the point.
                    if (e.button == 1 && rect.Contains(e.mousePosition)
                        && TryPickPoint(e.mousePosition - rect.position, out int killPoint))
                    {
                        DeleteCustomPoint(killPoint);
                        Repaint();
                        e.Use();
                        break;
                    }
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        Vector2 local = e.mousePosition - rect.position;
                        bool additive = e.shift || e.control;
                        if (e.alt && _multi.Count == 1 && _selKind == SelKind.Stream &&
                            _level.Streams[_selIndex].UsesCustomPoints)
                        {
                            AddPointToSelected(local);
                        }
                        else if (!additive && TryStartRotate(local))
                        {
                            // rotating the selection via the handle (state set inside)
                        }
                        else if (!additive && TryPickConeTip(local, out _dragConeIndex))
                        {
                            _dragging = true; // dragging a cone tip = rotate + lengthen
                        }
                        else if (!additive && TryPickAreaHandle(local, out _dragAreaIndex, out _dragAreaHandle))
                        {
                            _dragging = true; // dragging a Zeus area handle = move / resize
                        }
                        else if (!additive && TryPickPoint(local, out _dragPointIndex))
                        {
                            _dragging = true; // dragging a waypoint
                        }
                        else
                        {
                            _dragPointIndex = -1;
                            SelectAt(local, additive);
                            _dragging = !additive && _selKind != SelKind.None;
                            _draggedThisPress = false;
                        }
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 && _rotating)
                    {
                        _draggedThisPress = true;
                        DragRotate(e.mousePosition - rect.position);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 0 && _dragging && _dragConeIndex >= 0)
                    {
                        _draggedThisPress = true;
                        DragConeTip(e.mousePosition - rect.position);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 0 && _dragging && _dragAreaHandle != AreaHandle.None)
                    {
                        _draggedThisPress = true;
                        DragAreaHandle(e.mousePosition - rect.position);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 0 && _dragging && _dragPointIndex >= 0)
                    {
                        _draggedThisPress = true;
                        DragPoint(e.delta);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 0 && _dragging)
                    {
                        _draggedThisPress = true;
                        DragSelected(e.delta);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 2 || (e.button == 0 && !_dragging))
                    {
                        _panWorld -= new Vector2(e.delta.x, -e.delta.y) / _pixelsPerUnit;
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    // Click (no drag) on a selected element of a group: collapse to it.
                    if (_collapseOnMouseUp.HasValue && !_draggedThisPress)
                    {
                        var c = _collapseOnMouseUp.Value;
                        SetSingleSelection(c.kind, c.index);
                        Repaint();
                    }
                    _collapseOnMouseUp = null;
                    _dragging = false;
                    _rotating = false;
                    _rotSnapshot.Clear();
                    _dragPointIndex = -1;
                    _dragConeIndex = -1;
                    _dragAreaIndex = -1;
                    _dragAreaHandle = AreaHandle.None;
                    break;
            }
        }

        private void SelectAt(Vector2 screenInCanvas, bool additive)
        {
            const float pickRadius = 12f;
            float best = pickRadius;
            SelKind hitKind = SelKind.None;
            int hitIndex = -1;

            void Consider(Vector2 worldPos, SelKind kind, int index)
            {
                float d = Vector2.Distance(WorldToScreen(worldPos), screenInCanvas);
                if (d < best)
                {
                    best = d;
                    hitKind = kind;
                    hitIndex = index;
                }
            }

            Consider(_level.Start, SelKind.Start, -1);
            Consider(_level.Goal, SelKind.Goal, -1);
            if (_level.Hazards != null)
                for (int i = 0; i < _level.Hazards.Length; i++)
                    Consider(_level.Hazards[i].Position, SelKind.Hazard, i);
            if (_level.Burners != null)
                for (int i = 0; i < _level.Burners.Length; i++)
                    Consider(_level.Burners[i].Position, SelKind.Burner, i);
            if (_level.Zeuses != null)
                for (int i = 0; i < _level.Zeuses.Length; i++)
                    Consider(_level.Zeuses[i].Position, SelKind.Zeus, i);

            // A point element (start/goal/hazard) right under the cursor wins. Otherwise
            // fall through to streams, which pick on the whole path (any point along the
            // band), not just the origin handle — much easier to hit. Tolerance grows
            // with the band width.
            if (hitKind == SelKind.None && _level.Streams != null)
            {
                float bestStream = float.MaxValue;
                for (int i = 0; i < _level.Streams.Length; i++)
                {
                    float tol = pickRadius + _level.Streams[i].Width * 0.5f * _pixelsPerUnit;
                    float d = DistanceToStream(_level.Streams[i], screenInCanvas);
                    if (d <= tol && d < bestStream)
                    {
                        bestStream = d;
                        hitKind = SelKind.Stream;
                        hitIndex = i;
                    }
                }
            }

            // Kill line: picked only when nothing else is under the cursor — it
            // spans the whole canvas and must not shadow real elements.
            if (hitKind == SelKind.None)
            {
                float lineY = WorldToScreen(new Vector2(0f, _level.KillY)).y;
                if (Mathf.Abs(screenInCanvas.y - lineY) < 10f)
                {
                    hitKind = SelKind.KillLine;
                    hitIndex = -1;
                }
            }

            if (additive)
            {
                // Shift/Ctrl+click: toggle the hit element, keep the rest. Click on
                // empty space changes nothing.
                if (hitKind != SelKind.None && hitKind != SelKind.KillLine)
                    ToggleSelection(hitKind, hitIndex);
                return;
            }

            // Plain click on an already-selected element keeps the group for now (so
            // the whole selection can be dragged); if the press ends without a drag,
            // MouseUp collapses the selection to just this element.
            if (hitKind != SelKind.None && IsSelected(hitKind, hitIndex))
            {
                _selKind = hitKind;
                _selIndex = hitIndex;
                _collapseOnMouseUp = (hitKind, hitIndex);
                return;
            }
            _collapseOnMouseUp = null;
            SetSingleSelection(hitKind, hitIndex);
        }

        /// <summary>Screen-space distance from a point to the nearest segment of a stream's path.</summary>
        private float DistanceToStream(StreamDef def, Vector2 screenPoint)
        {
            List<Vector2> pts = StreamShapeBuilder.Build(def, out bool loop);
            if (pts.Count < 2)
                return float.MaxValue;

            int segs = loop ? pts.Count : pts.Count - 1;
            float best = float.MaxValue;
            for (int p = 0; p < segs; p++)
            {
                Vector2 a = WorldToScreen(def.Position + Rotate(pts[p], def.Rotation));
                Vector2 b = WorldToScreen(def.Position + Rotate(pts[(p + 1) % pts.Count], def.Rotation));
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(screenPoint - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(screenPoint, a + ab * t));
            }
            return best;
        }

        private bool TryPickPoint(Vector2 screenInCanvas, out int pointIndex)
        {
            pointIndex = -1;
            if (_multi.Count != 1 || _selKind != SelKind.Stream) return false;
            StreamDef def = _level.Streams[_selIndex];
            if (!def.UsesCustomPoints) return false;

            float best = 16f; // generous hitbox — waypoints are hard to hit precisely
            for (int p = 0; p < def.CustomPoints.Length; p++)
            {
                Vector2 ws = WorldToScreen(def.Position + Rotate(def.CustomPoints[p], def.Rotation));
                float d = Vector2.Distance(ws, screenInCanvas);
                if (d < best) { best = d; pointIndex = p; }
            }
            return pointIndex >= 0;
        }

        private void DeleteCustomPoint(int pointIndex)
        {
            StreamDef def = _level.Streams[_selIndex];
            if (def.CustomPoints.Length <= 2)
                return; // a path needs at least two points
            Undo.RecordObject(_level, "Удалить точку пути");
            var list = new List<Vector2>(def.CustomPoints);
            list.RemoveAt(pointIndex);
            def.CustomPoints = list.ToArray();
            _dragPointIndex = -1;
            EditorUtility.SetDirty(_level);
        }

        private void AddPointToSelected(Vector2 screenInCanvas)
        {
            StreamDef def = _level.Streams[_selIndex];
            Undo.RecordObject(_level, "Добавить точку пути");
            Vector2 world = ScreenToWorld(screenInCanvas);
            Vector2 local = Rotate(world - def.Position, -def.Rotation);
            var list = new List<Vector2>(def.CustomPoints) { local };
            def.CustomPoints = list.ToArray();
            _dragPointIndex = list.Count - 1;
            EditorUtility.SetDirty(_level);
        }

        private void DragPoint(Vector2 screenDelta)
        {
            StreamDef def = _level.Streams[_selIndex];
            Vector2 worldDelta = new Vector2(screenDelta.x, -screenDelta.y) / _pixelsPerUnit;
            Undo.RecordObject(_level, "Двигать точку пути");
            def.CustomPoints[_dragPointIndex] += Rotate(worldDelta, -def.Rotation);
            EditorUtility.SetDirty(_level);
        }

        /// <summary>Hit-test the tip handle of each cone on the single-selected burner.</summary>
        private bool TryPickConeTip(Vector2 screenInCanvas, out int coneIndex)
        {
            coneIndex = -1;
            if (_multi.Count != 1 || _selKind != SelKind.Burner) return false;
            BurnerDef burner = _level.Burners[_selIndex];
            if (burner.Cones == null) return false;

            float best = 16f; // generous hitbox, same feel as waypoints
            for (int i = 0; i < burner.Cones.Length; i++)
            {
                ConeDef cone = burner.Cones[i];
                Vector2 tip = ConeTipWorld(burner.Position, cone);
                float d = Vector2.Distance(WorldToScreen(tip), screenInCanvas);
                if (d < best) { best = d; coneIndex = i; }
            }
            return coneIndex >= 0;
        }

        /// <summary>Drag a cone tip: distance sets Length, direction sets Angle.</summary>
        private void DragConeTip(Vector2 screenInCanvas)
        {
            BurnerDef burner = _level.Burners[_selIndex];
            ConeDef cone = burner.Cones[_dragConeIndex];
            Vector2 world = ScreenToWorld(screenInCanvas);
            Vector2 to = world - burner.Position;
            if (to.sqrMagnitude < 0.0001f)
                return;

            Undo.RecordObject(_level, "Настроить конус");
            cone.Angle = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            cone.Length = Mathf.Max(0.25f, to.magnitude);
            EditorUtility.SetDirty(_level);
            RebuildSerialized(); // keep the inspector's SerializedObject in sync with the live edit
        }

        private static Vector2 ConeTipWorld(Vector2 origin, ConeDef cone)
        {
            float rad = cone.Angle * Mathf.Deg2Rad;
            return origin + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * cone.Length;
        }

        private static Vector2 AreaCenterWorld(ZeusDef zeus, ZeusAreaDef area)
            => zeus.Position + area.Offset;

        /// <summary>Hit-test the move/resize handles of each area on the single-selected Zeus.</summary>
        private bool TryPickAreaHandle(Vector2 screenInCanvas, out int areaIndex, out AreaHandle handle)
        {
            areaIndex = -1;
            handle = AreaHandle.None;
            if (_multi.Count != 1 || _selKind != SelKind.Zeus) return false;
            ZeusDef zeus = _level.Zeuses[_selIndex];
            if (zeus.Areas == null) return false;

            float best = 14f; // generous hitbox, same feel as the cone tip
            AreaHandle hit = AreaHandle.None;
            int hitIdx = -1;

            void Consider(Vector2 world, AreaHandle h, int idx)
            {
                float d = Vector2.Distance(WorldToScreen(world), screenInCanvas);
                if (d < best) { best = d; hit = h; hitIdx = idx; }
            }

            for (int i = 0; i < zeus.Areas.Length; i++)
            {
                ZeusAreaDef area = zeus.Areas[i];
                Vector2 center = AreaCenterWorld(zeus, area);
                Consider(center, AreaHandle.Center, i);
                Consider(center + new Vector2(area.RadiusX, 0f), AreaHandle.RadiusX, i);
                Consider(center + new Vector2(0f, area.RadiusY), AreaHandle.RadiusY, i);
            }

            handle = hit;
            areaIndex = hitIdx;
            return handle != AreaHandle.None;
        }

        /// <summary>Drag a Zeus area handle: centre moves the area, edge handles resize it.</summary>
        private void DragAreaHandle(Vector2 screenInCanvas)
        {
            ZeusDef zeus = _level.Zeuses[_selIndex];
            ZeusAreaDef area = zeus.Areas[_dragAreaIndex];
            Vector2 world = ScreenToWorld(screenInCanvas);

            Undo.RecordObject(_level, "Настроить область");
            switch (_dragAreaHandle)
            {
                case AreaHandle.Center:
                    area.Offset = world - zeus.Position;
                    break;
                case AreaHandle.RadiusX:
                    area.RadiusX = Mathf.Max(0.1f, Mathf.Abs(world.x - AreaCenterWorld(zeus, area).x));
                    break;
                case AreaHandle.RadiusY:
                    area.RadiusY = Mathf.Max(0.1f, Mathf.Abs(world.y - AreaCenterWorld(zeus, area).y));
                    break;
            }
            EditorUtility.SetDirty(_level);
            RebuildSerialized(); // keep the inspector's SerializedObject in sync with the live edit
        }

        private void DragSelected(Vector2 screenDelta)
        {
            Vector2 worldDelta = new Vector2(screenDelta.x, -screenDelta.y) / _pixelsPerUnit;
            Undo.RecordObject(_level, "Двигать элементы");
            foreach (var (kind, index) in _multi)
            {
                switch (kind)
                {
                    case SelKind.Start: _level.Start += worldDelta; break;
                    case SelKind.Goal: _level.Goal += worldDelta; break;
                    case SelKind.Hazard: _level.Hazards[index].Position += worldDelta; break;
                    case SelKind.Burner: _level.Burners[index].Position += worldDelta; break;
                    case SelKind.Zeus: _level.Zeuses[index].Position += worldDelta; break;
                    case SelKind.Stream: _level.Streams[index].Position += worldDelta; break;
                    case SelKind.KillLine: _level.KillY += worldDelta.y; break;
                }
            }
            EditorUtility.SetDirty(_level);
        }

        // ── Mouse rotation ────────────────────────────────────────────────────

        /// <summary>Kinds that have a world position and can be rotated/orbited.</summary>
        private static bool IsPositional(SelKind k) =>
            k == SelKind.Stream || k == SelKind.Hazard || k == SelKind.Burner ||
            k == SelKind.Zeus || k == SelKind.Start || k == SelKind.Goal;

        private bool HasRotatablePivot()
        {
            foreach (var (kind, _) in _multi)
                if (IsPositional(kind)) return true;
            return false;
        }

        private Vector2 ElementPos(SelKind k, int i) => k switch
        {
            SelKind.Stream => _level.Streams[i].Position,
            SelKind.Hazard => _level.Hazards[i].Position,
            SelKind.Burner => _level.Burners[i].Position,
            SelKind.Zeus => _level.Zeuses[i].Position,
            SelKind.Start => _level.Start,
            SelKind.Goal => _level.Goal,
            _ => Vector2.zero,
        };

        private void SetElementPos(SelKind k, int i, Vector2 p)
        {
            switch (k)
            {
                case SelKind.Stream: _level.Streams[i].Position = p; break;
                case SelKind.Hazard: _level.Hazards[i].Position = p; break;
                case SelKind.Burner: _level.Burners[i].Position = p; break;
                case SelKind.Zeus: _level.Zeuses[i].Position = p; break;
                case SelKind.Start: _level.Start = p; break;
                case SelKind.Goal: _level.Goal = p; break;
            }
        }

        /// <summary>Centroid of the selected positional elements — the rotation pivot.</summary>
        private Vector2 SelectionPivot()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var (kind, index) in _multi)
                if (IsPositional(kind)) { sum += ElementPos(kind, index); n++; }
            return n > 0 ? sum / n : Vector2.zero;
        }

        private static float AngleDeg(Vector2 pivot, Vector2 p) =>
            Mathf.Atan2(p.y - pivot.y, p.x - pivot.x) * Mathf.Rad2Deg;

        private bool TryStartRotate(Vector2 local)
        {
            if (!HasRotatablePivot()) return false;
            Vector2 pivot = SelectionPivot();
            Vector2 knob = WorldToScreen(pivot) + new Vector2(0f, -RotHandlePx);
            if (Vector2.Distance(local, knob) > 11f) return false;

            _rotPivot = pivot;
            _rotStartAngle = AngleDeg(pivot, ScreenToWorld(local));
            _rotSnapshot.Clear();
            foreach (var (kind, index) in _multi)
            {
                if (!IsPositional(kind)) continue;
                float rot = kind == SelKind.Stream ? _level.Streams[index].Rotation : 0f;
                _rotSnapshot.Add((kind, index, ElementPos(kind, index), rot));
            }
            _rotating = _rotSnapshot.Count > 0;
            return _rotating;
        }

        private void DragRotate(Vector2 local)
        {
            float delta = AngleDeg(_rotPivot, ScreenToWorld(local)) - _rotStartAngle;
            Undo.RecordObject(_level, "Повернуть элементы");
            foreach (var s in _rotSnapshot)
            {
                SetElementPos(s.kind, s.index, _rotPivot + Rotate(s.pos - _rotPivot, delta));
                if (s.kind == SelKind.Stream)
                    _level.Streams[s.index].Rotation = s.rot + delta;
            }
            EditorUtility.SetDirty(_level);
        }

        /// <summary>Ring + knob above the selection's pivot; grab the knob and swing to rotate.</summary>
        private void DrawRotateHandle()
        {
            if (!HasRotatablePivot()) return;
            Vector2 pivot = WorldToScreen(SelectionPivot());
            Vector2 knob = pivot + new Vector2(0f, -RotHandlePx);

            Handles.color = new Color(1f, 1f, 1f, 0.3f);
            Handles.DrawWireDisc(pivot, Vector3.forward, RotHandlePx);
            Handles.DrawLine(pivot, knob);
            Handles.color = new Color(0f, 0f, 0f, 0.5f);
            Handles.DrawSolidDisc(knob, Vector3.forward, _rotating ? 9f : 7f);
            Handles.color = new Color(0.4f, 0.8f, 1f, _rotating ? 1f : 0.85f);
            Handles.DrawSolidDisc(knob, Vector3.forward, _rotating ? 7f : 5.5f);
        }

        // ── Inspector ─────────────────────────────────────────────────────────

        // Deferred inspector action: button handlers must not mutate the array or
        // exit early mid-layout (it desyncs the GUIClip/ScrollView stack). They set
        // this instead; it runs after the layout groups are closed.
        private enum PendingAction { None, ConvertToPoints, ClearPoints, DeleteStream, DeleteHazard, DeleteBurner, DeleteZeus, DeleteSelected }
        private PendingAction _pending;
        private int _pendingIndex;

        private void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            _serialized.Update();
            EditorGUI.BeginChangeCheck();

            if (_multi.Count > 1)
            {
                DrawMultiInspector();
            }
            else
            {
                switch (_selKind)
                {
                    case SelKind.Stream: DrawStreamInspector(); break;
                    case SelKind.Hazard: DrawHazardInspector(); break;
                    case SelKind.Burner: DrawBurnerInspector(); break;
                    case SelKind.Zeus: DrawZeusInspector(); break;
                    default: DrawLevelInspector(); break;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(_level);
                Repaint();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            RunPendingAction();
            RunPendingConeEdits();
            RunPendingAreaEdits();
        }

        private void RunPendingAction()
        {
            switch (_pending)
            {
                case PendingAction.ConvertToPoints: ConvertToCustomPoints(_pendingIndex); break;
                case PendingAction.ClearPoints: ClearCustomPoints(_pendingIndex); break;
                case PendingAction.DeleteStream: DeleteStream(_pendingIndex); break;
                case PendingAction.DeleteHazard: DeleteHazard(_pendingIndex); break;
                case PendingAction.DeleteBurner: DeleteBurner(_pendingIndex); break;
                case PendingAction.DeleteZeus: DeleteZeus(_pendingIndex); break;
                case PendingAction.DeleteSelected: DeleteSelected(); break;
            }
            _pending = PendingAction.None;
        }

        private void DrawMultiInspector()
        {
            var streamIdx = new List<int>();
            int hazards = 0, burners = 0, zeuses = 0, other = 0;
            foreach (var (kind, index) in _multi)
            {
                if (kind == SelKind.Stream) streamIdx.Add(index);
                else if (kind == SelKind.Hazard) hazards++;
                else if (kind == SelKind.Burner) burners++;
                else if (kind == SelKind.Zeus) zeuses++;
                else other++;
            }
            EditorGUILayout.LabelField($"Выбрано: {_multi.Count}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"потоков {streamIdx.Count}, препятствий {hazards}, горелок {burners}, Зевсов {zeuses}" +
                (other > 0 ? ", старт/цель" : ""));
            EditorGUILayout.HelpBox(
                "Тащи — двигать всю группу. Ctrl+C/Ctrl+V — копировать группу. " +
                "Delete — удалить потоки/препятствия группы. Shift/Ctrl+клик — добавить/убрать из выделения.",
                MessageType.None);

            if (streamIdx.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Поток (группа из {streamIdx.Count})", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(
                    "Ввод значения — задать всем. «+/−» — сдвинуть каждого от его текущего. Прочерк = значения различаются.",
                    MessageType.None);
                GroupFloatRow(streamIdx, "Масштаб",
                    "Равномерный масштаб траектории у всех выбранных (любой тип).",
                    d => d.Scale, (d, v) => d.Scale = v, 0.1f, 0.1f);
                GroupFloatRow(streamIdx, "Скорость",
                    "Скорость течения у всех выбранных потоков.",
                    d => d.Speed, (d, v) => d.Speed = v, 0.5f, 0f);
                GroupFloatRow(streamIdx, "Скорость в конце",
                    "Линейный разгон к концу пути (0 = постоянная).",
                    d => d.SpeedEnd, (d, v) => d.SpeedEnd = v, 0.5f, 0f);
                GroupFloatRow(streamIdx, "Ширина",
                    "Ширина зоны захвата у всех выбранных потоков.",
                    d => d.Width, (d, v) => d.Width = v, 0.5f, 0.1f);
                GroupFloatRow(streamIdx, "Время активности",
                    "Секунды «включён» в пульс-цикле (0 = всегда).",
                    d => d.ActiveDuration, (d, v) => d.ActiveDuration = v, 0.5f, 0f);
                GroupFloatRow(streamIdx, "Время паузы",
                    "Секунды «выключен» в пульс-цикле (0 = не выключается).",
                    d => d.InactiveDuration, (d, v) => d.InactiveDuration = v, 0.5f, 0f);
                GroupFloatRow(streamIdx, "Интервал реверса",
                    "Каждые N секунд разворот течения (0 = никогда).",
                    d => d.ReverseInterval, (d, v) => d.ReverseInterval = v, 0.5f, 0f);
                GroupFloatRow(streamIdx, "Турбулентность",
                    "Амплитуда поперечной болтанки.",
                    d => d.Turbulence, (d, v) => d.Turbulence = v, 0.25f, 0f);
                GroupFloatRow(streamIdx, "Хват к оси",
                    "Притяжение к центральной линии (3 = обычный, 6–10 = рельсы).",
                    d => d.Grip, (d, v) => d.Grip = v, 1f, 0.5f);
                GroupFloatRow(streamIdx, "Скорость подстройки",
                    "Как быстро скорость цепляет течение (0 = как «Хват к оси»).",
                    d => d.CatchRate, (d, v) => d.CatchRate = v, 1f, 0f);
                GroupFloatRow(streamIdx, "Импульс выхода",
                    "Множитель скорости при складывании крыльев в потоке (1 = без буста).",
                    d => d.ExitBoost, (d, v) => d.ExitBoost = v, 0.1f, 0.1f);
                GroupFloatRow(streamIdx, "Z (приоритет)",
                    "Слой захвата: больше Z — несёт он, нижние игнорятся (OLD и NEW).",
                    d => d.Z, (d, v) => d.Z = v, 1f, float.NegativeInfinity);
            }

            EditorGUILayout.Space();
            if (streamIdx.Count > 0 && GUILayout.Button("⇄ Развернуть стрелки (все)"))
            {
                Undo.RecordObject(_level, "Развернуть потоки группы");
                foreach (int i in streamIdx)
                    _level.Streams[i].Reverse = !_level.Streams[i].Reverse;
                EditorUtility.SetDirty(_level);
            }
            if ((streamIdx.Count > 0 || hazards > 0 || burners > 0 || zeuses > 0)
                && GUILayout.Button("Удалить выбранные"))
                _pending = PendingAction.DeleteSelected;
        }

        /// <summary>
        /// One float row editing the same field on every selected stream. Mixed
        /// values show as a dash; typing sets all, +/− nudges each from its own value.
        /// </summary>
        private void GroupFloatRow(List<int> streamIdx, string label, string tooltip,
            System.Func<StreamDef, float> get, System.Action<StreamDef, float> set,
            float step, float min)
        {
            float first = get(_level.Streams[streamIdx[0]]);
            bool mixed = false;
            for (int i = 1; i < streamIdx.Count; i++)
                if (!Mathf.Approximately(get(_level.Streams[streamIdx[i]]), first))
                {
                    mixed = true;
                    break;
                }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            float typed = EditorGUILayout.FloatField(new GUIContent(label, tooltip), first);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            bool plus = GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(22f));
            bool minus = GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22f));
            EditorGUILayout.EndHorizontal();

            if (!changed && !plus && !minus)
                return;

            Undo.RecordObject(_level, "Правка группы потоков");
            foreach (int i in streamIdx)
            {
                StreamDef def = _level.Streams[i];
                float value = changed ? typed : get(def);
                if (plus) value += step;
                if (minus) value -= step;
                set(def, Mathf.Max(min, value));
            }
            EditorUtility.SetDirty(_level);
        }

        private void DrawLevelInspector()
        {
            EditorGUILayout.LabelField("Уровень", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serialized.FindProperty("Mode"),
                new GUIContent("Режим", "Free — свободная камера, смерть только снизу. UpOnly — камера едет вверх, бока экрана смертельны. SingleScreen — статичная камера, смертельны все края."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("CameraSize"),
                new GUIContent("Размер камеры", "Половина высоты вьюпорта в юнитах. Задаёт kill-стены в режимах UpOnly / SingleScreen."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("Start"),
                new GUIContent("Старт", "Точка спавна игрока и привязки камеры в режимах UpOnly / SingleScreen."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("Goal"),
                new GUIContent("Цель", "Точка финиша (солнце). Касание = победа."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("GoalRadius"),
                new GUIContent("Радиус цели", "Радиус зоны победы вокруг цели."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("KillY"),
                new GUIContent("Линия смерти Y", "Высота нижней смертельной линии. Падение ниже = респавн."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("TimeScale"),
                new GUIContent("Скорость времени", "Глобальный множитель скорости игры для этого уровня."));
            EditorGUILayout.PropertyField(_serialized.FindProperty("StartStreamIndex"),
                new GUIContent("Индекс старт-потока", "Номер потока (в списке), в который респавнится игрок."));
            EditorGUILayout.HelpBox(
                "Кликни элемент на карте, чтобы редактировать. Тащи — двигать, колесо — зум, " +
                "средняя кнопка — панорама.", MessageType.None);
        }

        private void DrawStreamInspector()
        {
            EditorGUILayout.LabelField("Поток " + _selIndex, EditorStyles.boldLabel);
            SerializedProperty streams = _serialized.FindProperty("Streams");
            if (_selIndex < 0 || _selIndex >= streams.arraySize)
            {
                EditorGUILayout.LabelField("(поток не выбран)");
                return; // safe: no layout group opened yet beyond the label
            }
            SerializedProperty s = streams.GetArrayElementAtIndex(_selIndex);

            EditorGUILayout.PropertyField(s.FindPropertyRelative("Position"),
                new GUIContent("Позиция", "Точка размещения потока в мире (origin), к ней привязана вся траектория."));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("Rotation"),
                new GUIContent("Поворот", "Поворот траектории вокруг точки размещения, в градусах."));
            EditorGUILayout.Space();

            StreamDef def = _level.Streams[_selIndex];
            if (def.IsCircle)
            {
                EditorGUILayout.LabelField("Круг", EditorStyles.miniBoldLabel);
                FloatRow(s.FindPropertyRelative("CircleRadius"), "Радиус",
                    "Радиус кругового потока. Число точек кольца растёт вместе с радиусом.", 0.5f, 0.5f);
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Reverse"),
                    new GUIContent("Реверс", "Развернуть направление течения по кольцу."));
                if (GUILayout.Button("Превратить в рисованный путь"))
                {
                    _pending = PendingAction.ConvertToPoints;
                    _pendingIndex = _selIndex;
                }
            }
            else if (def.UsesCustomPoints)
            {
                EditorGUILayout.LabelField("Рисованный путь", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(def.CustomPoints.Length + " точек (тащить на карте).");
                EditorGUILayout.HelpBox(
                    "Alt+клик по карте — добавить точку в конец. Тащи точки, чтобы двигать.",
                    MessageType.None);
                if (GUILayout.Button("Назад к генератору формы"))
                {
                    _pending = PendingAction.ClearPoints;
                    _pendingIndex = _selIndex;
                }
            }
            else
            {
                EditorGUILayout.LabelField("Форма", EditorStyles.miniBoldLabel);
                SerializedProperty shapeProp = s.FindPropertyRelative("Shape");
                shapeProp.enumValueIndex = EditorGUILayout.Popup(
                    new GUIContent("Форма", "Тип параметрической траектории потока."),
                    shapeProp.enumValueIndex, ShapeNamesRu);
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Size"),
                    new GUIContent("Размер", "Главный размер формы: длина / радиус / ширина."));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Size2"),
                    new GUIContent("Размер 2", "Вторичный размер: высота / амплитуда / внутренний радиус."));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Count"),
                    new GUIContent("Количество", "Число повторов: периоды / ступени / лепестки / витки."));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Turns"),
                    new GUIContent("Витки", "Витки для спирали; для дуги — угол развёртки в градусах."));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Seed"),
                    new GUIContent("Сид", "Зерно генератора случайности (только для шумовой формы NoisePath)."));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Reverse"),
                    new GUIContent("Реверс", "Развернуть направление течения потока."));
                if (GUILayout.Button("Превратить в рисованный путь"))
                {
                    _pending = PendingAction.ConvertToPoints;
                    _pendingIndex = _selIndex;
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Поток", EditorStyles.miniBoldLabel);
            FloatRow(s.FindPropertyRelative("Scale"), "Масштаб",
                "Равномерно масштабирует всю траекторию (любой тип: круг, рисованный, форма), " +
                "не меняя ширину. 1 = как есть, 2 = вдвое больше.", 0.1f, 0.1f);
            FloatRow(s.FindPropertyRelative("Speed"), "Скорость",
                "Скорость течения потока — как быстро он несёт Икара вдоль траектории. " +
                "Если задана «Скорость в конце», это скорость на СТАРТЕ пути.", 0.5f, 0f);
            FloatRow(s.FindPropertyRelative("SpeedEnd"), "Скорость в конце",
                "Скорость на конце пути: течение линейно разгоняется (или замедляется) от «Скорость» " +
                "к этому значению. 0 = постоянная скорость по всей длине.", 0.5f, 0f);
            FloatRow(s.FindPropertyRelative("Width"), "Ширина",
                "Ширина потока. Икар захватывается, пока находится внутри этой полосы.", 0.5f, 0.1f);
            FloatRow(s.FindPropertyRelative("ActiveDuration"), "Время активности",
                "Сколько секунд поток включён в пульс-цикле. 0 = поток включён всегда.", 0.5f, 0f);
            FloatRow(s.FindPropertyRelative("InactiveDuration"), "Время паузы",
                "Сколько секунд поток выключен (роняет игрока). 0 = поток не выключается.", 0.5f, 0f);
            FloatRow(s.FindPropertyRelative("ReverseInterval"), "Интервал реверса",
                "Каждые N секунд поток меняет направление течения на противоположное. 0 = никогда.", 0.5f, 0f);
            FloatRow(s.FindPropertyRelative("Turbulence"), "Турбулентность",
                "Амплитуда поперечного дрожания потока — болтает игрока вбок. 0 = ровный поток.", 0.25f, 0f);
            FloatRow(s.FindPropertyRelative("Grip"), "Хват к оси",
                "Притяжение к центральной линии потока. 3 = обычный, 6–10 = рельсы " +
                "(не слетит на резких поворотах даже на большой скорости), 1 = рыхлая река.", 1f, 0.5f);
            FloatRow(s.FindPropertyRelative("CatchRate"), "Скорость подстройки",
                "Как быстро скорость Икара подстраивается под течение, независимо от притяжения к оси. " +
                "0 = как «Хват к оси» (раньше был один параметр). Больше = быстрее цепляет скорость потока, меньше = вязко.", 1f, 0f);
            FloatRow(s.FindPropertyRelative("ExitBoost"), "Импульс выхода",
                "Множитель скорости в момент складывания крыльев внутри потока. " +
                "1 = чистый импульс потока, 1.5 = катапульта, меньше 1 = вязкий выход.", 0.1f, 0.1f);
            FloatRow(s.FindPropertyRelative("Z"), "Z (приоритет)",
                "Слой захвата. Когда несколько потоков накрывают Икара, несёт тот, у кого Z " +
                "больше; нижние слои игнорятся. OLD: захватывает один верхний (равные Z — глубже). " +
                "NEW: блендятся только потоки верхнего слоя.", 1f, float.NegativeInfinity);

            EditorGUILayout.Space();
            if (GUILayout.Button("⇄ Развернуть стрелки"))
            {
                SerializedProperty rev = s.FindPropertyRelative("Reverse");
                rev.boolValue = !rev.boolValue;
            }
            EditorGUILayout.HelpBox("Цвет потока зависит от скорости: синий — медленно, красный — быстро.",
                MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("Удалить поток"))
            {
                _pending = PendingAction.DeleteStream;
                _pendingIndex = _selIndex;
            }
        }

        /// <summary>
        /// One float field with a Russian tooltip and −/+ step buttons. The tooltip
        /// shows on hover over the label; the buttons nudge the value by `step`
        /// (clamped to `min`).
        /// </summary>
        private static void FloatRow(SerializedProperty prop, string label, string tooltip, float step, float min)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip));
            if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(22f)))
                prop.floatValue = Mathf.Max(min, prop.floatValue + step);
            if (GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                prop.floatValue = Mathf.Max(min, prop.floatValue - step);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHazardInspector()
        {
            EditorGUILayout.LabelField("Препятствие " + _selIndex, EditorStyles.boldLabel);
            SerializedProperty hazards = _serialized.FindProperty("Hazards");
            if (_selIndex < 0 || _selIndex >= hazards.arraySize)
            {
                EditorGUILayout.LabelField("(препятствие не выбрано)");
                return;
            }
            SerializedProperty h = hazards.GetArrayElementAtIndex(_selIndex);

            EditorGUILayout.PropertyField(h.FindPropertyRelative("Position"),
                new GUIContent("Позиция", "Положение препятствия в мире."));
            EditorGUILayout.PropertyField(h.FindPropertyRelative("PatrolTravel"),
                new GUIContent("Путь патруля", "Смещение, на которое препятствие колеблется туда-обратно."));
            EditorGUILayout.PropertyField(h.FindPropertyRelative("PatrolPeriod"),
                new GUIContent("Период патруля", "Время полного цикла туда-обратно, в секундах."));
            EditorGUILayout.PropertyField(h.FindPropertyRelative("Size"),
                new GUIContent("Размер", "Радиус препятствия."));

            EditorGUILayout.Space();
            if (GUILayout.Button("Удалить препятствие"))
            {
                _pending = PendingAction.DeleteHazard;
                _pendingIndex = _selIndex;
            }
        }

        // Deferred cone-list edits (mutate the SerializedProperty array after the
        // current layout pass, same reasoning as PendingAction).
        private int _coneAddBurner = -1;
        private int _coneRemoveBurner = -1, _coneRemoveIndex = -1;

        // Same deferred pattern for Zeus area-list edits.
        private int _areaAddZeus = -1;
        private int _areaRemoveZeus = -1, _areaRemoveIndex = -1;

        private void DrawBurnerInspector()
        {
            EditorGUILayout.LabelField("Горелка " + _selIndex, EditorStyles.boldLabel);
            SerializedProperty burners = _serialized.FindProperty("Burners");
            if (_selIndex < 0 || _selIndex >= burners.arraySize)
            {
                EditorGUILayout.LabelField("(горелка не выбрана)");
                return;
            }
            SerializedProperty b = burners.GetArrayElementAtIndex(_selIndex);

            EditorGUILayout.PropertyField(b.FindPropertyRelative("Position"),
                new GUIContent("Позиция", "Точка, из которой выходят лучи."));

            EditorGUILayout.Space();
            SerializedProperty cones = b.FindPropertyRelative("Cones");
            EditorGUILayout.LabelField($"Конусы ({cones.arraySize})", EditorStyles.boldLabel);

            for (int i = 0; i < cones.arraySize; i++)
            {
                SerializedProperty cone = cones.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Конус " + i, EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(70f)))
                {
                    _coneRemoveBurner = _selIndex;
                    _coneRemoveIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(cone.FindPropertyRelative("Angle"),
                    new GUIContent("Угол", "Направление луча, градусы (0 = вправо). Для вращения — начальный угол."));
                EditorGUILayout.PropertyField(cone.FindPropertyRelative("Length"),
                    new GUIContent("Длина", "Дальность луча в мире."));
                EditorGUILayout.PropertyField(cone.FindPropertyRelative("HalfAngle"),
                    new GUIContent("Полуугол", "Половина раствора конуса, градусы."));

                SerializedProperty motion = cone.FindPropertyRelative("Motion");
                EditorGUILayout.PropertyField(motion,
                    new GUIContent("Движение", "Статичный / вращение / мигание по таймеру."));

                var mode = (ConeMotion)motion.enumValueIndex;
                if (mode == ConeMotion.Rotate)
                {
                    EditorGUILayout.PropertyField(cone.FindPropertyRelative("RotateSpeed"),
                        new GUIContent("Скорость вращения", "Градусы/сек (знак задаёт направление)."));
                }
                else if (mode == ConeMotion.Pulse)
                {
                    EditorGUILayout.PropertyField(cone.FindPropertyRelative("OnDuration"),
                        new GUIContent("Горит, сек", "Сколько секунд луч активен в цикле."));
                    EditorGUILayout.PropertyField(cone.FindPropertyRelative("OffDuration"),
                        new GUIContent("Пауза, сек", "Сколько секунд луч погашен в цикле."));
                    EditorGUILayout.PropertyField(cone.FindPropertyRelative("PhaseOffset"),
                        new GUIContent("Сдвиг фазы, сек", "Смещение цикла, чтобы конусы мигали вразнобой."));
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("+ Конус"))
                _coneAddBurner = _selIndex;

            EditorGUILayout.Space();
            if (GUILayout.Button("Удалить горелку"))
            {
                _pending = PendingAction.DeleteBurner;
                _pendingIndex = _selIndex;
            }
        }

        private void DrawZeusInspector()
        {
            EditorGUILayout.LabelField("Зевс " + _selIndex, EditorStyles.boldLabel);
            SerializedProperty zeuses = _serialized.FindProperty("Zeuses");
            if (_selIndex < 0 || _selIndex >= zeuses.arraySize)
            {
                EditorGUILayout.LabelField("(Зевс не выбран)");
                return;
            }
            SerializedProperty z = zeuses.GetArrayElementAtIndex(_selIndex);

            EditorGUILayout.PropertyField(z.FindPropertyRelative("Position"),
                new GUIContent("Позиция", "Точка, из которой вылетают молнии."));

            EditorGUILayout.Space();
            SerializedProperty areas = z.FindPropertyRelative("Areas");
            EditorGUILayout.LabelField($"Области ({areas.arraySize})", EditorStyles.boldLabel);

            for (int i = 0; i < areas.arraySize; i++)
            {
                SerializedProperty area = areas.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Область " + i, EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(70f)))
                {
                    _areaRemoveZeus = _selIndex;
                    _areaRemoveIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(area.FindPropertyRelative("Offset"),
                    new GUIContent("Смещение", "Центр области относительно точки Зевса."));
                EditorGUILayout.PropertyField(area.FindPropertyRelative("RadiusX"),
                    new GUIContent("Радиус X", "Горизонтальный радиус эллипса. Равные радиусы = круг."));
                EditorGUILayout.PropertyField(area.FindPropertyRelative("RadiusY"),
                    new GUIContent("Радиус Y", "Вертикальный радиус эллипса."));
                EditorGUILayout.PropertyField(area.FindPropertyRelative("Period"),
                    new GUIContent("Период, сек", "Сколько секунд между ударами по этой области."));
                EditorGUILayout.PropertyField(area.FindPropertyRelative("StartDelay"),
                    new GUIContent("Задержка старта, сек", "Пауза перед самым первым ударом."));
                EditorGUILayout.PropertyField(area.FindPropertyRelative("FlightTime"),
                    new GUIContent("Время полёта, сек", "Сколько молния летит от Зевса до области."));

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("+ Область"))
                _areaAddZeus = _selIndex;

            EditorGUILayout.Space();
            if (GUILayout.Button("Удалить Зевса"))
            {
                _pending = PendingAction.DeleteZeus;
                _pendingIndex = _selIndex;
            }
        }

        /// <summary>Apply deferred cone add/remove after the layout pass closes.</summary>
        private void RunPendingConeEdits()
        {
            if (_coneAddBurner < 0 && _coneRemoveBurner < 0)
                return;

            _serialized.Update();
            SerializedProperty burners = _serialized.FindProperty("Burners");

            if (_coneAddBurner >= 0 && _coneAddBurner < burners.arraySize)
            {
                SerializedProperty cones = burners.GetArrayElementAtIndex(_coneAddBurner)
                    .FindPropertyRelative("Cones");
                cones.arraySize++; // new element copies the last one's values — fine as a starting point
            }

            if (_coneRemoveBurner >= 0 && _coneRemoveBurner < burners.arraySize)
            {
                SerializedProperty cones = burners.GetArrayElementAtIndex(_coneRemoveBurner)
                    .FindPropertyRelative("Cones");
                if (_coneRemoveIndex >= 0 && _coneRemoveIndex < cones.arraySize)
                    cones.DeleteArrayElementAtIndex(_coneRemoveIndex);
            }

            _serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(_level);
            _coneAddBurner = -1;
            _coneRemoveBurner = -1;
            _coneRemoveIndex = -1;
            Repaint();
        }

        /// <summary>Apply deferred Zeus area add/remove after the layout pass closes.</summary>
        private void RunPendingAreaEdits()
        {
            if (_areaAddZeus < 0 && _areaRemoveZeus < 0)
                return;

            _serialized.Update();
            SerializedProperty zeuses = _serialized.FindProperty("Zeuses");

            if (_areaAddZeus >= 0 && _areaAddZeus < zeuses.arraySize)
            {
                SerializedProperty areas = zeuses.GetArrayElementAtIndex(_areaAddZeus)
                    .FindPropertyRelative("Areas");
                areas.arraySize++; // new element copies the last one's values — fine as a starting point
            }

            if (_areaRemoveZeus >= 0 && _areaRemoveZeus < zeuses.arraySize)
            {
                SerializedProperty areas = zeuses.GetArrayElementAtIndex(_areaRemoveZeus)
                    .FindPropertyRelative("Areas");
                if (_areaRemoveIndex >= 0 && _areaRemoveIndex < areas.arraySize)
                    areas.DeleteArrayElementAtIndex(_areaRemoveIndex);
            }

            _serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(_level);
            _areaAddZeus = -1;
            _areaRemoveZeus = -1;
            _areaRemoveIndex = -1;
            Repaint();
        }

        // ── Asset management ──────────────────────────────────────────────────

        private const string LevelsFolder = "Assets/_Project/Levels";

        /// <summary>Create a fresh blank LevelData asset and load it into the editor.</summary>
        private void NewLevel()
        {
            string path = PromptSavePath("Новый уровень", "Level");
            if (string.IsNullOrEmpty(path))
                return;

            var level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssets();
            LoadLevel(level);
        }

        /// <summary>Duplicate the current LevelData (deep copy of all data) to a new asset.</summary>
        private void CopyLevel()
        {
            string src = AssetDatabase.GetAssetPath(_level);
            string suggested = Path.GetFileNameWithoutExtension(src) + " Copy";
            string path = PromptSavePath("Копировать уровень", suggested);
            if (string.IsNullOrEmpty(path))
                return;

            // CopyAsset clones the serialized data, so streams/hazards arrays come
            // across by value — the copy is fully independent of the original.
            AssetDatabase.CopyAsset(src, path);
            AssetDatabase.SaveAssets();
            LoadLevel(AssetDatabase.LoadAssetAtPath<LevelData>(path));
        }

        /// <summary>Save-file dialog rooted in the Levels folder, returning a project-relative path (or null).</summary>
        private static string PromptSavePath(string title, string defaultName)
        {
            if (!AssetDatabase.IsValidFolder(LevelsFolder))
                System.IO.Directory.CreateDirectory(LevelsFolder);

            string abs = EditorUtility.SaveFilePanel(title, LevelsFolder, defaultName, "asset");
            if (string.IsNullOrEmpty(abs))
                return null;

            string projectRoot = Path.GetDirectoryName(Application.dataPath); // strips trailing "/Assets"
            string rel = Path.GetRelativePath(projectRoot, abs).Replace('\\', '/');
            if (!rel.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog(title, "Сохраняйте уровень внутри папки Assets проекта.", "OK");
                return null;
            }
            return AssetDatabase.GenerateUniqueAssetPath(rel);
        }

        private void LoadLevel(LevelData level)
        {
            _level = level;
            SetSingleSelection(SelKind.None, -1);
            // Deliberately NOT selecting the asset in the Project window: a selected
            // asset turns the Delete key into "delete asset file", which would nuke
            // the level when the user only meant to delete a stream/hazard.
            RebuildSerialized();
            FrameAll();
        }

        // ── Mutations ─────────────────────────────────────────────────────────

        // Russian labels aligned to the StreamShape enum order.
        private static readonly string[] ShapeNamesRu =
        {
            "Линия", "Дуга", "Круг", "Эллипс", "Синусоида", "Зигзаг", "Спираль",
            "Восьмёрка", "S-кривая", "Горка", "Яма", "Мёртвая петля", "Штопор",
            "Лестница", "Звезда", "Шум", "Прямоугольник", "Сердце",
        };

        /// <summary>Dropdown with every parametric shape — picks one and adds a shaped stream.</summary>
        private void ShowShapeMenu()
        {
            var menu = new GenericMenu();
            var shapes = (StreamShape[])System.Enum.GetValues(typeof(StreamShape));
            for (int i = 0; i < shapes.Length; i++)
            {
                string label = i < ShapeNamesRu.Length ? ShapeNamesRu[i] : shapes[i].ToString();
                StreamShape shape = shapes[i];
                menu.AddItem(new GUIContent(label), false, () => AddShapedStream(shape));
            }
            menu.ShowAsContext();
        }

        private void AddShapedStream(StreamShape shape)
        {
            Undo.RecordObject(_level, "Добавить поток-форму");
            var list = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            // Empty CustomPoints -> the shape generator builds the trajectory;
            // its parameters are edited in the inspector (Размер/Количество/...).
            list.Add(new StreamDef { Position = _panWorld, Shape = shape });
            _level.Streams = list.ToArray();
            SetSingleSelection(SelKind.Stream, list.Count - 1);
            Commit();
        }

        private void AddStream()
        {
            Undo.RecordObject(_level, "Добавить поток");
            var list = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            // Default to a hand-drawn path: seed two points so UsesCustomPoints is true.
            var def = new StreamDef
            {
                Position = _panWorld,
                CustomPoints = new[] { new Vector2(-2f, 0f), new Vector2(2f, 0f) },
            };
            list.Add(def);
            _level.Streams = list.ToArray();
            SetSingleSelection(SelKind.Stream, list.Count - 1);
            Commit();
        }

        private void AddCircle()
        {
            Undo.RecordObject(_level, "Добавить круговой поток");
            var list = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            list.Add(new StreamDef { Position = _panWorld, IsCircle = true, CircleRadius = 4f });
            _level.Streams = list.ToArray();
            SetSingleSelection(SelKind.Stream, list.Count - 1);
            Commit();
        }

        /// <summary>
        /// A fork: one trunk stream up to a split point, then several branches fanning
        /// out from it — each a separate hand-drawn stream sharing the same origin, so
        /// the player can hop between them. All branches start at the fork point so they
        /// stay joined when the trunk is dragged.
        /// </summary>
        private void AddFork()
        {
            Undo.RecordObject(_level, "Добавить развилку");
            var list = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            Vector2 o = _panWorld;
            var fork = new Vector2(0f, 4f);

            StreamDef Branch(params Vector2[] pts) => new StreamDef { Position = o, CustomPoints = pts };

            int first = list.Count;
            list.Add(Branch(new Vector2(0f, 0f), new Vector2(0f, 2f), fork));                      // trunk (up)
            list.Add(Branch(fork, new Vector2(-2f, 6f), new Vector2(-4f, 8.5f)));                  // up-left
            list.Add(Branch(fork, new Vector2(0.4f, 6.5f), new Vector2(1f, 9f)));                  // up
            list.Add(Branch(fork, new Vector2(2f, 4.5f), new Vector2(4f, 2f), new Vector2(5.5f, -1.5f))); // down-right
            _level.Streams = list.ToArray();

            // Select the whole fork so it drags / edits as a group.
            _multi.Clear();
            for (int i = first; i < list.Count; i++)
                _multi.Add((SelKind.Stream, i));
            _selKind = SelKind.Stream;
            _selIndex = first;
            Commit();
        }

        private void AddHazard()
        {
            Undo.RecordObject(_level, "Добавить препятствие");
            var list = new List<HazardDef>(_level.Hazards ?? new HazardDef[0]);
            list.Add(new HazardDef { Position = _panWorld });
            _level.Hazards = list.ToArray();
            SetSingleSelection(SelKind.Hazard, list.Count - 1);
            Commit();
        }

        private void AddBurner()
        {
            Undo.RecordObject(_level, "Добавить горелку");
            var list = new List<BurnerDef>(_level.Burners ?? new BurnerDef[0]);
            list.Add(new BurnerDef { Position = _panWorld, Cones = new[] { new ConeDef() } });
            _level.Burners = list.ToArray();
            SetSingleSelection(SelKind.Burner, list.Count - 1);
            Commit();
        }

        private void AddZeus()
        {
            Undo.RecordObject(_level, "Добавить Зевса");
            var list = new List<ZeusDef>(_level.Zeuses ?? new ZeusDef[0]);
            list.Add(new ZeusDef { Position = _panWorld });
            _level.Zeuses = list.ToArray();
            SetSingleSelection(SelKind.Zeus, list.Count - 1);
            Commit();
        }

        private void ConvertToCustomPoints(int index)
        {
            Undo.RecordObject(_level, "Превратить поток в путь");
            StreamDef def = _level.Streams[index];
            // Build(def) routes circle -> ring and bakes Reverse into point order,
            // so circles convert too and the flow direction survives the conversion.
            List<Vector2> pts = StreamShapeBuilder.Build(def, out bool loop);
            def.IsCircle = false;   // a baked ring is now just an editable closed path
            def.Reverse = false;    // Build already baked Reverse into the point order
            def.Scale = 1f;         // Build already baked Scale into the points
            def.CustomPoints = pts.ToArray();
            def.CustomLoop = loop;
            Commit();
        }

        private void ClearCustomPoints(int index)
        {
            Undo.RecordObject(_level, "Очистить точки пути");
            _level.Streams[index].CustomPoints = new Vector2[0];
            Commit();
        }

        private void DeleteStream(int index)
        {
            Undo.RecordObject(_level, "Удалить поток");
            var list = new List<StreamDef>(_level.Streams);
            list.RemoveAt(index);
            _level.Streams = list.ToArray();
            SetSingleSelection(SelKind.None, -1);
            Commit();
        }

        private void DeleteHazard(int index)
        {
            Undo.RecordObject(_level, "Удалить препятствие");
            var list = new List<HazardDef>(_level.Hazards);
            list.RemoveAt(index);
            _level.Hazards = list.ToArray();
            SetSingleSelection(SelKind.None, -1);
            Commit();
        }

        private void DeleteBurner(int index)
        {
            Undo.RecordObject(_level, "Удалить горелку");
            var list = new List<BurnerDef>(_level.Burners);
            list.RemoveAt(index);
            _level.Burners = list.ToArray();
            SetSingleSelection(SelKind.None, -1);
            Commit();
        }

        private void DeleteZeus(int index)
        {
            Undo.RecordObject(_level, "Удалить Зевса");
            var list = new List<ZeusDef>(_level.Zeuses);
            list.RemoveAt(index);
            _level.Zeuses = list.ToArray();
            SetSingleSelection(SelKind.None, -1);
            Commit();
        }

        /// <summary>Delete every selected stream/hazard/burner/zeus (multi-selection aware).</summary>
        private void DeleteSelected()
        {
            Undo.RecordObject(_level, "Удалить выбранные");

            // Collect indices per array and remove from the end so they stay valid.
            var streamIdx = new List<int>();
            var hazardIdx = new List<int>();
            var burnerIdx = new List<int>();
            var zeusIdx = new List<int>();
            foreach (var (kind, index) in _multi)
            {
                if (kind == SelKind.Stream) streamIdx.Add(index);
                else if (kind == SelKind.Hazard) hazardIdx.Add(index);
                else if (kind == SelKind.Burner) burnerIdx.Add(index);
                else if (kind == SelKind.Zeus) zeusIdx.Add(index);
            }
            streamIdx.Sort();
            hazardIdx.Sort();
            burnerIdx.Sort();
            zeusIdx.Sort();

            var streams = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            for (int i = streamIdx.Count - 1; i >= 0; i--)
                streams.RemoveAt(streamIdx[i]);
            _level.Streams = streams.ToArray();

            var hazards = new List<HazardDef>(_level.Hazards ?? new HazardDef[0]);
            for (int i = hazardIdx.Count - 1; i >= 0; i--)
                hazards.RemoveAt(hazardIdx[i]);
            _level.Hazards = hazards.ToArray();

            var burners = new List<BurnerDef>(_level.Burners ?? new BurnerDef[0]);
            for (int i = burnerIdx.Count - 1; i >= 0; i--)
                burners.RemoveAt(burnerIdx[i]);
            _level.Burners = burners.ToArray();

            var zeuses = new List<ZeusDef>(_level.Zeuses ?? new ZeusDef[0]);
            for (int i = zeusIdx.Count - 1; i >= 0; i--)
                zeuses.RemoveAt(zeusIdx[i]);
            _level.Zeuses = zeuses.ToArray();

            SetSingleSelection(SelKind.None, -1);
            Commit();
        }

        private void Commit()
        {
            EditorUtility.SetDirty(_level);
            RebuildSerialized();
            Repaint();
        }

        // ── View helpers ──────────────────────────────────────────────────────

        private void FrameAll()
        {
            var pts = new List<Vector2> { _level.Start, _level.Goal };
            if (_level.Streams != null)
                foreach (var s in _level.Streams) pts.Add(s.Position);
            if (_level.Hazards != null)
                foreach (var h in _level.Hazards) pts.Add(h.Position);

            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            _panWorld = (min + max) * 0.5f;
            Vector2 span = Vector2.Max(max - min, Vector2.one * 6f);
            float fitX = (_canvasRect.width - 60f) / span.x;
            float fitY = (_canvasRect.height - 60f) / span.y;
            _pixelsPerUnit = Mathf.Clamp(Mathf.Min(fitX, fitY), 4f, 200f);
            Repaint();
        }

        private Vector2 WorldToScreen(Vector2 world)
        {
            Vector2 fromCenter = (world - _panWorld) * _pixelsPerUnit;
            return new Vector2(
                _canvasRect.width * 0.5f + fromCenter.x,
                _canvasRect.height * 0.5f - fromCenter.y); // flip Y
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            float x = (screen.x - _canvasRect.width * 0.5f) / _pixelsPerUnit;
            float y = (_canvasRect.height * 0.5f - screen.y) / _pixelsPerUnit;
            return _panWorld + new Vector2(x, y);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
#endif
