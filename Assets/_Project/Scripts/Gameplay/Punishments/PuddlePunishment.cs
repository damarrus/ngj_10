using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Spawns several large static puddles that slow the player; removes them on end.</summary>
    public class PuddlePunishment : IPunishment
    {
        private const int Count = 4;
        private const float MinScale = 1.6f;
        private const float MaxScale = 2.6f;

        private PunishmentContext _ctx;
        private readonly List<GameObject> _puddles = new();

        public void Begin(PunishmentContext ctx)
        {
            _ctx = ctx;
            if (_ctx.Prefabs?.Puddle == null || _ctx.Camera == null) return;

            float halfH = _ctx.Camera.orthographicSize;
            float halfW = halfH * _ctx.Camera.aspect;
            Vector2 c = _ctx.Camera.transform.position;
            float m = 1.5f;

            for (int i = 0; i < Count; i++)
            {
                var pos = new Vector2(
                    Random.Range(c.x - halfW + m, c.x + halfW - m),
                    Random.Range(c.y - halfH + m, c.y + halfH - m));
                var go = Object.Instantiate(_ctx.Prefabs.Puddle, _ctx.EffectsParent);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * Random.Range(MinScale, MaxScale);
                _puddles.Add(go);
            }
        }

        public void Tick(float dt) { }

        public void End()
        {
            foreach (var p in _puddles)
                if (p != null) Object.Destroy(p);
            _puddles.Clear();
        }
    }
}
