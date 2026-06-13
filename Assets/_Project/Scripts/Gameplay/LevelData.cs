using System;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A whole level as data: streams, hazards, start/goal/kill geometry.
    /// Edited via the Map Editor window, spawned into the scene by LevelBuilder.
    /// Kept as a small .asset (not baked into the scene) so levels diff and
    /// merge cleanly and several can coexist.
    /// </summary>
    /// <summary>How the camera moves and which screen edges kill the player.</summary>
    public enum LevelMode
    {
        /// <summary>Camera follows freely; only the kill line below is deadly.</summary>
        Free,
        /// <summary>Camera follows upward only (X locked); left/right screen edges are deadly.</summary>
        UpOnly,
        /// <summary>Camera is static; all four screen edges are deadly (one screen of play).</summary>
        SingleScreen,
    }

    [CreateAssetMenu(menuName = "Ngj10/Level", fileName = "Level")]
    public class LevelData : ScriptableObject
    {
        public LevelMode Mode = LevelMode.Free;

        /// <summary>Camera orthographic size (half-height in world units). Drives the
        /// kill walls in UpOnly / SingleScreen modes.</summary>
        public float CameraSize = 5f;

        public Vector2 Start = new Vector2(-9f, 4.5f);
        public Vector2 Goal = new Vector2(12f, 8.5f);
        public float GoalRadius = 1.5f;
        public float KillY = -2.5f;
        public float TimeScale = 1.4f;

        public StreamDef[] Streams = Array.Empty<StreamDef>();
        public HazardDef[] Hazards = Array.Empty<HazardDef>();

        /// <summary>The stream the player respawns into (index into Streams).</summary>
        public int StartStreamIndex;
    }

    /// <summary>
    /// One air stream: placement (position/rotation), the shape generator inputs
    /// that build its waypoints, and the StreamPath runtime parameters.
    /// </summary>
    [Serializable]
    public class StreamDef
    {
        public Vector2 Position;
        public float Rotation;

        /// <summary>
        /// Explicit local-space waypoints. When non-empty these win: the stream
        /// is built from them directly and the shape generator inputs are ignored.
        /// This is the hand-drawn path. Leave empty to use the shape generator.
        /// </summary>
        public Vector2[] CustomPoints = Array.Empty<Vector2>();
        public bool CustomLoop;

        public bool UsesCustomPoints => !IsCircle && CustomPoints != null && CustomPoints.Length >= 2;

        // Circular stream: a closed ring of the given radius. Point count scales with
        // the radius. Wins over custom points and the shape generator when set.
        public bool IsCircle;
        public float CircleRadius = 4f;

        /// <summary>Ring point count for the circle, ~one point per unit of circumference.</summary>
        public int CirclePointCount => Mathf.Clamp(Mathf.RoundToInt(CircleRadius * 4f), 8, 64);

        // Shape generator inputs (used only when CustomPoints is empty).
        public StreamShape Shape = StreamShape.Line;
        public float Size = 8f;
        public float Size2 = 3f;
        public int Count = 3;
        public float Turns = 2f;
        public int Seed;
        public bool Reverse;

        // StreamPath runtime parameters.
        public float Speed = 5f;

        /// <summary>Speed at the end of the path: the flow ramps linearly from
        /// Speed (start) to this value (peak). 0 = constant Speed everywhere.</summary>
        public float SpeedEnd;
        public float Width = 3f;
        public float ActiveDuration;
        public float InactiveDuration;
        public float ReverseInterval;
        public float Turbulence;

        /// <summary>How tightly the stream holds Icarus: pull toward the centerline
        /// and how fast his velocity converges to the flow. 3 = default feel,
        /// 6-10 = rails that survive sharp bends at high speed, 1 = loose river.</summary>
        public float Grip = 3f;

        /// <summary>Exit impulse multiplier: applied to the player's velocity the
        /// moment he folds his wings while carried by this stream. 1 = plain
        /// momentum, 1.5 = catapult feel, &lt;1 = sticky exit.</summary>
        public float ExitBoost = 1f;

        /// <summary>Stream colour is derived from Speed, not authored by hand.</summary>
        public Color VisualColor => SpeedToColor(Speed);

        /// <summary>
        /// Map flow speed to a colour ramp: slow = cool blue, fast = warm orange/red.
        /// Used both for the map-editor preview and the in-game visual.
        /// </summary>
        public static Color SpeedToColor(float speed)
        {
            // 0..12 speed -> hue 0.58 (cyan-blue) down to 0.0 (red).
            float t = Mathf.Clamp01(speed / 12f);
            float hue = Mathf.Lerp(0.58f, 0f, t);
            return Color.HSVToRGB(hue, 0.65f, 1f);
        }
    }

    /// <summary>One patrolling deadly obstacle.</summary>
    [Serializable]
    public class HazardDef
    {
        public Vector2 Position;
        public Vector2 PatrolTravel = new Vector2(0f, 3f);
        public float PatrolPeriod = 3f;
        public float Size = 1f;
    }
}
