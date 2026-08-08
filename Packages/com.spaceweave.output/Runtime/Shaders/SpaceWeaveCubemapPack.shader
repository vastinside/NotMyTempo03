Shader "SpaceWeave/CubemapPack"
{
    Properties
    {
        _Cubemap ("Cubemap", Cube) = "black" {}
        _Layout ("0=Cross 1=Strip", Float) = 0
        _Exposure ("Exposure", Float) = 1
        _OutputGamma ("Output Gamma", Float) = 2.2
        _DiagnosticOverlay ("Diagnostic Overlay", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Cubemap;
            float _Layout, _Exposure, _OutputGamma, _DiagnosticOverlay;
            float4x4 _Rotation;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Must match SpaceWeaveOutputContract.DirectionToCubemapFaceUv and
            // SpaceWeave DecodeAtlasDirection / CubeFaceST exactly. A Z sign flip
            // on +Y/-Y packs the floor and ceiling mirrored front↔back; SpaceWeave
            // then samples the wrong seam and the operator sees solid magenta/cyan
            // strips (the intentional face markers and bilinear bleed from neighbours).
            float3 FaceDirection(int face, float2 q)
            {
                float x = q.x * 2.0 - 1.0;
                float y = 1.0 - q.y * 2.0;
                if (face == 0) return normalize(float3( 1, y, -x)); // +X
                if (face == 1) return normalize(float3(-1, y,  x)); // -X
                if (face == 2) return normalize(float3( x, 1,  1.0 - q.y * 2.0)); // +Y
                if (face == 3) return normalize(float3( x,-1,  q.y * 2.0 - 1.0)); // -Y
                if (face == 4) return normalize(float3( x, y,  1)); // +Z
                return normalize(float3(-x, y, -1)); // -Z
            }

            float3 RotateDir(float3 dir)
            {
                float3x3 rot = (float3x3)_Rotation;
                if (dot(rot[0], rot[0]) > 0.01)
                    return normalize(mul(rot, dir));
                return dir;
            }

            bool CrossCell(int col, int row, out int face)
            {
                face = -1;
                if (row == 0 && col == 1) face = 2;
                if (row == 1 && col == 0) face = 1;
                if (row == 1 && col == 1) face = 4;
                if (row == 1 && col == 2) face = 0;
                if (row == 1 && col == 3) face = 5;
                if (row == 2 && col == 1) face = 3;
                return face >= 0;
            }

            half3 FaceColour(int face)
            {
                if (face == 0) return half3(1, 0, 0);
                if (face == 1) return half3(0, 1, 0);
                if (face == 2) return half3(0, 0, 1);
                if (face == 3) return half3(1, 1, 0);
                if (face == 4) return half3(0, 1, 1);
                return half3(1, 0, 1);
            }

            half4 frag(v2f i) : SV_Target
            {
                // Work in the source contract's top-down logical image coordinates.
                float2 logical = float2(i.uv.x, 1.0 - i.uv.y);
                int face;
                float2 q;
                if (_Layout > 0.5)
                {
                    float sx = min(logical.x, 0.999999) * 6.0;
                    face = (int)floor(sx);
                    q = float2(frac(sx), logical.y);
                }
                else
                {
                    float2 cell = floor(min(logical, 0.999999) * float2(4, 3));
                    if (!CrossCell((int)cell.x, (int)cell.y, face))
                        return half4(0, 0, 0, 1);
                    q = frac(logical * float2(4, 3));
                }

                half4 c = texCUBE(_Cubemap, RotateDir(FaceDirection(face, q)));
                c.rgb *= _Exposure;
                c.rgb = pow(max(c.rgb, 0.0001h), 1.0 / max(_OutputGamma, 0.0001));
                c.a = 1;

                if (_DiagnosticOverlay > 0.5)
                {
                    float edge = step(q.x, 0.012) + step(0.988, q.x) +
                                 step(q.y, 0.012) + step(0.988, q.y);
                    c.rgb = lerp(c.rgb, half3(1, 1, 1), saturate(edge));
                    float corner = step(q.x, 0.08) * step(q.y, 0.08);
                    c.rgb = lerp(c.rgb, FaceColour(face), corner);
                }
                return c;
            }
            ENDCG
        }
    }
}
