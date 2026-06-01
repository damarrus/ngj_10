#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using Ngj10.Core.Achievements;
using UnityEditor;
using UnityEngine;

namespace Ngj10.EditorTools
{
    /// <summary>
    /// One-shot editor helper: creates the example achievement assets for this
    /// game under Resources so the runtime auto-loads them. Run once via
    /// <c>Ngj10 ▸ Achievements ▸ Create Example Assets</c>. Safe to re-run — it
    /// overwrites the same files.
    ///
    /// Throwaway content tool (jam example), not part of the reusable engine.
    /// </summary>
    public static class AchievementSetup
    {
        private const string Dir = "Assets/_Project/Resources/Achievements";

        [MenuItem("Ngj10/Achievements/Create Example Assets")]
        public static void CreateExampleAssets()
        {
            Directory.CreateDirectory(Dir);

            Create("PopFirst", "pop_first", "First Pop!",
                "Pop your very first balloon.", AchievementType.Single, 1);

            Create("PopHundred", "pop_total_100", "Balloon Veteran",
                "Pop 100 balloons across all your games.", AchievementType.Counter, 100);

            Create("ScoreTen", "score_10", "Sharp Shooter",
                "Score 10 points in a single game.", AchievementType.SingleGameMax, 10);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AchievementSetup] Example achievement assets created under " + Dir);
        }

        private static void Create(
            string fileName, string id, string title, string desc,
            AchievementType type, int target)
        {
            string path = $"{Dir}/{fileName}.asset";
            var def = AssetDatabase.LoadAssetAtPath<AchievementDefinition>(path);
            bool isNew = def == null;
            if (isNew)
            {
                def = ScriptableObject.CreateInstance<AchievementDefinition>();
            }

            // Fields are private [SerializeField]; set via SerializedObject so we
            // don't have to widen the engine's API just for this editor tool.
            var so = new SerializedObject(def);
            so.FindProperty("_id").stringValue = id;
            so.FindProperty("_title").stringValue = title;
            so.FindProperty("_description").stringValue = desc;
            so.FindProperty("_type").enumValueIndex = (int)type;
            so.FindProperty("_target").intValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (isNew)
            {
                AssetDatabase.CreateAsset(def, path);
            }
            else
            {
                EditorUtility.SetDirty(def);
            }
        }
    }
}
#endif
