#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ngj10.Gameplay;

namespace Ngj10.EditorTools
{
    /// <summary>
    /// Scene View editing for StreamPath: drag waypoints directly, "+" buttons
    /// on segment midpoints insert a point, "x" buttons above points delete.
    /// </summary>
    [CustomEditor(typeof(StreamPath))]
    public class StreamPathEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            var path = (StreamPath)target;
            if (GUILayout.Button("Add Waypoint At End"))
            {
                var points = GetWaypoints(path);
                Vector3 pos = points.Count == 0
                    ? path.transform.position
                    : points[points.Count - 1].position + Vector3.right * 2f;
                CreateWaypoint(path, pos, points.Count);
            }
            EditorGUILayout.HelpBox(
                "Scene View: тащи точки за кружки, '+' на сегменте — вставить точку, '×' над точкой — удалить.",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var path = (StreamPath)target;
            var points = GetWaypoints(path);
            if (points.Count == 0) return;

            // Drag handles on every waypoint.
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 pos = points[i].position;
                float size = HandleUtility.GetHandleSize(pos) * 0.12f;

                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(pos, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(points[i], "Move Stream Waypoint");
                    moved.z = 0f;
                    points[i].position = moved;
                }

                Handles.Label(pos + Vector3.up * size * 2.5f, i.ToString());

                // Delete button above the point (keep at least 2 points).
                if (points.Count > 2)
                {
                    Vector3 delPos = pos + Vector3.up * size * 5f;
                    Handles.color = new Color(1f, 0.4f, 0.4f);
                    if (Handles.Button(delPos, Quaternion.identity, size * 0.8f, size, Handles.DotHandleCap))
                    {
                        Undo.DestroyObjectImmediate(points[i].gameObject);
                        RenumberWaypoints(path);
                        return;
                    }
                }
            }

            // Insert buttons on segment midpoints.
            int segments = path.Loop ? points.Count : points.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = points[i].position;
                Vector3 b = points[(i + 1) % points.Count].position;
                Vector3 mid = (a + b) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid) * 0.1f;

                Handles.color = new Color(0.4f, 1f, 0.5f);
                if (Handles.Button(mid, Quaternion.identity, size, size * 1.4f, Handles.CubeHandleCap))
                {
                    var wp = CreateWaypoint(path, mid, points[i].GetSiblingIndex() + 1);
                    wp.transform.SetSiblingIndex(points[i].GetSiblingIndex() + 1);
                    RenumberWaypoints(path);
                    return;
                }
            }
        }

        private static List<Transform> GetWaypoints(StreamPath path)
        {
            var list = new List<Transform>();
            foreach (Transform child in path.transform)
                if (child.name.StartsWith("Waypoint"))
                    list.Add(child);
            return list;
        }

        private static GameObject CreateWaypoint(StreamPath path, Vector3 pos, int index)
        {
            var wp = new GameObject("Waypoint" + index);
            Undo.RegisterCreatedObjectUndo(wp, "Add Stream Waypoint");
            wp.transform.SetParent(path.transform, false);
            pos.z = 0f;
            wp.transform.position = pos;
            return wp;
        }

        private static void RenumberWaypoints(StreamPath path)
        {
            int n = 0;
            foreach (Transform child in path.transform)
                if (child.name.StartsWith("Waypoint"))
                    child.name = "Waypoint" + n++;
        }
    }
}
#endif
