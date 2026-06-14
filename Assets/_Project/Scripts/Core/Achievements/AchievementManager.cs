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
        private static AchievementManager _instance;

        /// <summary>
        /// Persistent instance. Lazy fallback spawns the engine plus the unlock toast
        /// on one GameObject the first time it's needed — covers starting any scene
        /// directly without a Boot scene (common in the Editor). The in-menu browser
        /// (<see cref="AchievementsMenuView"/>) lives on the title panel, not here, so
        /// it isn't part of this stack. Always non-null.
        /// </summary>
        public static AchievementManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Achievements");
                    go.AddComponent<AchievementManager>(); // Awake sets _instance
                    go.AddComponent<AchievementToast>();
                }

                return _instance;
            }
        }

        private const string ProgressPrefix = "ach.progress.";
        private const string UnlockedPrefix = "ach.unlocked.";

        [Tooltip("TEMP off for visual testing: when false, progress/unlocks live only in " +
                 "memory (reset every run) and nothing is read from or written to PlayerPrefs.")]
        [SerializeField] private bool _persist = false;

        // In-memory store used while _persist is off — wiped on every domain reload /
        // play, so the toast and grid can be retriggered fresh each test run.
        private readonly Dictionary<string, int> _memProgress = new Dictionary<string, int>();
        private readonly HashSet<string> _memUnlocked = new HashSet<string>();

        /// <summary>Raised once, the moment an achievement unlocks.</summary>
        public event Action<AchievementDefinition> OnUnlocked;

        private readonly Dictionary<string, AchievementDefinition> _byId =
            new Dictionary<string, AchievementDefinition>();

        public IReadOnlyCollection<AchievementDefinition> All => _byId.Values;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadDefinitions();
        }

        private void LoadDefinitions()
        {
            foreach (var def in Resources.LoadAll<AchievementDefinition>(string.Empty))
            {
                if (!def.Enabled)
                {
                    continue;
                }

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

        public bool IsUnlocked(string id) => _persist
            ? PlayerPrefs.GetInt(UnlockedPrefix + id, 0) == 1
            : _memUnlocked.Contains(id);

        public int GetProgress(string id) => _persist
            ? PlayerPrefs.GetInt(ProgressPrefix + id, 0)
            : (_memProgress.TryGetValue(id, out var v) ? v : 0);

        public bool TryGet(string id, out AchievementDefinition def) => _byId.TryGetValue(id, out def);

        /// <summary>How many achievements are currently unlocked. The leaderboard
        /// reports this as one of the ranked fields.</summary>
        public int UnlockedCount
        {
            get
            {
                int count = 0;
                foreach (var id in _byId.Keys)
                {
                    if (IsUnlocked(id))
                        count++;
                }
                return count;
            }
        }

        /// <summary>Wipe all achievement save data. Handy for testing.</summary>
        public void ResetAll()
        {
            foreach (var id in _byId.Keys)
            {
                PlayerPrefs.DeleteKey(UnlockedPrefix + id);
                PlayerPrefs.DeleteKey(ProgressPrefix + id);
            }
            PlayerPrefs.Save();
            _memProgress.Clear();
            _memUnlocked.Clear();
        }

        // --- Internals -------------------------------------------------------

        private void Unlock(AchievementDefinition def)
        {
            if (IsUnlocked(def.Id))
            {
                return;
            }

            if (_persist)
            {
                PlayerPrefs.SetInt(UnlockedPrefix + def.Id, 1);
                PlayerPrefs.Save();
            }
            else
            {
                _memUnlocked.Add(def.Id);
            }
            Debug.Log($"[Achievements] Unlocked '{def.Id}' — {def.Title}");
            OnUnlocked?.Invoke(def);
        }

        private void SetProgress(string id, int value)
        {
            if (_persist)
            {
                PlayerPrefs.SetInt(ProgressPrefix + id, value);
                PlayerPrefs.Save();
            }
            else
            {
                _memProgress[id] = value;
            }
        }
    }
}
