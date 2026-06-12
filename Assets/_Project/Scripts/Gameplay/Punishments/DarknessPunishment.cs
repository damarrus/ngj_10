using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Spawns a black overlay with a soft hole that follows the player, so they can
    /// only see a small radius around themselves. Fades in on begin, out on end.
    /// </summary>
    public class DarknessPunishment : IPunishment
    {
        private const float FadeIn = 1.2f;
        private const float MaxAlpha = 1f;

        private PunishmentContext _ctx;
        private GameObject _overlay;
        private SpriteRenderer _sr;
        private float _fade;

        public void Begin(PunishmentContext ctx)
        {
            _ctx = ctx;
            if (_ctx.Prefabs?.DarknessOverlay == null) return;
            _overlay = Object.Instantiate(_ctx.Prefabs.DarknessOverlay, _ctx.EffectsParent);
            _sr = _overlay.GetComponent<SpriteRenderer>();
            SetAlpha(0f);
        }

        public void Tick(float dt)
        {
            if (_overlay == null) return;
            // follow player
            if (_ctx.Player != null)
                _overlay.transform.position = new Vector3(_ctx.Player.Position.x, _ctx.Player.Position.y, _overlay.transform.position.z);

            _fade = Mathf.Min(FadeIn, _fade + dt);
            SetAlpha(Mathf.Lerp(0f, MaxAlpha, _fade / FadeIn));
        }

        public void End()
        {
            if (_overlay != null) Object.Destroy(_overlay);
            _overlay = null;
        }

        private void SetAlpha(float a)
        {
            if (_sr == null) return;
            var c = _sr.color; c.a = a; _sr.color = c;
        }
    }
}
