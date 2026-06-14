using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Ngj10.Core.Leaderboard
{
    /// <summary>One leaderboard row as returned by the server.</summary>
    [Serializable]
    public struct ScoreEntry
    {
        public string uid;  // server row owner — lets the board spot the local player
        public string name;

        // Legacy single-number score (the original Game scene's board). Kept so that
        // older callers compile; the Icarus board ignores it in favour of the three
        // fields below. Reused server-side as the max-height column (see SubmitRun).
        public int score;

        public int max_height;   // peak height, whole metres (rounded up) — primary sort (desc)
        public int time_to_max;  // milliseconds from takeoff to that peak — tie-break (asc)
        public int achievements; // unlocked achievement count — second tie-break (desc)
    }

    /// <summary>
    /// Talks to the Supabase REST API over <see cref="UnityWebRequest"/> (WebGL-safe,
    /// no SDK). Two operations: submit the local player's score (upsert by uid, so
    /// each player owns one row) and fetch the top N. Everything is best-effort —
    /// on no network / server error the callbacks report failure and the UI simply
    /// hides the board, leaving the bare score.
    ///
    /// Persistent lazy singleton (same pattern as the other managers) so callers
    /// get a running MonoBehaviour to host the coroutines without inspector wiring.
    /// </summary>
    public class LeaderboardClient : MonoBehaviour
    {
        private const string ConfigResourcePath = "LeaderboardConfig";

        // Set once a submit succeeds — local proof the player owns a server row, so the
        // menu can offer rename without asking the server (see HasRecord).
        private const string SubmittedKey = "lb.submitted";

        private static LeaderboardClient _instance;

        public static LeaderboardClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LeaderboardClient");
                    go.AddComponent<LeaderboardClient>(); // Awake sets _instance
                }
                return _instance;
            }
        }

        private LeaderboardConfig _config;

        /// <summary>True when a project URL + key are present. UI checks this to
        /// decide whether the board is even attemptable (offline build = no board).</summary>
        public bool IsAvailable => _config != null && _config.IsConfigured;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _config = Resources.Load<LeaderboardConfig>(ConfigResourcePath);
            if (_config == null)
            {
                Debug.LogWarning($"[Leaderboard] No '{ConfigResourcePath}' asset in Resources — leaderboard disabled.");
            }
        }

        /// <summary>
        /// Upsert the player's score by uid. <paramref name="onDone"/> gets true on
        /// success, false on any failure. Null-safe to call when not configured.
        /// </summary>
        public void SubmitScore(int score, Action<bool> onDone = null)
        {
            if (!IsAvailable)
            {
                onDone?.Invoke(false);
                return;
            }
            StartCoroutine(SubmitRoutine(new ScoreRow
            {
                uid = PlayerIdentity.Uid,
                name = PlayerIdentity.Name,
                score = score,
                max_height = score,
                time_to_max = 0,
                achievements = 0,
            }, onDone));
        }

        /// <summary>
        /// Upsert the player's Icarus run by uid: peak height (whole metres), time
        /// to that peak (seconds) and unlocked-achievement count. The caller owns the
        /// "is this actually a new record" decision (see LeaderboardReporter) — this
        /// just writes the row. <paramref name="onDone"/> gets true on success.
        /// </summary>
        public void SubmitRun(int maxHeight, int timeToMaxMs, int achievements, Action<bool> onDone = null)
        {
            if (!IsAvailable)
            {
                onDone?.Invoke(false);
                return;
            }
            StartCoroutine(SubmitRoutine(new ScoreRow
            {
                uid = PlayerIdentity.Uid,
                name = PlayerIdentity.Name,
                score = maxHeight, // legacy column mirrors height so the old board still sorts
                max_height = maxHeight,
                time_to_max = timeToMaxMs,
                achievements = achievements,
            }, onDone));
        }

        /// <summary>
        /// Fetch the top <paramref name="limit"/> scores, highest first.
        /// <paramref name="onResult"/> on success, <paramref name="onError"/> on
        /// any failure (no network, bad response, not configured).
        /// </summary>
        public void FetchTop(int limit, Action<List<ScoreEntry>> onResult, Action onError = null)
        {
            if (!IsAvailable)
            {
                onError?.Invoke();
                return;
            }
            StartCoroutine(FetchRoutine(limit, onResult, onError));
        }

        /// <summary>
        /// True (via callback) when the local player already owns a server row — the
        /// gate for offering a rename. Cheap path first: a successful submit sets a
        /// local flag, so a returning player answers without any network. Otherwise it
        /// asks the server by uid (HEAD-style count) — the menu needs that round-trip
        /// anyway when the player is outside the fetched top window. Best-effort: any
        /// failure (offline, not configured) answers false.
        /// </summary>
        public void HasRecord(Action<bool> onResult)
        {
            if (PlayerPrefs.GetInt(SubmittedKey, 0) == 1)
            {
                onResult?.Invoke(true);
                return;
            }
            if (!IsAvailable)
            {
                onResult?.Invoke(false);
                return;
            }
            StartCoroutine(HasRecordRoutine(onResult));
        }

        /// <summary>
        /// Change only the display name on the player's existing row (PATCH name by
        /// uid) — leaves height/time/achievements untouched. Persists the new name in
        /// <see cref="PlayerIdentity"/> on success. <paramref name="onDone"/> reports
        /// success. No-op (false) when not configured or the name is blank.
        /// </summary>
        public void Rename(string newName, Action<bool> onDone = null)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(newName))
            {
                onDone?.Invoke(false);
                return;
            }
            StartCoroutine(RenameRoutine(newName.Trim(), onDone));
        }

        private IEnumerator HasRecordRoutine(Action<bool> onResult)
        {
            var url = $"{RestBase()}?select=uid&uid=eq.{UnityWebRequest.EscapeURL(PlayerIdentity.Uid)}&limit=1";
            using var req = UnityWebRequest.Get(url);
            ApplyHeaders(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onResult?.Invoke(false);
                yield break;
            }

            // Non-empty JSON array ("[{...}]") = a row exists. Cache it on a hit.
            bool exists = req.downloadHandler.text.Contains("\"uid\"");
            if (exists)
            {
                PlayerPrefs.SetInt(SubmittedKey, 1);
                PlayerPrefs.Save();
            }
            onResult?.Invoke(exists);
        }

        private IEnumerator RenameRoutine(string newName, Action<bool> onDone)
        {
            var url = $"{RestBase()}?uid=eq.{UnityWebRequest.EscapeURL(PlayerIdentity.Uid)}";
            var body = JsonUtility.ToJson(new NameRow { name = newName });

            using var req = new UnityWebRequest(url, "PATCH")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            ApplyHeaders(req);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            var ok = req.result == UnityWebRequest.Result.Success;
            if (ok)
                PlayerIdentity.Rename(newName);
            else
                Debug.LogWarning($"[Leaderboard] Rename failed: {req.error} ({req.responseCode})");
            onDone?.Invoke(ok);
        }

        private IEnumerator SubmitRoutine(ScoreRow row, Action<bool> onDone)
        {
            // PostgREST upsert: POST the row with Prefer: resolution=merge-duplicates
            // so a matching uid (primary key) is updated instead of erroring.
            var url = $"{RestBase()}?on_conflict=uid";
            var body = JsonUtility.ToJson(row);

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            ApplyHeaders(req);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Prefer", "resolution=merge-duplicates");

            yield return req.SendWebRequest();

            var ok = req.result == UnityWebRequest.Result.Success;
            if (ok)
            {
                // A successful upsert means the player now owns a server row. Remember
                // it locally so the menu can offer rename without a round-trip (see
                // HasRecord) — the row exists by definition once this fires.
                PlayerPrefs.SetInt(SubmittedKey, 1);
                PlayerPrefs.Save();
            }
            else
            {
                Debug.LogWarning($"[Leaderboard] Submit failed: {req.error} ({req.responseCode})");
            }
            onDone?.Invoke(ok);
        }

        private IEnumerator FetchRoutine(int limit, Action<List<ScoreEntry>> onResult, Action onError)
        {
            // Sort: max height desc, then fastest climb, then most achievements, then
            // name — exactly the leaderboard ordering spec. `score` is selected too so
            // the legacy Game-scene board (which reads ScoreEntry.score) still works.
            var url = $"{RestBase()}?select=uid,name,score,max_height,time_to_max,achievements" +
                      $"&order=max_height.desc,time_to_max.asc,achievements.desc,name.asc&limit={limit}";

            using var req = UnityWebRequest.Get(url);
            ApplyHeaders(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Leaderboard] Fetch failed: {req.error} ({req.responseCode})");
                onError?.Invoke();
                yield break;
            }

            List<ScoreEntry> entries;
            try
            {
                // JsonUtility can't parse a top-level array — wrap it.
                var wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                entries = new List<ScoreEntry>(JsonUtility.FromJson<ScoreList>(wrapped).items);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Leaderboard] Parse failed: {e.Message}");
                onError?.Invoke();
                yield break;
            }

            onResult?.Invoke(entries);
        }

        private string RestBase() => $"{_config.ProjectUrl}/rest/v1/{_config.Table}";

        private void ApplyHeaders(UnityWebRequest req)
        {
            req.SetRequestHeader("apikey", _config.AnonKey);
            req.SetRequestHeader("Authorization", $"Bearer {_config.AnonKey}");
        }

        [Serializable]
        private struct ScoreRow
        {
            public string uid;
            public string name;
            public int score;
            public int max_height;
            public int time_to_max;
            public int achievements;
        }

        [Serializable]
        private struct ScoreList
        {
            public ScoreEntry[] items;
        }

        [Serializable]
        private struct NameRow
        {
            public string name;
        }
    }
}
