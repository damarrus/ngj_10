using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Core.Achievements
{
    /// <summary>
    /// Reusable achievement engine. Persistent singleton: tracks progress, saves
    /// to PlayerPrefs (WebGL-safe — maps to IndexedDB) and raises
    /// <see cref="OnUnlocked"/> when one fires. UI (toast, list) just subscribes;
    /// gameplay just calls Report/ReportMax/Unlock by id.
    ///
    /// Definitions are loaded automatically from any "Resources" folder via
    /// <c>Resources.LoadAll</c>, so no inspector wiring is required — this is the
    /// part we reuse on every jam.
    /// </summary>
    public class AchievementManager : MonoBehaviour
    {
        public static AchievementManager Instance { get; private set; }

        private const string ProgressPrefix = "ach.progress.";
        private const string UnlockedPrefix = "ach.unlocked.";

        /// <summary>Raised once, the moment an achievement unlocks.</summary>
        public event Action<AchievementDefinition> OnUnlocked;

        private readonly Dictionary<string, AchievementDefinition> _byId =
            new Dictionary<string, AchievementDefinition>();

        public IReadOnlyCollection<AchievementDefinition> All => _byId.Values;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadDefinitions();
        }

        private void LoadDefinitions()
        {
            foreach (var def in Resources.LoadAll<AchievementDefinition>(string.Empty))
            {
                if (string.IsNullOrEmpty(def.Id))
                {
                    Debug.LogError($"[Achievements] Definition '{def.name}' has empty Id; skipped.", def);
                    continue;
                }

                if (!_byId.TryAdd(def.Id, def))
                {
                    Debug.LogError($"[Achievements] Duplicate id '{def.Id}' on '{def.name}'; skipped.", def);
                }
            }

            Debug.Log($"[Achievements] Loaded {_byId.Count} definition(s).");
        }

        // --- Public reporting API (call these from gameplay) -----------------

        /// <summary>Add to a Counter achievement, or fire a Single one.</summary>
        public void Report(string id, int amount = 1)
        {
            if (!TryGet(id, out var def) || IsUnlocked(id))
            {
                return;
            }

            switch (def.Type)
            {
                case AchievementType.Single:
                    Unlock(def);
                    break;
                case AchievementType.Counter:
                    int total = GetProgress(id) + amount;
                    SetProgress(id, total);
                    if (total >= def.Target)
                    {
                        Unlock(def);
                    }
                    break;
                case AchievementType.SingleGameMax:
                    // Treat Report as a delta isn't meaningful here; prefer ReportMax.
                    Debug.LogWarning($"[Achievements] '{id}' is SingleGameMax; use ReportMax.");
                    break;
            }
        }

        /// <summary>Report the best value seen in a single game (e.g. final score).</summary>
        public void ReportMax(string id, int value)
        {
            if (!TryGet(id, out var def) || IsUnlocked(id))
            {
                return;
            }

            if (def.Type != AchievementType.SingleGameMax)
            {
                Debug.LogWarning($"[Achievements] '{id}' is not SingleGameMax; use Report.");
                return;
            }

            if (value > GetProgress(id))
            {
                SetProgress(id, value);
            }

            if (value >= def.Target)
            {
                Unlock(def);
            }
        }

        /// <summary>Force-unlock by id (e.g. a story beat). No-op if already unlocked.</summary>
        public void Unlock(string id)
        {
            if (TryGet(id, out var def))
            {
                Unlock(def);
            }
        }

        // --- Queries (for UI) ------------------------------------------------

        public bool IsUnlocked(string id) => PlayerPrefs.GetInt(UnlockedPrefix + id, 0) == 1;

        public int GetProgress(string id) => PlayerPrefs.GetInt(ProgressPrefix + id, 0);

        public bool TryGet(string id, out AchievementDefinition def) => _byId.TryGetValue(id, out def);

        /// <summary>Wipe all achievement save data. Handy for testing.</summary>
        public void ResetAll()
        {
            foreach (var id in _byId.Keys)
            {
                PlayerPrefs.DeleteKey(UnlockedPrefix + id);
                PlayerPrefs.DeleteKey(ProgressPrefix + id);
            }
            PlayerPrefs.Save();
        }

        // --- Internals -------------------------------------------------------

        private void Unlock(AchievementDefinition def)
        {
            if (IsUnlocked(def.Id))
            {
                return;
            }

            PlayerPrefs.SetInt(UnlockedPrefix + def.Id, 1);
            PlayerPrefs.Save();
            Debug.Log($"[Achievements] Unlocked '{def.Id}' — {def.Title}");
            OnUnlocked?.Invoke(def);
        }

        private void SetProgress(string id, int value)
        {
            PlayerPrefs.SetInt(ProgressPrefix + id, value);
            PlayerPrefs.Save();
        }
    }
}
