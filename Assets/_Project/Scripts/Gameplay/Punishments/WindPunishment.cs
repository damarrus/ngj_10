using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Pushes the player constantly in one random direction, with a visible flow.</summary>
    public class WindPunishment : IPunishment
    {
        private const float Strength = 1.47f;
        private PunishmentContext _ctx;
        private GameObject _stream;

        public void Begin(PunishmentContext ctx)
        {
            _ctx = ctx;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _ctx.Player?.SetExternalVelocity(dir * Strength);

            if (_ctx.Prefabs?.WindStream != null)
            {
                _stream = Object.Instantiate(_ctx.Prefabs.WindStream, _ctx.EffectsParent);
                var ws = _stream.GetComponent<WindStream>();
                if (ws != null) ws.Init(dir, _ctx.Camera);
            }
        }

        public void Tick(float dt) { }

        public void End()
        {
            _ctx.Player?.ClearExternalVelocity();
            if (_stream != null) Object.Destroy(_stream);
            _stream = null;
        }
    }
}
