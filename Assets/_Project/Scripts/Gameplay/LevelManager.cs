using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// One level: a delivery goal. Altars are now self-contained — each owns its task,
    /// timer and anger meter. The manager wires them up: it counts fulfilments toward
    /// the goal, runs an altar's punishment while that altar is raging, and enforces
    /// that at most one altar holds a Run task at a time.
    /// </summary>
    [Serializable]
    public class LevelDefinition
    {
        [Tooltip("How many tasks must be fulfilled (across all altars) to clear the level.")]
        public int RequestsToClear = 12;
    }

    public class LevelManager : MonoBehaviour
    {
        [SerializeField] private List<Altar> _altars = new();
        [SerializeField] private PlayerController _player;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _effectsParent;
        [SerializeField] private PunishmentPrefabs _prefabs = new();
        [SerializeField] private List<LevelDefinition> _levels = new();

        /// <summary>Raised when a level's delivery goal is met. Carries the (zero-based) level index.</summary>
        public event Action<int> LevelCleared;

        private PunishmentContext _ctx;
        private readonly Dictionary<Altar, IPunishment> _running = new();
        private int _levelIndex;
        private int _delivered;

        private void Start()
        {
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_camera == null) _camera = Camera.main;

            _ctx = new PunishmentContext
            {
                Player = _player,
                Camera = _camera,
                CoroutineHost = this,
                EffectsParent = _effectsParent != null ? _effectsParent : transform,
                Prefabs = _prefabs,
            };

            foreach (var a in _altars)
            {
                if (a == null) continue;
                a.CanAssignRun = CanAssignRun;
                a.Fulfilled += OnFulfilled;
                a.RageStarted += OnRageStarted;
                a.RageEnded += OnRageEnded;
            }

            _levelIndex = 0;
            _delivered = 0;
        }

        private void OnDestroy()
        {
            foreach (var a in _altars)
            {
                if (a == null) continue;
                a.Fulfilled -= OnFulfilled;
                a.RageStarted -= OnRageStarted;
                a.RageEnded -= OnRageEnded;
            }
            foreach (var p in _running.Values) p.End();
            _running.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (var p in _running.Values) p.Tick(dt);
        }

        /// <summary>Only one altar may hold a Run task at a time.</summary>
        private bool CanAssignRun(Altar asking)
        {
            foreach (var a in _altars)
                if (a != null && a != asking && a.HasRunTask) return false;
            return true;
        }

        private void OnFulfilled(Altar a)
        {
            if (_levelIndex >= _levels.Count) return;
            _delivered++;
            if (_delivered < _levels[_levelIndex].RequestsToClear) return;

            int cleared = _levelIndex;
            LevelCleared?.Invoke(cleared);
            _levelIndex++;
            _delivered = 0;
        }

        private void OnRageStarted(Altar a)
        {
            if (_running.ContainsKey(a)) return;
            var p = PunishmentFactory.Create(a.Punishment);
            if (p == null) return;
            p.Begin(_ctx);
            _running[a] = p;
        }

        private void OnRageEnded(Altar a)
        {
            if (_running.TryGetValue(a, out var p))
            {
                p.End();
                _running.Remove(a);
            }
        }
    }
}
