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
        public string name;
        public int score;
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
            StartCoroutine(SubmitRoutine(score, onDone));
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

        private IEnumerator SubmitRoutine(int score, Action<bool> onDone)
        {
            // PostgREST upsert: POST the row with Prefer: resolution=merge-duplicates
            // so a matching uid (primary key) is updated instead of erroring.
            var url = $"{RestBase()}?on_conflict=uid";
            var body = JsonUtility.ToJson(new ScoreRow
            {
                uid = PlayerIdentity.Uid,
                name = PlayerIdentity.Name,
                score = score,
            });

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
            if (!ok)
            {
                Debug.LogWarning($"[Leaderboard] Submit failed: {req.error} ({req.responseCode})");
            }
            onDone?.Invoke(ok);
        }

        private IEnumerator FetchRoutine(int limit, Action<List<ScoreEntry>> onResult, Action onError)
        {
            var url = $"{RestBase()}?select=name,score&order=score.desc&limit={limit}";

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
        }

        [Serializable]
        private struct ScoreList
        {
            public ScoreEntry[] items;
        }
    }
}
