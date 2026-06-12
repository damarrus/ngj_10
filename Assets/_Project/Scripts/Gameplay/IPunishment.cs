using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Shared references a punishment needs to affect the game.</summary>
    public class PunishmentContext
    {
        public PlayerController Player;
        public Camera Camera;
        public MonoBehaviour CoroutineHost;   // for StartCoroutine
        public Transform EffectsParent;       // where spawned effect objects live

        public PunishmentPrefabs Prefabs;
    }

    /// <summary>Prefabs / sprites used by punishments, set up on the manager.</summary>
    [System.Serializable]
    public class PunishmentPrefabs
    {
        public GameObject Meteor;        // meteor with its own fall+shadow logic
        public GameObject Lightning;     // lightning bolt projectile
        public GameObject Puddle;        // slowing puddle
        public GameObject DarknessOverlay; // black overlay with a hole
        public GameObject WindStream;    // drifting wind streaks visual
    }

    /// <summary>
    /// A timed punishment. Begin once, Tick every frame while active, End when its
    /// duration elapses. Implementations are plain C# (not MonoBehaviours) and spawn
    /// their own scene objects via the context.
    /// </summary>
    public interface IPunishment
    {
        void Begin(PunishmentContext ctx);
        void Tick(float dt);
        void End();
    }
}
