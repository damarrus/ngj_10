using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Periodically drops meteors aimed at the player's current position.</summary>
    public class MeteorPunishment : IPunishment
    {
        private const float Interval = 2.4f;
        private PunishmentContext _ctx;
        private float _cd;

        public void Begin(PunishmentContext ctx)
        {
            _ctx = ctx;
            _cd = 0.5f; // small initial delay
        }

        public void Tick(float dt)
        {
            if (_ctx.Prefabs?.Meteor == null || _ctx.Player == null) return;
            _cd -= dt;
            if (_cd > 0f) return;
            _cd = Interval;

            var go = Object.Instantiate(_ctx.Prefabs.Meteor, _ctx.EffectsParent);
            var meteor = go.GetComponent<Meteor>();
            if (meteor != null) meteor.Init(_ctx.Player.Position);
        }

        public void End() { }
    }
}
