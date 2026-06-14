// Full-screen lens flare for the level Goal ("sun"). Ported from the Shadertoy
// reference (slVXDW / Xlc3D2) and fully parameterised: every visual knob is a
// material Property so the whole look can be tuned from the Inspector.
//
// Rendered on a screen-filling quad parented to the game camera; SunFlareController
// feeds the light's screen position (_Center) and aspect (_Aspect) each frame, so
// the coloured ghosts streak across the lens from the Sun. Additive over the scene.
// Pure SDF + analytic terms (no texture/noise) -> cheap, WebGL-friendly.
Shader "Ngj10/SunFlare"
{
    Properties
    {
        [Header(General)] [Space(4)]
        [HDR] _Tint       ("Tint (multiply all)", Color) = (1,1,1,1)
        _Intensity        ("Master Intensity", Range(0,6)) = 1.0
        _AnimSpeed        ("Anim Speed (corona spin)", Range(0,3)) = 0.0

        [Header(Core)] [Space(4)]
        [HDR] _CoreColor  ("Core Color", Color) = (0.2, 0.21, 0.3, 1)
        _CoreIntensity    ("Core Intensity", Range(0,40)) = 16.0
        _CoreSize         ("Core Size", Range(0.01,1)) = 0.1
        _CoreSharpness    ("Core Sharpness", Range(0.1,3)) = 0.5

        [Header(Corona Rays)] [Space(4)]
        _RayIntensity     ("Ray Intensity", Range(0,4)) = 1.0
        _RayFreqA         ("Ray Freq A (spikes)", Range(1,24)) = 5.0
        _RayFreqB         ("Ray Freq B (wobble)", Range(1,24)) = 9.0
        _RayFreqC         ("Ray Freq C (frills)", Range(1,24)) = 3.0

        [Header(Halo Arc)] [Space(4)]
        _HaloIntensity    ("Halo Intensity", Range(0,4)) = 1.0
        _HaloRadius       ("Halo Radius", Range(0.2,3)) = 1.5
        _HaloWidth        ("Halo Width", Range(0.5,12)) = 4.0
        _HaloChroma       ("Halo Chroma (rainbow)", Range(0,0.4)) = 0.05

        [Header(World Quad)] [Space(4)]
        _EdgeMask         ("Edge Mask (0=off / fullscreen)", Range(0,6)) = 1.5

        [Header(Ghosts)] [Space(4)]
        _GhostIntensity   ("Ghost Intensity", Range(0,4)) = 1.0
        _GhostSpread      ("Ghost Spread", Range(0,3)) = 1.0
        _GhostScale       ("Ghost Size", Range(0.2,4)) = 1.0
        _GhostShape       ("Ghost Shape (round to hex)", Range(0,1)) = 0.7
        _GhostEdge        ("Ghost Edge Hardness", Range(20,400)) = 200.0

        [Header(Driven by SunFlareController)] [Space(4)]
        _Center           ("Light Center (xy)", Vector) = (0,0,0,0)
        _Aspect           ("Aspect", Float) = 1.7777778
        _Fade             ("Edge Fade (driven)", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend One One           // additive: black = invisible
            ZWrite Off
            Cull Off
            ZTest Always            // overlay; ignore scene depth

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float  _Intensity;
                float  _AnimSpeed;
                float4 _CoreColor;
                float  _CoreIntensity, _CoreSize, _CoreSharpness;
                float  _RayIntensity, _RayFreqA, _RayFreqB, _RayFreqC;
                float  _HaloIntensity, _HaloRadius, _HaloWidth, _HaloChroma;
                float  _EdgeMask;
                float  _GhostIntensity, _GhostSpread, _GhostScale, _GhostShape, _GhostEdge;
                float4 _Center;
                float  _Aspect;
                float  _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float sat(float x) { return saturate(x); }

            float2 rotate(float2 p, float r)
            {
                float s = sin(r), c = cos(r);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            float sdCircle(float2 p, float r) { return length(p) - r; }
            float sdHexagon(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            // Analytic halo ring (chromatic spread applied by the caller).
            float halo(float2 p, float2 center, float r)
            {
                float l  = length(p);
                float l1 = abs(l - r);
                float n  = pow(sat(1.0 - l1 * _HaloWidth), 1.2);
                return n * 0.2 * sat(pow(
                    sat(pow(length(center), 0.5)),
                    (length(p - center) / max(r, 1e-3)) * 1.5));
            }

            // One ghost: blends chromatic halo (focus=0) with a hard aperture shape
            // (focus=1). Spread/scale/shape/edge come from the global knobs.
            float3 ghost3(float2 p, float2 center, float focus, float r, float offset)
            {
                r *= _GhostScale;
                p -= center * offset * _GhostSpread;
                float2 p2 = rotate(p, 0.25);
                float d0 = lerp(sdCircle(p2 * 0.85, r), sdHexagon(p2 * 0.85, r), _GhostShape);
                float d1 = lerp(sdCircle(p2,        r), sdHexagon(p2,        r), _GhostShape);
                float d2 = lerp(sdCircle(p2 * 1.15, r), sdHexagon(p2 * 1.15, r), _GhostShape);
                float c = _HaloChroma;
                float3 haloRGB = float3(
                    halo(p * (1.0 + c), center, r),
                    halo(p,             center, r),
                    halo(p * (1.0 - c), center, r)) * float3(2.0, 1.5, 1.0) * 2.0;
                float3 aperture = pow(sat(1.0 - float3(d0, d1, d2)), _GhostEdge.xxx);
                return lerp(haloRGB, aperture, focus);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 p = IN.uv - 0.5;
                p.x *= _Aspect;
                float2 center = _Center.xy;

                float a  = atan2(p.y - center.y, p.x - center.x) + _Time.y * _AnimSpeed;
                float dl = length(p - center);
                float env = exp(1.0 - dl) / 5.0;

                // --- core + corona rays (centred on the light) ---
                float spikes = abs(sin(a * _RayFreqA + cos(a * _RayFreqB)));
                float frills = abs(sin(a * _RayFreqC + cos(a * _RayFreqB))) * abs(sin(a * _RayFreqB));
                float3 sun = 0.0;
                sun += max(0.1 / pow(dl * 5.0, 5.0), 0.0) * spikes / 20.0 * _RayIntensity;
                sun += max(0.1 / pow(dl * 10.0, 0.05), 0.0) + frills / 8.0 * _RayIntensity;
                sun += max(_CoreSize / pow(dl * 4.0, _CoreSharpness), 0.0)
                       * _CoreColor.rgb * _CoreIntensity;
                sun *= env;

                float3 col = sun;

                // --- big chromatic halo ---
                col += ghost3(p, center, 0.0, _HaloRadius, 1.2) * _HaloIntensity;

                // --- coloured aperture ghosts streaking across the lens ---
                float gi = _GhostIntensity;
                col += ghost3(p, center, 0.3,  0.105, 0.5 / 1.5)  * float3(0.5,  0.1,  -0.05) * gi;
                col += ghost3(p, center, 0.3,  0.11,  0.8)        * float3(0.1,  0.6,  -0.05) * gi;
                col += ghost3(p, center, 0.3,  0.11,  0.3 / 1.5)  * float3(0.05, -0.05, 0.45) * gi;
                col += ghost3(p, center, 0.2,  0.13, -0.8 / 3.5)  * float3(0.5,  0.1,  -0.05) * gi;
                col += ghost3(p, center, 0.05, 0.11, -1.2 / 3.5)  * float3(0.0,  0.5,  -0.05) * gi;
                col += ghost3(p, center, 0.2,  0.17, -2.0 / 3.5)  * float3(0.05, -0.05, 0.45) * gi;
                col += ghost3(p, center, 0.25, 0.14, -3.15 / 3.5) * float3(0.0,  0.4,   0.2)  * gi;
                col += ghost3(p, center, 0.15, 0.185,-3.75 / 3.5) * float3(0.0,  0.2,   0.05) * gi;
                col += ghost3(p, center, 0.05, 0.16, -4.5 / 3.5)  * float3(0.05, -0.05, 0.45) * gi;
                col += ghost3(p, center, 0.20, 0.13, -5.75 / 3.5) * float3(0.0,  0.3,   0.5)  * gi;
                col += ghost3(p, center, 0.08, 0.12, -5.95 / 3.5) * float3(0.05, 0.2,   0.05) * gi;
                col += ghost3(p, center, 0.04, 0.11, -6.15 / 3.5) * float3(0.1, -0.05,  0.65) * gi;

                // Radial mask so a world-space quad fades to transparent before its
                // square edge. _EdgeMask = 0 disables it (full-screen overlay use).
                float mask = (_EdgeMask <= 0.0) ? 1.0
                    : pow(sat(1.0 - length(IN.uv - 0.5) * 2.0), _EdgeMask);

                col = max(col, 0.0) * _Tint.rgb * _Intensity * _Fade * mask;
                return half4(col, 1.0);          // additive -> alpha unused
            }
            ENDHLSL
        }
    }
    Fallback Off
}
