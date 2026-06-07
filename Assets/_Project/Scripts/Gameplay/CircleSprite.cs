using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Builds a soft white circle sprite at runtime so the game needs no art
    /// assets for its round shapes (balloons, bokeh, pop particles). The texture
    /// is unit-sized: pixelsPerUnit == size means the sprite is exactly 1 world
    /// unit wide, so a GameObject's localScale maps straight to world diameter.
    /// </summary>
    public static class CircleSprite
    {
        /// <summary>
        /// A circle with a soft (anti-aliased) edge. <paramref name="edgeSoftness"/>
        /// is the fraction of the radius over which alpha fades from 1 to 0 at the
        /// rim — 0 gives a hard edge, ~0.5 gives a fuzzy bokeh look.
        /// </summary>
        public static Sprite Build(int size = 64, float edgeSoftness = 0.15f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float r = size * 0.5f;
            float softPixels = Mathf.Max(0.0001f, r * edgeSoftness);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1 inside, fading to 0 across the soft band ending at the rim.
                    float alpha = Mathf.Clamp01((r - dist) / softPixels);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size); // pixelsPerUnit = size -> sprite is 1 world unit wide
        }
    }
}
