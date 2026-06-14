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

            Create("WingsSpread", "wings_spread", "Расправить крылья",
                "Соверши свой первый полёт.", AchievementType.Single, 1);

            Create("FirstDeath", "first_death", "В первый раз?",
                "Погибни в первый раз.", AchievementType.Single, 1);

            Create("Walk100", "walk_100", "Лёгкая прогулка",
                "Пролети 100 метров за один полёт.", AchievementType.SingleGameMax, 100);

            Create("Walk1000", "walk_1000", "Длинный путь",
                "Пролети 1000 метров за один полёт.", AchievementType.SingleGameMax, 1000);

            Create("Height100", "height_100", "Новая вершина",
                "Достигни высоты 100 метров.", AchievementType.SingleGameMax, 100);

            Create("Height200", "height_200", "Через тернии к звёздам",
                "Достигни высоты 200 метров.", AchievementType.SingleGameMax, 200);

            Create("Death10", "death_10", "Ты пытался",
                "Погибни 10 раз.", AchievementType.Counter, 10);

            Create("ReachSun", "reach_sun", "ИКАР",
                "Достигни солнца.", AchievementType.Single, 1);

            Create("RestartPress", "restart_press", "Давай по новой, Миша…",
                "Нажми кнопку рестарта.", AchievementType.Single, 1);

            Create("MaxSpeed", "max_speed", "Быстрее ветра",
                "Достигни максимальной скорости.", AchievementType.Single, 1);

            Create("Burn", "burn", "Сгорел на работе",
                "Сгори от лучей солнца.", AchievementType.Single, 1);

            Create("Shock", "shock", "Гром и молния",
                "Попади под молнии Зевса.", AchievementType.Single, 1);

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
