using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// A one-shot particle burst spawned where a balloon pops. Built entirely in
    /// code (no prefab, no art) from the built-in <see cref="ParticleSystem"/> —
    /// which runs on WebGL, unlike the URP VFX Graph (compute shaders, no WebGL).
    /// Self-destructs once the burst has finished, so callers just spawn and forget.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class PopBurst : MonoBehaviour
    {
        /// <summary>
        /// Spawn a burst at <paramref name="position"/> tinted <paramref name="color"/>.
        /// The shared circle <paramref name="sprite"/> is reused for the particle
        /// material so no extra texture is generated per pop.
        /// </summary>
        public static void Spawn(Vector3 position, Color color, Sprite sprite)
        {
            var go = new GameObject("PopBurst");
            go.transform.position = position;
            go.AddComponent<PopBurst>().Build(color, sprite);
        }

        private void Build(Color color, Sprite sprite)
        {
            var ps = GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            const float lifetime = 0.45f;

            var main = ps.main;
            main.duration = lifetime;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startColor = color;
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy; // frees the GameObject

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.15f;

            // Fade and shrink over life so the burst dissolves instead of vanishing.
            var color2 = ps.colorOverLifetime;
            color2.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color2.color = new ParticleSystem.MinMaxGradient(grad);

            var size2 = ps.sizeOverLifetime;
            size2.enabled = true;
            size2.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Built-in additive-friendly sprite material; tint comes from start color.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = sprite.texture;
            renderer.material = mat;
            renderer.sortingOrder = 50; // above balloons

            ps.Play();
        }
    }
}
