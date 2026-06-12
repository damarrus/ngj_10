using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Spawns a level from LevelData at startup: instantiates stream and hazard
    /// prefabs, configures them from the data, and hands the start/goal geometry
    /// to the LevelController. The scene holds only the builder, the player, the
    /// camera and the goal marker — the level layout lives in the .asset.
    /// </summary>
    public class LevelBuilder : MonoBehaviour
    {
        [SerializeField] private LevelData _data;
        [SerializeField] private StreamPath _streamPrefab;
        [SerializeField] private Hazard _hazardPrefab;
        [SerializeField] private LevelController _controller;
        [SerializeField] private Transform _goalMarker;

        public LevelData Data => _data;

        private bool _built;

        // GameConfig (exec order -100) builds the chosen level before us. Only
        // build here if it didn't — keeps the builder usable without a GameConfig.
        private void Awake()
        {
            if (!_built)
                BuildNow();
        }

        /// <summary>
        /// Spawn the level from the assigned data. Idempotent: clears any
        /// previously spawned children first, so GameConfig and the Map Editor
        /// can both call it without doubling up the geometry.
        /// </summary>
        public void BuildNow()
        {
            if (_data == null)
            {
                Debug.LogError("LevelBuilder: no LevelData assigned.", this);
                return;
            }

            ClearSpawned();
            _built = true;

            var streams = BuildStreams();
            BuildHazards();
            PlaceGoal();

            StreamPath startStream = streams.Length > 0
                ? streams[Mathf.Clamp(_data.StartStreamIndex, 0, streams.Length - 1)]
                : null;

            if (_controller != null)
                _controller.Configure(_data, startStream);
        }

        public void SetData(LevelData data) => _data = data;

        /// <summary>Destroy all spawned children — used by the editor before a re-preview.</summary>
        public void ClearSpawned()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        private StreamPath[] BuildStreams()
        {
            var result = new StreamPath[_data.Streams.Length];
            for (int i = 0; i < _data.Streams.Length; i++)
            {
                StreamDef def = _data.Streams[i];
                StreamPath stream = Instantiate(_streamPrefab, transform);
                stream.name = "Stream" + i;
                stream.transform.SetPositionAndRotation(
                    def.Position, Quaternion.Euler(0f, 0f, def.Rotation));

                if (def.IsCircle)
                    BuildCircleWaypoints(stream, def);
                else if (def.UsesCustomPoints)
                    BuildCustomWaypoints(stream, def);
                else
                {
                    var generator = stream.GetComponent<StreamShapeGenerator>();
                    generator.Configure(def.Shape, def.Size, def.Size2, def.Count,
                        def.Turns, def.Seed, def.Reverse);
                    generator.Generate();
                }

                // OnEnable cached an empty path (waypoints were added just above) —
                // rebuild now that the children exist.
                stream.RebuildPath();

                stream.Configure(def.Speed, def.Width, def.ActiveDuration,
                    def.InactiveDuration, def.ReverseInterval, def.Turbulence, def.Grip,
                    def.SpeedEnd, def.ExitBoost);

                var visual = stream.GetComponent<StreamFlowVisual>();
                if (visual != null)
                    visual.Configure(def.VisualColor);

                result[i] = stream;
            }
            return result;
        }

        private static void BuildCustomWaypoints(StreamPath stream, StreamDef def)
        {
            stream.SetLoop(def.CustomLoop);
            // Reverse flips waypoint order — the flow direction follows it.
            for (int i = 0; i < def.CustomPoints.Length; i++)
            {
                int src = def.Reverse ? def.CustomPoints.Length - 1 - i : i;
                var wp = new GameObject("Waypoint" + i);
                wp.transform.SetParent(stream.transform, false);
                wp.transform.localPosition = def.CustomPoints[src];
            }
        }

        private static void BuildCircleWaypoints(StreamPath stream, StreamDef def)
        {
            stream.SetLoop(true);
            var pts = StreamShapeBuilder.BuildCircle(def.CircleRadius, def.CirclePointCount, def.Reverse);
            for (int i = 0; i < pts.Count; i++)
            {
                var wp = new GameObject("Waypoint" + i);
                wp.transform.SetParent(stream.transform, false);
                wp.transform.localPosition = pts[i];
            }
        }

        private void BuildHazards()
        {
            foreach (HazardDef def in _data.Hazards)
            {
                Hazard hazard = Instantiate(_hazardPrefab, transform);
                hazard.transform.position = def.Position;
                hazard.transform.localScale = Vector3.one * def.Size;

                var oscillator = hazard.GetComponent<Oscillator>();
                if (oscillator != null)
                    oscillator.Configure(def.PatrolTravel, def.PatrolPeriod);
            }
        }

        private void PlaceGoal()
        {
            if (_goalMarker != null)
                _goalMarker.position = _data.Goal;
        }
    }
}
