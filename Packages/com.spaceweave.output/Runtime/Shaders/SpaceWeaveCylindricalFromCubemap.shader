// CylindricalFromCubemap.shader
// Converts a cubemap to a cylindrical panorama texture.
//
// Use this instead of equirect for semi-circular or full-circle CAVE rooms.
// Cylindrical maps only the horizontal arc (±HFOV/2) and vertical wall range
// (±VFOV/2) to the full texture — no wasted pixels on poles.
//
// NEW in this version (expert upgrade):
//   _Rotation   — 3x3 rotation matrix for head-tracking / sweet-spot correction.
//                 CubemapRendererImproved.cs sets this automatically when applyRotation=true.
//                 Without it, cylindrical rooms do not respond to head-tracking (unlike equirect).
//   _SeamBlendPixels — seam blend at 0°/360° wrap-around (active when _HFOV >= 355°).
//                 At full 360° the azimuth wraps; without blending a visible seam appears
//                 at the cubemap face boundary. Matches the seam handling in CubemapToEquirect.
//   _OutputSize — output RT dimensions (set by script) for correct pixel-width seam blend.
//
// Set CaveViewer Input Format to "Cylindrical".
// _HFOV and _VFOV must match CaveViewer's cylindrical settings (default 360/120).

Shader "SpaceWeave/CylindricalFromCubemap"
{
    Properties
    {
        _Cubemap         ("Cubemap",                        Cube)   = "white" {}
        _HFOV            ("Horizontal FOV (deg)",           Float)  = 360.0
        _VFOV            ("Vertical FOV (deg)",             Float)  = 120.0
        _Exposure        ("Exposure",                       Float)  = 1.0
        _Gamma           ("Output Gamma (1=lin 2.2=sRGB)", Float)  = 1.0
        _SeamBlendPixels ("Seam Blend Pixels (360° only)",  Float)  = 8.0
        _OutputSize      ("Output Size (xy=px zw=1/px)",   Vector) = (8192, 4096, 0.000122, 0.000244)
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

            samplerCUBE _Cubemap;
            float4x4    _Rotation;   // set by CubemapRendererImproved — do NOT negate dir
            float _HFOV, _VFOV, _Exposure, _Gamma;
            float _SeamBlendPixels;
            float4 _OutputSize;  // xy = pixel dims, zw = 1/pixel dims

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // UV → cubemap sample direction for cylindrical projection.
            float3 CylUVToDir(float2 uv)
            {
                float yaw   = (uv.x - 0.5) * radians(_HFOV);
                // Top-down logical V: V=0 is zenith/top, V=0.5 is horizon, V=1 is nadir/bottom.
                //
                // The comment above was already correct about the INTENT; the code applied
                // the formula straight to Unity's blit UV, which is bottom-up (v=0 is the
                // BOTTOM row). So the cylindrical panorama was sent vertically mirrored,
                // the same defect as CubemapToEquirect had, while CubemapPack/Eac/Fisheye
                // and the truth pattern all converted correctly. Confirmed by
                // SpaceWeaveOrientationContractTests.Cylindrical_UpIsInTopHalf.
                float logicalV = 1.0 - uv.y;
                float pitch = atan((0.5 - logicalV) * 2.0 * tan(radians(_VFOV * 0.5)));
                float cp    = cos(pitch);
                float3 dir  = float3(sin(yaw) * cp, sin(pitch), cos(yaw) * cp);
                // Apply rotation matrix (head-tracking / sweet-spot correction).
                // No negation — the old Platform/CAVE shader had mul(..., -dir) which mirrored.
                // Guard: if _Rotation was never set it defaults to the zero matrix → corrupt output.
                // Check diagonal; identity has non-zero diagonal, zero matrix does not.
                float3x3 rot = (float3x3)_Rotation;
                float diagSum = rot[0][0] + rot[1][1] + rot[2][2];
                if (abs(diagSum) < 0.001) rot = float3x3(1,0,0, 0,1,0, 0,0,1);
                return normalize(mul(rot, dir));
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = texCUBE(_Cubemap, CylUVToDir(i.uv));

                // Seam blend at horizontal wrap-around (only meaningful when HFOV ≈ 360°).
                // At full 360° the left and right edges of the texture represent the same azimuth —
                // cubemap face boundaries produce a visible discontinuity without blending.
                if (_HFOV >= 355.0)
                {
                    // Shader-side fallback: if script never set _OutputSize (or set it to zero),
                    // compute reciprocals from xy. If xy are also zero, use safe 4096px default.
                    float invX = (_OutputSize.z > 0.0) ? _OutputSize.z :
                                 (_OutputSize.x > 0.0) ? (1.0 / _OutputSize.x) : (1.0 / 4096.0);
                    float seamW = max(_SeamBlendPixels * invX, invX);
                    float dist  = min(i.uv.x, 1.0 - i.uv.x);
                    if (dist < seamW)
                    {
                        float2 oUV = float2(i.uv.x < 0.5 ? i.uv.x + 1.0 : i.uv.x - 1.0, i.uv.y);
                        half4 oCol = texCUBE(_Cubemap, CylUVToDir(oUV));
                        col = lerp((col + oCol) * 0.5, col, smoothstep(0.0, seamW, dist));
                    }
                }

                col.rgb *= _Exposure;
                if (_Gamma != 1.0)
                    col.rgb = pow(max(col.rgb, 0.0001h), 1.0 / max(_Gamma, 0.0001));

                return col;
            }
            ENDCG
        }
    }
}
