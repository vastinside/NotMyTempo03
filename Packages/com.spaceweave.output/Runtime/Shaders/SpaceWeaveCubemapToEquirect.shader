Shader "SpaceWeave/CubemapToEquirect"
{
    Properties
    {
        _MainTex        ("Cubemap",                        Cube)   = "white" {}
        _YawOffsetDeg   ("Yaw Offset (deg)",               Float)  = 0
        _PitchOffsetDeg ("Pitch Offset (deg)",             Float)  = 0
        _SeamBlendPixels("Seam Blend Pixels",              Float)  = 8
        _PoleBlendWidth ("Pole Blend Width (0.005-0.05)",  Float)  = 0.02
        _OutputSize     ("Output Size (xy=px  zw=1/px)",   Vector) = (8192, 4096, 0.000122, 0.000244)
        _Exposure       ("Exposure",                       Float)  = 1.0
        _Gamma          ("Output Gamma (1=lin  2.2=sRGB)", Float)  = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define PI     3.14159265359
            #define TWO_PI 6.28318530718

            samplerCUBE _MainTex;
            float4x4    _Rotation;
            float _YawOffsetDeg, _PitchOffsetDeg, _SeamBlendPixels, _PoleBlendWidth, _Exposure, _Gamma;
            float4 _OutputSize;   // xy = pixel dims,  zw = 1/pixel dims

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Equirect UV [0,1]^2  →  cubemap sample direction.
            // No negation of dir — the old Platform/CAVE version had  mul(..., -dir)
            // which mirrored the panorama left-right. This version is correct.
            float3 UVToDir(float2 uv)
            {
                // Blit UV is bottom-up (v=0 is the BOTTOM row of the render target);
                // the SpaceWeave source contract's logical V is top-down (v=0 is the
                // zenith). The pitch formula below is written in logical V, so the
                // incoming UV must be converted first -- exactly as
                // SpaceWeaveCubemapPack.shader:77, SpaceWeaveEacFromCubemap.shader:66,
                // SpaceWeaveFisheyeFromCubemap.shader:39 and the truth pattern all do.
                //
                // Without this the whole panorama was sent VERTICALLY MIRRORED. It went
                // unnoticed because the truth pattern is in the converting group: an
                // operator validated orientation, saw it pass, switched to live content
                // and got sky on the floor. Confirmed by
                // SpaceWeaveOrientationContractTests.Equirect_UpIsInTopHalf, which read
                // DOWN(-Y) at logical v=0.05 while the cube-cross control read UP(+Y).
                //
                // Converting HERE rather than at each call site keeps one statement of
                // the convention: frag samples this for the seam and pole neighbours too.
                float logicalV = 1.0 - uv.y;
                float yaw   = (uv.x - 0.5) * TWO_PI + radians(_YawOffsetDeg);
                float pitch = (0.5 - logicalV) * PI  + radians(_PitchOffsetDeg);
                float cp    = cos(pitch);
                float3 dir  = float3(sin(yaw) * cp, sin(pitch), cos(yaw) * cp);
                // Guard an unset _Rotation, which defaults to the ZERO matrix and makes
                // normalize() produce NaN for every pixel. CylindricalFromCubemap
                // already does this; this shader did not.
                float3x3 rot = (float3x3)_Rotation;
                float diagSum = rot[0][0] + rot[1][1] + rot[2][2];
                if (abs(diagSum) < 0.001) rot = float3x3(1,0,0, 0,1,0, 0,0,1);
                return normalize(mul(rot, dir));
            }

            // Use half4, NOT fixed4 — fixed4 clamps to [0,1] and destroys HDR values.
            half4 frag(v2f i) : SV_Target
            {
                float2 uv;
                uv.x = frac(i.uv.x);
                // Clamp vertical to half-texel from the poles to avoid cubemap singularities.
                uv.y = clamp(i.uv.y, _OutputSize.w * 0.5, 1.0 - _OutputSize.w * 0.5);

                half4 col = texCUBE(_MainTex, UVToDir(uv));

                // ── Horizontal seam blend (left ↔ right wrap-around) ──────────────────
                float seamW = max(_SeamBlendPixels * _OutputSize.z, _OutputSize.z);
                float dist  = min(uv.x, 1.0 - uv.x);
                if (dist < seamW)
                {
                    float2 oUV = float2(uv.x < 0.5 ? uv.x + 1.0 : uv.x - 1.0, uv.y);
                    half4 oCol = texCUBE(_MainTex, UVToDir(oUV));
                    col = lerp((col + oCol) * 0.5, col, smoothstep(0.0, seamW, dist));
                }

                // ── Pole smoothing (top/bottom 2% of image) ───────────────────────────
                // Averages 3 horizontal neighbours to suppress cubemap corner seam
                // artifacts that appear at the poles of the equirect projection.
                float poleW    = _PoleBlendWidth;  // was hardcoded 0.02; now tunable (default 0.02)
                float poleDist = min(uv.y, 1.0 - uv.y);
                if (poleDist < poleW)
                {
                    float2 l = float2(frac(uv.x - _OutputSize.z * 2.0), uv.y);
                    float2 r = float2(frac(uv.x + _OutputSize.z * 2.0), uv.y);
                    half4 avg = (texCUBE(_MainTex, UVToDir(l)) + col +
                                 texCUBE(_MainTex, UVToDir(r))) / 3.0;
                    col = lerp(avg, col, smoothstep(0.0, poleW, poleDist));
                }

                // ── Exposure + gamma encode ───────────────────────────────────────────
                col.rgb *= _Exposure;
                if (_Gamma != 1.0)
                    col.rgb = pow(max(col.rgb, 0.0001h), 1.0 / _Gamma);

                return col;
            }
            ENDCG
        }
    }
}