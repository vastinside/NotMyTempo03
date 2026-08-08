Shader "SpaceWeave/FisheyeFromCubemap"
{
    Properties
    {
        _Cubemap ("Cubemap", Cube) = "black" {}
        _FovDeg ("FOV degrees (180 or 360)", Float) = 180
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
            float _FovDeg, _Exposure, _OutputGamma;
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

            // Matches SpaceWeave sourceuv::Map for FisheyeDome / Fisheye360:
            // zenith at centre, equidistant radius, phi = atan2(dx, -dz),
            // u = 0.5 + r*sin(phi), v = 0.5 - r*cos(phi) in top-down logical UV.
            half4 frag(v2f i) : SV_Target
            {
                float2 logical = float2(i.uv.x, 1.0 - i.uv.y);
                float2 dUV = logical - 0.5;
                float radius = length(dUV);
                float maxTheta = radians(clamp(_FovDeg, 1.0, 360.0)) * 0.5;
                // SpaceWeave uses maxTheta = pi/2 for dome, pi for 360.
                if (_FovDeg < 270.0) maxTheta = 3.14159265359 * 0.5;
                else maxTheta = 3.14159265359;

                float rNorm = radius / 0.5;
                if (rNorm > 1.0 + 1e-4)
                    return half4(0, 0, 0, 1);

                float theta = rNorm * maxTheta;
                float phi = atan2(dUV.x, -dUV.y);
                float st = sin(theta);
                float3 dir = normalize(float3(st * sin(phi), cos(theta), -st * cos(phi)));
                float3x3 rot = (float3x3)_Rotation;
                if (dot(rot[0], rot[0]) > 0.01)
                    dir = normalize(mul(rot, dir));

                half4 c = texCUBE(_Cubemap, dir);
                c.rgb *= _Exposure;
                c.rgb = pow(max(c.rgb, 0.0001h), 1.0 / max(_OutputGamma, 0.0001));
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
