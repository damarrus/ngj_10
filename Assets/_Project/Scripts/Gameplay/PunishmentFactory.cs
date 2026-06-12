using UnityEngine;

namespace Ngj10.Gameplay
{
    public static class PunishmentFactory
    {
        public static IPunishment Create(PunishmentType type) => type switch
        {
            PunishmentType.Wind => new WindPunishment(),
            PunishmentType.Meteors => new MeteorPunishment(),
            PunishmentType.Darkness => new DarknessPunishment(),
            PunishmentType.Lightning => new LightningPunishment(),
            PunishmentType.Puddles => new PuddlePunishment(),
            _ => null,
        };

        /// <summary>
        /// A one-shot "taste" of a punishment — fired when a task times out, so failing
        /// stings immediately without a full rage. Cheap, self-cleaning effects.
        /// </summary>
        public static void Burst(PunishmentType type, PunishmentContext ctx)
        {
            if (ctx == null) return;
            switch (type)
            {
                case PunishmentType.Wind: BurstWind(ctx); break;
                case PunishmentType.Meteors: BurstMeteor(ctx); break;
                case PunishmentType.Lightning: BurstLightning(ctx); break;
                case PunishmentType.Puddles: BurstPuddle(ctx); break;
                case PunishmentType.Darkness: BurstMeteor(ctx); break; // no good one-shot darkness; lob a meteor instead
            }
        }

        private const float WindGust = 6.5f;

        private static void BurstWind(PunishmentContext ctx)
        {
            if (ctx.Player == null) return;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            ctx.Player.Push(dir * WindGust);
            if (ctx.Prefabs?.WindStream != null)
            {
                var go = Object.Instantiate(ctx.Prefabs.WindStream, ctx.EffectsParent);
                var ws = go.GetComponent<WindStream>();
                if (ws != null) ws.Init(dir, ctx.Camera);
                TimedDestroy.Attach(go, 1.2f);
            }
        }

        private static void BurstMeteor(PunishmentContext ctx)
        {
            if (ctx.Prefabs?.Meteor == null || ctx.Player == null) return;
            var go = Object.Instantiate(ctx.Prefabs.Meteor, ctx.EffectsParent);
            var m = go.GetComponent<Meteor>();
            if (m != null) m.Init(ctx.Player.Position); // self-destructs on impact
        }

        private static void BurstLightning(PunishmentContext ctx)
        {
            if (ctx.Prefabs?.Lightning == null || ctx.Player == null || ctx.Camera == null) return;
            float halfH = ctx.Camera.orthographicSize, halfW = halfH * ctx.Camera.aspect;
            Vector2 c = ctx.Camera.transform.position;
            float ringR = Mathf.Sqrt(halfW * halfW + halfH * halfH) + 1.5f;
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 start = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR;
            Vector2 dir = (ctx.Player.Position - start).normalized;
            var go = Object.Instantiate(ctx.Prefabs.Lightning, ctx.EffectsParent);
            var bolt = go.GetComponent<Lightning>();
            if (bolt != null) bolt.Init(start, dir); // self-destructs
        }

        private static void BurstPuddle(PunishmentContext ctx)
        {
            if (ctx.Prefabs?.Puddle == null || ctx.Player == null) return;
            var go = Object.Instantiate(ctx.Prefabs.Puddle, ctx.EffectsParent);
            go.transform.position = ctx.Player.Position;
            go.transform.localScale = Vector3.one * 2.2f;
            TimedDestroy.Attach(go, 5f);
        }
    }
}
