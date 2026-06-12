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

        private enum SelKind { None, Stream, Hazard, Start, Goal }
        private SelKind _selKind = SelKind.None;
        private int _selIndex = -1;

        private Vector2 _inspectorScroll;
        private Rect _canvasRect;

        [MenuItem("NGJ/Редактор карт")]
        public static void Open() => GetWindow<MapEditorWindow>("Редактор карт");

        private void OnEnable()
        {
            if (_level == null)
                _level = Selection.activeObject as LevelData;
            RebuildSerialized();
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
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand)
                return;
            if (e.commandName != "Delete" && e.commandName != "SoftDelete")
                return;

            bool canDelete = _selKind == SelKind.Stream || _selKind == SelKind.Hazard;
            if (!canDelete)
                return;

            if (e.type == EventType.ValidateCommand)
            {
                e.Use(); // tell Unity we'll handle it
                return;
            }

            // ExecuteCommand: defer the array mutation via _pending (same as the buttons).
            _pending = _selKind == SelKind.Stream ? PendingAction.DeleteStream : PendingAction.DeleteHazard;
            _pendingIndex = _selIndex;
            RunPendingAction();
            e.Use();
            Repaint();
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
                _selKind = SelKind.None;
                _selIndex = -1;
                RebuildSerialized();
            }
            x += 204f;

            if (GUI.Button(new Rect(x, row1.y, 110f, RowHeight), "Новый уровень", EditorStyles.toolbarButton)) NewLevel();
            x += 112f;
            using (new EditorGUI.DisabledScope(_level == null))
            {
                if (GUI.Button(new Rect(x, row1.y, 110f, RowHeight), "Копировать", EditorStyles.toolbarButton)) CopyLevel();
            }

            // Row 2 — element ops: add stream / add circle / add hazard.
            Rect row2 = new Rect(rect.x, rect.y + RowHeight, rect.width, RowHeight);
            GUI.Box(row2, GUIContent.none, EditorStyles.toolbar);
            x = row2.x + 2f;
            using (new EditorGUI.DisabledScope(_level == null))
            {
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Поток", EditorStyles.toolbarButton)) AddStream();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Круг", EditorStyles.toolbarButton)) AddCircle();
                x += 92f;
                if (GUI.Button(new Rect(x, row2.y, 90f, RowHeight), "+ Препятствие", EditorStyles.toolbarButton)) AddHazard();
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
            DrawStartGoal();

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
            Handles.color = new Color(0.2f, 0.5f, 1f, 0.7f);
            Vector2 min = ScreenToWorld(new Vector2(0f, rect.height));
            Vector2 max = ScreenToWorld(new Vector2(rect.width, 0f));
            Vector2 a = WorldToScreen(new Vector2(min.x, _level.KillY));
            Vector2 b = WorldToScreen(new Vector2(max.x, _level.KillY));
            Handles.DrawAAPolyLine(3f, a, b);
            GUI.Label(new Rect(a.x + 4f, a.y, 80f, 16f), "смерть");
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

            var wall = new Color(1f, 0.3f, 0.3f, 0.9f);
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
                WorldToScreen(new Vector2(c.x - halfW, top)).y, 100f, 16f), "стена смерти");
        }

        private void DrawStreams()
        {
            if (_level.Streams == null) return;
            for (int i = 0; i < _level.Streams.Length; i++)
            {
                StreamDef def = _level.Streams[i];
                List<Vector2> pts = StreamShapeBuilder.Build(def, out bool loop);
                if (pts.Count < 2) continue;

                bool selected = _selKind == SelKind.Stream && _selIndex == i;
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

                var screen = new Vector3[wpCount];
                for (int p = 0; p < wpCount; p++)
                    screen[p] = WorldToScreen(world[p]);
                Handles.color = c;
                Handles.DrawAAPolyLine(selected ? 4f : 2f, screen);

                // Direction arrows along the path; arrow length scales with Speed so
                // faster streams read as longer darts.
                DrawFlowArrows(world, loop, def.Speed, def.Reverse, c);

                // Selectable handle at the placement origin.
                Vector2 originScreen = WorldToScreen(def.Position);
                DrawHandleDot(originScreen, selected, c);
                GUI.Label(new Rect(originScreen.x + 8f, originScreen.y - 8f, 140f, 16f),
                    $"П{i}  скор {def.Speed:0.#}  шир {def.Width:0.#}");

                // Editable waypoints of the selected custom path.
                if (selected && def.UsesCustomPoints)
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

        /// <summary>Direction arrows spaced along the path; arrow size scales with Speed.</summary>
        private void DrawFlowArrows(Vector2[] world, bool loop, float speed, bool reverse, Color color)
        {
            int segs = loop ? world.Length : world.Length - 1;
            if (segs < 1)
                return;

            // Roughly one arrow per few waypoints; longer dart for faster flow.
            int stride = Mathf.Max(1, segs / 4);
            float worldLen = 0.4f + Mathf.Clamp(speed, 0f, 12f) * 0.12f;

            for (int p = 0; p < segs; p += stride)
            {
                Vector2 a = world[p];
                Vector2 b = world[(p + 1) % world.Length];
                Vector2 dir = (b - a).normalized;
                if (reverse) dir = -dir;
                Vector2 baseWorld = (a + b) * 0.5f;
                DrawArrow(baseWorld + dir * worldLen, dir, color);
            }
        }

        private void DrawHazards()
        {
            if (_level.Hazards == null) return;
            for (int i = 0; i < _level.Hazards.Length; i++)
            {
                HazardDef def = _level.Hazards[i];
                bool selected = _selKind == SelKind.Hazard && _selIndex == i;
                Color c = new Color(1f, 0.35f, 0.3f, selected ? 1f : 0.8f);
                Handles.color = c;

                Vector2 s = WorldToScreen(def.Position);
                float r = Mathf.Max(4f, def.Size * 0.5f * _pixelsPerUnit);
                Handles.DrawWireDisc(s, Vector3.forward, r);

                // Patrol range.
                Handles.color = new Color(c.r, c.g, c.b, 0.4f);
                Vector2 end = WorldToScreen(def.Position + def.PatrolTravel);
                Handles.DrawDottedLine(s, end, 3f);

                DrawHandleDot(s, selected, c);
                GUI.Label(new Rect(s.x + 8f, s.y - 8f, 60f, 16f), "Оп" + i);
            }
        }

        private void DrawStartGoal()
        {
            // Start.
            bool startSel = _selKind == SelKind.Start;
            Color sc = new Color(0.4f, 1f, 0.5f, 1f);
            Vector2 ss = WorldToScreen(_level.Start);
            Handles.color = sc;
            Handles.DrawWireDisc(ss, Vector3.forward, 8f);
            DrawHandleDot(ss, startSel, sc);
            GUI.Label(new Rect(ss.x + 8f, ss.y - 8f, 60f, 16f), "старт");

            // Goal + radius.
            bool goalSel = _selKind == SelKind.Goal;
            Color gc = new Color(1f, 0.9f, 0.3f, 1f);
            Vector2 gs = WorldToScreen(_level.Goal);
            Handles.color = gc;
            Handles.DrawWireDisc(gs, Vector3.forward, _level.GoalRadius * _pixelsPerUnit);
            DrawHandleDot(gs, goalSel, gc);
            GUI.Label(new Rect(gs.x + 8f, gs.y - 8f, 60f, 16f), "цель");
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
        private int _dragPointIndex = -1;

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
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        Vector2 local = e.mousePosition - rect.position;
                        if (e.alt && _selKind == SelKind.Stream &&
                            _level.Streams[_selIndex].UsesCustomPoints)
                        {
                            AddPointToSelected(local);
                        }
                        else if (TryPickPoint(local, out _dragPointIndex))
                        {
                            _dragging = true; // dragging a waypoint
                        }
                        else
                        {
                            _dragPointIndex = -1;
                            SelectAt(local);
                            _dragging = _selKind != SelKind.None;
                        }
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 && _dragging && _dragPointIndex >= 0)
                    {
                        DragPoint(e.delta);
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 0 && _dragging)
                    {
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
                    _dragging = false;
                    _dragPointIndex = -1;
                    break;
            }
        }

        private void SelectAt(Vector2 screenInCanvas)
        {
            const float pickRadius = 12f;
            float best = pickRadius;
            _selKind = SelKind.None;
            _selIndex = -1;

            void Consider(Vector2 worldPos, SelKind kind, int index)
            {
                float d = Vector2.Distance(WorldToScreen(worldPos), screenInCanvas);
                if (d < best)
                {
                    best = d;
                    _selKind = kind;
                    _selIndex = index;
                }
            }

            Consider(_level.Start, SelKind.Start, -1);
            Consider(_level.Goal, SelKind.Goal, -1);
            if (_level.Hazards != null)
                for (int i = 0; i < _level.Hazards.Length; i++)
                    Consider(_level.Hazards[i].Position, SelKind.Hazard, i);

            // A point element (start/goal/hazard) right under the cursor wins. Otherwise
            // fall through to streams, which pick on the whole path (any point along the
            // band), not just the origin handle — much easier to hit. Tolerance grows
            // with the band width.
            if (_selKind == SelKind.None && _level.Streams != null)
            {
                float bestStream = float.MaxValue;
                for (int i = 0; i < _level.Streams.Length; i++)
                {
                    float tol = pickRadius + _level.Streams[i].Width * 0.5f * _pixelsPerUnit;
                    float d = DistanceToStream(_level.Streams[i], screenInCanvas);
                    if (d <= tol && d < bestStream)
                    {
                        bestStream = d;
                        _selKind = SelKind.Stream;
                        _selIndex = i;
                    }
                }
            }
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
            if (_selKind != SelKind.Stream) return false;
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

        private void DragSelected(Vector2 screenDelta)
        {
            Vector2 worldDelta = new Vector2(screenDelta.x, -screenDelta.y) / _pixelsPerUnit;
            Undo.RecordObject(_level, "Двигать элемент");
            switch (_selKind)
            {
                case SelKind.Start: _level.Start += worldDelta; break;
                case SelKind.Goal: _level.Goal += worldDelta; break;
                case SelKind.Hazard: _level.Hazards[_selIndex].Position += worldDelta; break;
                case SelKind.Stream: _level.Streams[_selIndex].Position += worldDelta; break;
            }
            EditorUtility.SetDirty(_level);
        }

        // ── Inspector ─────────────────────────────────────────────────────────

        // Deferred inspector action: button handlers must not mutate the array or
        // exit early mid-layout (it desyncs the GUIClip/ScrollView stack). They set
        // this instead; it runs after the layout groups are closed.
        private enum PendingAction { None, ConvertToPoints, ClearPoints, DeleteStream, DeleteHazard }
        private PendingAction _pending;
        private int _pendingIndex;

        private void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            _serialized.Update();
            EditorGUI.BeginChangeCheck();

            switch (_selKind)
            {
                case SelKind.Stream: DrawStreamInspector(); break;
                case SelKind.Hazard: DrawHazardInspector(); break;
                default: DrawLevelInspector(); break;
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
        }

        private void RunPendingAction()
        {
            switch (_pending)
            {
                case PendingAction.ConvertToPoints: ConvertToCustomPoints(_pendingIndex); break;
                case PendingAction.ClearPoints: ClearCustomPoints(_pendingIndex); break;
                case PendingAction.DeleteStream: DeleteStream(_pendingIndex); break;
                case PendingAction.DeleteHazard: DeleteHazard(_pendingIndex); break;
            }
            _pending = PendingAction.None;
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
                EditorGUILayout.PropertyField(s.FindPropertyRelative("Shape"),
                    new GUIContent("Форма", "Тип параметрической траектории потока (линия, дуга, круг, спираль и т.д.)."));
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
            FloatRow(s.FindPropertyRelative("Speed"), "Скорость",
                "Скорость течения потока — как быстро он несёт Икара вдоль траектории.", 0.5f, 0f);
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
            _selKind = SelKind.None;
            _selIndex = -1;
            // Deliberately NOT selecting the asset in the Project window: a selected
            // asset turns the Delete key into "delete asset file", which would nuke
            // the level when the user only meant to delete a stream/hazard.
            RebuildSerialized();
            FrameAll();
        }

        // ── Mutations ─────────────────────────────────────────────────────────

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
            _selKind = SelKind.Stream;
            _selIndex = list.Count - 1;
            Commit();
        }

        private void AddCircle()
        {
            Undo.RecordObject(_level, "Добавить круговой поток");
            var list = new List<StreamDef>(_level.Streams ?? new StreamDef[0]);
            list.Add(new StreamDef { Position = _panWorld, IsCircle = true, CircleRadius = 4f });
            _level.Streams = list.ToArray();
            _selKind = SelKind.Stream;
            _selIndex = list.Count - 1;
            Commit();
        }

        private void AddHazard()
        {
            Undo.RecordObject(_level, "Добавить препятствие");
            var list = new List<HazardDef>(_level.Hazards ?? new HazardDef[0]);
            list.Add(new HazardDef { Position = _panWorld });
            _level.Hazards = list.ToArray();
            _selKind = SelKind.Hazard;
            _selIndex = list.Count - 1;
            Commit();
        }

        private void ConvertToCustomPoints(int index)
        {
            Undo.RecordObject(_level, "Превратить поток в путь");
            StreamDef def = _level.Streams[index];
            List<Vector2> pts = StreamShapeBuilder.Build(def.Shape, def.Size, def.Size2,
                def.Count, def.Turns, def.Seed, out bool loop);
            if (def.Reverse) pts.Reverse();
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
            _selKind = SelKind.None;
            Commit();
        }

        private void DeleteHazard(int index)
        {
            Undo.RecordObject(_level, "Удалить препятствие");
            var list = new List<HazardDef>(_level.Hazards);
            list.RemoveAt(index);
            _level.Hazards = list.ToArray();
            _selKind = SelKind.None;
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
