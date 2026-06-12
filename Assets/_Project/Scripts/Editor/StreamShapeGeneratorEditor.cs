#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Ngj10.Gameplay;

namespace Ngj10.EditorTools
{
    /// <summary>
    /// Per-shape inspector for StreamShapeGenerator: shows only the parameters
    /// the chosen shape uses (with meaningful labels) and rebuilds waypoints on
    /// change when auto-regenerate is on.
    /// </summary>
    [CustomEditor(typeof(StreamShapeGenerator))]
    public class StreamShapeGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var gen = (StreamShapeGenerator)target;
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_shape"));
            var shape = (StreamShape)serializedObject.FindProperty("_shape").enumValueIndex;

            switch (shape)
            {
                case StreamShape.Line:
                    Field("_size", "Length");
                    break;
                case StreamShape.Arc:
                    Field("_size", "Radius");
                    Field("_turns", "Sweep (degrees)");
                    break;
                case StreamShape.Circle:
                    Field("_size", "Radius");
                    break;
                case StreamShape.Ellipse:
                    Field("_size", "Radius X");
                    Field("_size2", "Radius Y");
                    break;
                case StreamShape.SineWave:
                    Field("_size", "Length");
                    Field("_size2", "Amplitude");
                    Field("_count", "Periods");
                    break;
                case StreamShape.Zigzag:
                    Field("_size", "Length");
                    Field("_size2", "Amplitude");
                    Field("_count", "Zigs");
                    break;
                case StreamShape.Spiral:
                    Field("_size", "End Radius");
                    Field("_size2", "Start Radius");
                    Field("_turns", "Turns");
                    break;
                case StreamShape.FigureEight:
                    Field("_size", "Half Width");
                    Field("_size2", "Height");
                    break;
                case StreamShape.SCurve:
                case StreamShape.Hill:
                case StreamShape.Valley:
                    Field("_size", "Length");
                    Field("_size2", "Height");
                    break;
                case StreamShape.LoopTheLoop:
                    Field("_size", "Length");
                    Field("_size2", "Loop Radius");
                    break;
                case StreamShape.Corkscrew:
                    Field("_size", "Length");
                    Field("_size2", "Loop Radius");
                    Field("_count", "Loops");
                    break;
                case StreamShape.Stairs:
                    Field("_size", "Length");
                    Field("_size2", "Total Rise");
                    Field("_count", "Steps");
                    break;
                case StreamShape.Star:
                    Field("_size", "Base Radius");
                    Field("_size2", "Petal Depth");
                    Field("_count", "Petals");
                    break;
                case StreamShape.NoisePath:
                    Field("_size", "Length");
                    Field("_size2", "Amplitude");
                    Field("_count", "Frequency");
                    Field("_seed", "Seed");
                    break;
                case StreamShape.RoundedRect:
                    Field("_size", "Width");
                    Field("_size2", "Height");
                    break;
                case StreamShape.Heart:
                    Field("_size", "Width");
                    break;
            }

            Field("_reverse", "Reverse Direction");
            Field("_autoRegenerate", "Auto Regenerate");

            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if ((changed && gen.AutoRegenerate) || GUILayout.Button("Generate Waypoints"))
            {
                gen.Generate();
                EditorUtility.SetDirty(gen.gameObject);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            }

            EditorGUILayout.HelpBox(
                "Формы строятся в локальных координатах: двигай/вращай сам объект потока, чтобы поставить в уровень. Точки после генерации можно дотащить руками.",
                MessageType.Info);
        }

        private void Field(string prop, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(prop), new GUIContent(label));
        }
    }
}
#endif
