using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Fires bolts from off-screen toward the player's predicted position (with slight
    /// aim jitter). The launch side drifts around a base angle each episode, so volleys
    /// come from a coherent but arbitrary direction rather than pure noise.
    /// </summary>
    public class LightningPunishment : IPunishment
    {
        private const float Interval = 1.65f;
        private const float Lead = 0.35f;       // seconds of player-velocity lead
        private const float AimJitter = 0.6f;   // world units of randomness on the target
        private const float LaunchSpread = 35f; // deg of drift around the base launch angle

        private PunishmentContext _ctx;
        private float _cd;
        private float _baseLaunchAngle;         // deg, fixed-ish source direction this episode
        private Vector2 _prevPlayerPos;

        public void Begin(PunishmentContext ctx)
        {
            _ctx = ctx;
            _cd = 0.4f;
            _baseLaunchAngle = Random.Range(0f, 360f); // arbitrary angle, any direction
            if (_ctx.Player != null) _prevPlayerPos = _ctx.Player.Position;
        }

        public void Tick(float dt)
        {
            if (_ctx.Prefabs?.Lightning == null || _ctx.Player == null || _ctx.Camera == null) return;

            Vector2 playerPos = _ctx.Player.Position;
            Vector2 playerVel = dt > 0f ? (playerPos - _prevPlayerPos) / dt : Vector2.zero;
            _prevPlayerPos = playerPos;

            _cd -= dt;
            if (_cd > 0f) return;
            _cd = Interval;

            Vector2 target = playerPos + playerVel * Lead
                + new Vector2(Random.Range(-AimJitter, AimJitter), Random.Range(-AimJitter, AimJitter));

            Vector2 start = OffscreenStart();
            Vector2 dir = (target - start).normalized;

            var go = Object.Instantiate(_ctx.Prefabs.Lightning, _ctx.EffectsParent);
            var bolt = go.GetComponent<Lightning>();
            if (bolt != null) bolt.Init(start, dir);
        }

        public void End() { }

        // Spawn just outside the screen, on a ring around the view, near the base angle.
        private Vector2 OffscreenStart()
        {
            float halfH = _ctx.Camera.orthographicSize;
            float halfW = halfH * _ctx.Camera.aspect;
            Vector2 c = _ctx.Camera.transform.position;
            float ringR = Mathf.Sqrt(halfW * halfW + halfH * halfH) + 1.5f; // outside any corner

            float ang = (_baseLaunchAngle + Random.Range(-LaunchSpread, LaunchSpread)) * Mathf.Deg2Rad;
            return c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ringR;
        }
    }
}
