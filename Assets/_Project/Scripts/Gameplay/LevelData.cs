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
        public BurnerDef[] Burners = Array.Empty<BurnerDef>();
        public ZeusDef[] Zeuses = Array.Empty<ZeusDef>();

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

        /// <summary>Uniform multiplier on the generated shape — resize the whole figure
        /// without changing its proportions. Size/Size2 define the form, Scale the overall
        /// size. 1 = as authored. Only affects the shape generator (circles use CircleRadius,
        /// hand-drawn paths use their explicit points).</summary>
        public float Scale = 1f;

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

        /// <summary>Capture priority for the Legacy (rails) model: when several streams
        /// cover the player at once, the one with the highest Z captures him. Ties break
        /// toward the deepest coverage. No effect on the Field model. Default 0.</summary>
        public float Z;

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

    /// <summary>How a burn cone moves over time.</summary>
    public enum ConeMotion
    {
        /// <summary>Fixed direction, always on.</summary>
        Static,
        /// <summary>Sweeps around the burner anchor at RotateSpeed.</summary>
        Rotate,
        /// <summary>Blinks on for OnDuration, off for OffDuration.</summary>
        Pulse,
    }

    /// <summary>
    /// One burn cone radiating from a Burner anchor. The cone burns Icarus while
    /// he sits inside the sector (within Length and HalfAngle of the direction).
    /// </summary>
    [Serializable]
    public class ConeDef
    {
        /// <summary>Direction the cone points, degrees (0 = +X). For Rotate this is the start angle.</summary>
        public float Angle;
        /// <summary>Reach of the cone from the anchor, world units.</summary>
        public float Length = 4f;
        /// <summary>Half of the cone's opening, degrees (full spread = 2 * HalfAngle).</summary>
        public float HalfAngle = 15f;

        public ConeMotion Motion = ConeMotion.Static;

        /// <summary>Rotate: sweep speed, degrees/second (sign sets direction).</summary>
        public float RotateSpeed = 60f;

        /// <summary>Pulse: seconds the cone burns each cycle.</summary>
        public float OnDuration = 1.5f;
        /// <summary>Pulse: seconds the cone is dark each cycle.</summary>
        public float OffDuration = 1f;
        /// <summary>Pulse: phase shift in seconds so several cones desync.</summary>
        public float PhaseOffset;
    }

    /// <summary>
    /// A burner: one anchor point emitting any number of burn cones. Place the
    /// point in the editor, then add and tune cones individually.
    /// </summary>
    [Serializable]
    public class BurnerDef
    {
        public Vector2 Position;
        public ConeDef[] Cones = { new ConeDef() };
    }

    /// <summary>
    /// Zeus' lightning node: an anchor that fires bolts into one or more target
    /// areas. Each area runs on its own timer; on a tick a bolt travels from the
    /// anchor to the area over its flight time, and on arrival Icarus standing
    /// inside the area's ellipse has his wings shocked (folded and blocked
    /// briefly). Place the node in the editor and add/tune areas individually.
    /// </summary>
    [Serializable]
    public class ZeusDef
    {
        /// <summary>The anchor — the point bolts launch from.</summary>
        public Vector2 Position;

        public ZeusAreaDef[] Areas = { new ZeusAreaDef() };
    }

    /// <summary>
    /// One strike area for a Zeus node: an ellipse the bolt lands in, plus its
    /// own firing timer. The bolt hits exactly on arrival — Icarus only gets
    /// shocked if he is inside the ellipse the frame the bolt lands.
    /// </summary>
    [Serializable]
    public class ZeusAreaDef
    {
        /// <summary>Area centre, world-space offset from the Zeus anchor.</summary>
        public Vector2 Offset = new Vector2(0f, -3f);

        /// <summary>Horizontal ellipse radius. Equal radii = circle.</summary>
        public float RadiusX = 2f;
        /// <summary>Vertical ellipse radius.</summary>
        public float RadiusY = 2f;

        /// <summary>Seconds between strikes (from one bolt's launch to the next).</summary>
        public float Period = 4f;

        /// <summary>Seconds to wait before the very first strike of this area.</summary>
        public float StartDelay;

        /// <summary>Seconds a bolt takes to travel from the anchor to the area.</summary>
        public float FlightTime = 0.6f;
    }
}
