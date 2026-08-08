Shader "SpaceWeave/EacFromCubemap"
{
    Properties
    {
        _Cubemap ("Cubemap", Cube) = "black" {}
        _Exposure ("Exposure", Float) = 1
        _OutputGamma ("Output Gamma", Float) = 2.2
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
            float _Exposure, _OutputGamma;
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

            // Face layout matches SpaceWeave sourceuv::MapCubeFace EAC:
            // row0: +X +Z -X   (Right Front Left)
            // row1: -Y -Z +Y   (Down Back Up)
            // Local face UV uses equi-angular (atan) mapping.
            float3 FaceDirectionEac(int face, float2 q)
            {
                // q is top-down local [0,1]. Convert through equi-angular to [-1,1] ST.
                float s = tan((q.x * 2.0 - 1.0) * 0.78539816339); // * pi/4
                float t = tan((q.y * 2.0 - 1.0) * 0.78539816339);
                // Match CubeFaceST inverse used by SpaceWeave DecodeAtlasDirection.
                if (face == 0) return normalize(float3( 1, -t, -s)); // +X
                if (face == 1) return normalize(float3(-1, -t,  s)); // -X
                if (face == 2) return normalize(float3( s,  1, -t)); // +Y
                if (face == 3) return normalize(float3( s, -1,  t)); // -Y
                if (face == 4) return normalize(float3( s, -t,  1)); // +Z
                return normalize(float3(-s, -t, -1));                 // -Z
            }

            float3 RotateDir(float3 dir)
            {
                float3x3 rot = (float3x3)_Rotation;
                if (dot(rot[0], rot[0]) > 0.01)
                    return normalize(mul(rot, dir));
                return dir;
            }

            bool EacCell(int col, int row, out int face)
            {
                face = -1;
                // cols: 0,1,2  rows: 0,1
                if (row == 0 && col == 0) face = 0; // +X
                if (row == 0 && col == 1) face = 4; // +Z
                if (row == 0 && col == 2) face = 1; // -X
                if (row == 1 && col == 0) face = 3; // -Y
                if (row == 1 && col == 1) face = 5; // -Z
                if (row == 1 && col == 2) face = 2; // +Y
                return face >= 0;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 logical = float2(i.uv.x, 1.0 - i.uv.y);
                float2 cell = floor(min(logical, 0.999999) * float2(3, 2));
                int face;
                if (!EacCell((int)cell.x, (int)cell.y, face))
                    return half4(0, 0, 0, 1);
                float2 q = frac(logical * float2(3, 2));
                half4 c = texCUBE(_Cubemap, RotateDir(FaceDirectionEac(face, q)));
                c.rgb *= _Exposure;
                c.rgb = pow(max(c.rgb, 0.0001h), 1.0 / max(_OutputGamma, 0.0001));
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
