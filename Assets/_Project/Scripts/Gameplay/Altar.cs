using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A self-contained altar. Around it sits a circular deliver zone (green ring);
    /// above it floats the current task bubble (icon + green progress border), and a
    /// red anger ring with the punishment icon sits higher still.
    ///
    /// Anger (0..max) rises passively over time and falls on completed tasks. When it
    /// fills, the altar's punishment runs for a while, then anger resets. Tasks have no
    /// timer — they stay until fulfilled.
    ///
    /// Task types:
    /// - Resource (Sheep / Log / Berry): hand in N times inside this altar's zone (sheep
    ///   herded in; carryables handed in with E while inside).
    /// - Run: spawns zones on the field; running through each advances progress. Only
    ///   one altar may hold a Run task at a time (via <see cref="CanAssignRun"/>).
    /// </summary>
    public class Altar : MonoBehaviour, IInteractable
    {
        [SerializeField] private PunishmentType _punishment = PunishmentType.Wind;
        [Tooltip("Flame whose intensity tracks this altar's anger.")]
        [SerializeField] private AltarFire _fire;

        [Header("Task")]
        [SerializeField] private int _resourceSteps = 3;
        [SerializeField] private int _runZoneCount = 4;
        [SerializeField] private float _refillPause = 1.5f;

        [Header("Anger / punishment")]
        [SerializeField] private float _angerMax = 100f;
        [SerializeField] private float _angerPerSec = 0.67f;  // passive build-up
        [SerializeField] private float _angerOnDeliver = 10f; // completed a task
        [SerializeField] private float _rageDuration = 27f;

        [Header("Layout (offsets from altar)")]
        [SerializeField] private Vector2 _taskOffset = new Vector2(0f, 1.3f);

        [Header("Deliver zone (circle around altar)")]
        [Tooltip("Radius of the circular deliver zone, centred on the altar.")]
        [SerializeField] private float _zoneRadius = 1.6f;
        [SerializeField] private Color _zoneColor = new Color(0.4f, 0.95f, 0.45f, 0.9f);

        [Header("Run zones")]
        [SerializeField] private Vector2 _runArea = new Vector2(0f, -1.5f);
        [SerializeField] private float _runSpread = 3.2f;

        [Header("Bubbles")]
        [SerializeField] private float _bubbleSize = 0.8f;
        [SerializeField] private Color _bubbleColor = new Color(0.16f, 0.14f, 0.12f, 0.9f);
        [Tooltip("Colour of the bubble border ring, which now fills with this altar's anger (0→100%).")]
        [SerializeField] private Color _angerColor = new Color(0.95f, 0.18f, 0.12f, 1f);

        [Header("Icons")]
        [SerializeField] private Sprite _sheepIcon;
        [SerializeField] private Sprite _logIcon;
        [SerializeField] private Sprite _berryIcon;
        [SerializeField] private Sprite _runIcon;

        /// <summary>A task was completed (counts toward the level goal).</summary>
        public event Action<Altar> Fulfilled;
        /// <summary>Anger filled: start this altar's punishment.</summary>
        public event Action<Altar> RageStarted;
        /// <summary>Rage ended: stop this altar's punishment.</summary>
        public event Action<Altar> RageEnded;

        /// <summary>Set by LevelManager — true if this altar may take a Run task now.</summary>
        public Func<Altar, bool> CanAssignRun;

        public PunishmentType Punishment => _punishment;
        public bool IsRaging { get; private set; }
        public bool HasRunTask => _state == State.Active && _task == TaskType.Run;

        /// <summary>World centre of the deliver zone (the altar itself).</summary>
        public Vector2 ZoneCenter => transform.position;
        /// <summary>Radius of the deliver zone, so spawners can avoid it.</summary>
        public float ZoneRadius => _zoneRadius;

        private enum State { Active, Pause }

        private State _state;
        private TaskType _task;
        private int _progress;
        private int _stepsTotal;
        private float _phaseT;
        private float _anger;
        private float _rageTimer;

        private readonly List<RunZone> _runZones = new();

        private const float Ppu = 100f;
        private RectTransform _taskBoard;
        private Image _bubble, _angerBorder, _icon;
        private SpriteRenderer _zoneSprite;
        private static Sprite _disc, _ring, _zoneRing;

        private void Start()
        {
            if (_fire == null) _fire = GetComponentInChildren<AltarFire>(includeInactive: true);
            EnsureSprites();
            BuildZone();
            BuildTaskBubble();
            BeginTask();
            UpdateAngerRing();
            UpdateFire();
        }

        private void OnDestroy() => ClearRunZones();

        // ---------- task lifecycle ----------
        private void BeginTask()
        {
            _state = State.Active;
            _progress = 0;
            _task = PickTask();
            _stepsTotal = _task == TaskType.Run ? _runZoneCount : _resourceSteps;
            ApplyIcon();
            ShowTask(true);
            if (_task == TaskType.Run) SpawnRunZones();
        }

        private TaskType PickTask()
        {
            bool wantRun = UnityEngine.Random.Range(0, 4) == 0;
            if (wantRun && (CanAssignRun == null || CanAssignRun(this)))
                return TaskType.Run;
            return (TaskType)UnityEngine.Random.Range(0, 3);
        }

        private void CompleteTask()
        {
            ClearRunZones();
            ShowTask(false);
            AddAnger(-_angerOnDeliver);
            Fulfilled?.Invoke(this);
            _state = State.Pause;
            _phaseT = _refillPause;
        }

        private void Step()
        {
            _progress++;
            if (_progress >= _stepsTotal) CompleteTask();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            TickRage(dt);
            if (!IsRaging) AddAnger(_angerPerSec * dt); // passive build-up (paused during rage)

            switch (_state)
            {
                case State.Active:
                    if (_task.IsResource()) CheckResourceDelivery();
                    break;
                case State.Pause:
                    _phaseT -= dt;
                    if (_phaseT <= 0f) BeginTask();
                    break;
            }
        }

        // ---------- resource delivery (own zone) ----------
        private bool InZone(Vector2 world)
        {
            return ((Vector2)transform.position - world).sqrMagnitude <= _zoneRadius * _zoneRadius;
        }

        private void CheckResourceDelivery()
        {
            if (_task != TaskType.Sheep) return;
            foreach (var sheep in FindObjectsByType<Sheep>(FindObjectsSortMode.None))
            {
                if (!InZone(sheep.Position)) continue;
                Destroy(sheep.gameObject);
                Step();
                return;
            }
        }

        // --- IInteractable: hand in a carryable (log/berry) with E in the zone ---
        public Vector2 Position => transform.position;

        public bool CanInteract(PlayerController player)
        {
            if (_state != State.Active || !_task.IsResource() || _task == TaskType.Sheep) return false;
            var item = player.CarriedItem;
            return item != null && item.Type == _task.ToResource() && InZone(player.Position);
        }

        public void Interact(PlayerController player)
        {
            if (!CanInteract(player)) return;
            player.OfferCarriedItem();
            Step();
        }

        // ---------- run zones ----------
        private void SpawnRunZones()
        {
            ClearRunZones();
            for (int i = 0; i < _runZoneCount; i++)
            {
                var go = new GameObject($"RunZone_{name}_{i}");
                go.transform.position = _runArea + UnityEngine.Random.insideUnitCircle * _runSpread;
                var rz = go.AddComponent<RunZone>();
                rz.Entered += OnRunZoneEntered;
                _runZones.Add(rz);
            }
        }

        private void OnRunZoneEntered(RunZone z)
        {
            _runZones.Remove(z);
            if (_state == State.Active && _task == TaskType.Run) Step();
        }

        private void ClearRunZones()
        {
            foreach (var z in _runZones)
                if (z != null) { z.Entered -= OnRunZoneEntered; Destroy(z.gameObject); }
            _runZones.Clear();
        }

        // ---------- anger / rage ----------
        private void AddAnger(float delta)
        {
            if (IsRaging) return; // rage owns the meter while draining
            _anger = Mathf.Clamp(_anger + delta, 0f, _angerMax);
            UpdateAngerRing();
            UpdateFire();
            if (_anger >= _angerMax) StartRage();
        }

        private void StartRage()
        {
            IsRaging = true;
            _rageTimer = _rageDuration;
            RageStarted?.Invoke(this);
        }

        private void TickRage(float dt)
        {
            if (!IsRaging) return;
            _rageTimer -= dt;
            _anger = _angerMax * Mathf.Clamp01(_rageTimer / _rageDuration); // drains visibly
            UpdateAngerRing();
            UpdateFire();
            if (_rageTimer <= 0f)
            {
                IsRaging = false;
                _anger = 0f;
                UpdateAngerRing();
                UpdateFire();
                RageEnded?.Invoke(this);
            }
        }

        // ---------- visuals ----------
        private void EnsureSprites()
        {
            if (_disc == null) _disc = CircleSprite.Build(96, 0.16f);
            if (_ring == null) _ring = BuildRing(128, 0.16f);
            if (_zoneRing == null) _zoneRing = BuildRing(256, 0.045f);
        }

        private void BuildZone()
        {
            var go = new GameObject("DeliverZone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(_zoneRadius * 2f, _zoneRadius * 2f, 1f);
            _zoneSprite = go.AddComponent<SpriteRenderer>();
            _zoneSprite.sprite = _zoneRing;
            _zoneSprite.color = _zoneColor;
            _zoneSprite.sortingOrder = -45;
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;
        }

        private RectTransform NewBoard(string name, Vector2 worldOffset, int order)
        {
            var go = new GameObject(name);
            go.transform.position = (Vector2)transform.position + worldOffset;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(2, 2);
            rt.localScale = Vector3.one / Ppu;
            return rt;
        }

        private void BuildTaskBubble()
        {
            _taskBoard = NewBoard("TaskBubble", _taskOffset, 30);
            float px = _bubbleSize * Ppu;
            _bubble = NewImage("BG", _taskBoard, _disc, _bubbleColor, px);
            NewImage("Idle", _taskBoard, _ring, new Color(1f, 1f, 1f, 0.18f), px);
            // The bubble border is this altar's anger meter: red radial fill, 0→100%.
            _angerBorder = NewImage("AngerBorder", _taskBoard, _ring, _angerColor, px);
            _angerBorder.type = Image.Type.Filled;
            _angerBorder.fillMethod = Image.FillMethod.Radial360;
            _angerBorder.fillOrigin = (int)Image.Origin360.Top;
            _angerBorder.fillClockwise = true;
            _angerBorder.fillAmount = 0f;
            _icon = NewImage("Icon", _taskBoard, null, Color.white, px * 0.58f);
            ShowTask(false);
        }

        private static Image NewImage(string name, RectTransform parent, Sprite spr, Color col, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = spr; img.color = col; img.raycastTarget = false;
            return img;
        }

        private void ShowTask(bool on) { if (_taskBoard != null) _taskBoard.gameObject.SetActive(on); }

        private void UpdateAngerRing()
        {
            if (_angerBorder != null) _angerBorder.fillAmount = Mathf.Clamp01(_anger / _angerMax);
        }

        private void UpdateFire()
        {
            if (_fire != null) _fire.SetIntensity(Mathf.Clamp01(_anger / _angerMax));
        }

        private void ApplyIcon()
        {
            if (_icon == null) return;
            _icon.sprite = _task switch
            {
                TaskType.Sheep => _sheepIcon,
                TaskType.Log => _logIcon,
                TaskType.Berry => _berryIcon,
                _ => _runIcon != null ? _runIcon : (_runFallback ??= BuildRunIcon(96)),
            };
            _icon.enabled = _icon.sprite != null;
        }

        private static Sprite _runFallback;

        private static Sprite BuildRunIcon(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) tex.SetPixel(x, y, new Color(0, 0, 0, 0));
            float thick = size * 0.12f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fy = Mathf.Abs(y - size * 0.5f);
                foreach (float cx in new[] { size * 0.32f, size * 0.56f })
                {
                    float edge = cx + fy * 0.9f;
                    if (Mathf.Abs(x - edge) <= thick && y > size * 0.12f && y < size * 0.88f)
                        tex.SetPixel(x, y, Color.white);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite BuildRing(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f, outer = r - 1f, inner = outer * (1f - thickness * 2f);
            float soft = Mathf.Max(1f, outer * 0.05f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((outer - d) / soft) * Mathf.Clamp01((d - inner) / soft);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
