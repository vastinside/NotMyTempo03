Shader "SpaceWeave/SourceTruthPattern"
{
    Properties
    {
        _MainTex ("Blit source (unused)", 2D) = "black" {}
        _Layout ("0=Equirect 1=Cylindrical 2=Cross 3=Strip", Float) = 0
        _HFOV ("Cylindrical HFOV", Float) = 360
        _VFOV ("Cylindrical VFOV", Float) = 120
        _TimeSeconds ("Time", Float) = 0
        _OutputSize ("Output Size", Vector) = (4096,2048,0,0)
    }
    SubShader
    {
        // Blit-compatible: do not restrict to URP-only. Graphics.Blit / EditMode
        // tests use the built-in blit path; a URP-only tag can yield a pass that
        // never draws contract markers while still reporting isSupported.
        Tags { "Queue"="Overlay" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Layout, _HFOV, _VFOV, _TimeSeconds;
            float4 _OutputSize;

            float RectMask(float2 uv, float2 c, float2 halfSize)
            {
                float2 d=abs(uv-c);
                return step(d.x,halfSize.x)*step(d.y,halfSize.y);
            }

            // Marker size in UV. Prefer ~12px of the declared output, but never
            // smaller than ~2% UV — otherwise a 512 test RT with a stale 4K
            // _OutputSize makes the front marker sub-pixel and the horizon wins.
            float MarkerSizeUv()
            {
                float2 px = max(_OutputSize.xy, float2(1.0, 1.0));
                return max(max(12.0 / px.x, 12.0 / px.y), 0.02);
            }

            half4 frag(v2f_img i) : SV_Target
            {
                // Blit UV is bottom-left; contract space is top-down (v=0 at top).
                float2 uv=float2(i.uv.x,1.0-i.uv.y);
                float3 col=float3(.018,.022,.055);
                float2 px = max(_OutputSize.xy, float2(1.0, 1.0));
                float2 magicSize = max(
                    float2(10.0 / px.x, 10.0 / px.y),
                    float2(0.004, 0.006));
                col=lerp(col,float3(1,0,0),RectMask(uv,float2(.010,.012),magicSize));
                col=lerp(col,float3(0,1,0),RectMask(uv,float2(.025,.012),magicSize));
                col=lerp(col,float3(0,0,1),RectMask(uv,float2(.040,.012),magicSize));
                col=lerp(col,float3(1,1,1),RectMask(uv,float2(.055,.012),magicSize));

                float markerSize=MarkerSizeUv();
                if (_Layout<1.5)
                {
                    float horizonMask=step(abs(uv.y-.5),.008);
                    col=lerp(col,float3(1,.65,.05),horizonMask);
                    float2 markerHalf=float2(markerSize*.58,markerSize*.58);
                    // Direction markers after horizon so front/top always win at centres.
                    col=lerp(col,float3(1,.08,.08),RectMask(uv,float2(.50,.50),markerHalf));
                    col=lerp(col,float3(.05,1,1),RectMask(uv,float2(.002,.50),markerHalf));
                    col=lerp(col,float3(.08,.18,1),RectMask(uv,float2(.25,.50),markerHalf));
                    col=lerp(col,float3(.08,1,.12),RectMask(uv,float2(.75,.50),markerHalf));
                    if (_Layout<.5)
                    {
                        col=lerp(col,float3(1,1,.05),RectMask(uv,float2(.50,.055),markerHalf));
                        col=lerp(col,float3(1,.05,1),RectMask(uv,float2(.50,.945),markerHalf));
                    }
                    else
                    {
                        col=lerp(col,float3(1,1,.05),RectMask(uv,float2(.50,.075),markerHalf));
                        col=lerp(col,float3(1,.05,1),RectMask(uv,float2(.50,.925),markerHalf));
                        // v2 HFOV fiducials: squares at world yaw -45/+45 deg.
                        // Texture-u = 0.5 -/+ 45/HFOV, so the receiver can solve
                        // the sender HFOV exactly from where these land. Skipped
                        // below 95 deg where they would leave the safe interior.
                        if (_HFOV>=95.0)
                        {
                            float uOff=45.0/_HFOV;
                            col=lerp(col,float3(1,.55,.05),
                                RectMask(uv,float2(.50-uOff,.30),markerHalf));
                            col=lerp(col,float3(.05,1,.55),
                                RectMask(uv,float2(.50+uOff,.30),markerHalf));
                        }
                    }
                    float seamHalf=max(3.0/px.x, 0.0015);
                    float seamMask=max(RectMask(uv,float2(0,.5),float2(seamHalf,.5)),
                                       RectMask(uv,float2(1,.5),float2(seamHalf,.5)));
                    col=lerp(col,float3(1,0,1),seamMask);
                }
                else
                {
                    float face=-1;
                    float2 q=float2(0,0);
                    // Face indices match the contract: +X=0 -X=1 +Y=2 -Y=3 +Z=4 -Z=5.
                    // Strip order +X -X +Y -Y +Z -Z. Cross: +Y / (-X +Z +X -Z) / -Y.
                    if (_Layout>2.5)
                    {
                        float stripX=min(uv.x,.999999)*6;
                        float stripCell=floor(stripX);
                        face=stripCell; // 0..5 in contract order
                        q=float2(frac(stripX),uv.y);
                    }
                    else
                    {
                        float2 cell=floor(min(uv,.999999)*float2(4,3));
                        if(cell.y<.5&&cell.x>.5&&cell.x<1.5) face=2;              // +Y
                        else if(cell.y>.5&&cell.y<1.5&&cell.x<.5) face=1;           // -X
                        else if(cell.y>.5&&cell.y<1.5&&cell.x>.5&&cell.x<1.5) face=4;// +Z
                        else if(cell.y>.5&&cell.y<1.5&&cell.x>1.5&&cell.x<2.5) face=0;// +X
                        else if(cell.y>.5&&cell.y<1.5&&cell.x>2.5) face=5;          // -Z
                        else if(cell.y>1.5&&cell.x>.5&&cell.x<1.5) face=3;           // -Y
                        q=frac(uv*float2(4,3));
                    }
                    if(face>=0)
                    {
                        float2 cornerHalf=float2(.035,.035);
                        col=lerp(col,float3(1,0,0),RectMask(q,float2(.07,.07),cornerHalf));
                        col=lerp(col,float3(0,1,0),RectMask(q,float2(.93,.07),cornerHalf));
                        col=lerp(col,float3(0,0,1),RectMask(q,float2(.93,.93),cornerHalf));
                        col=lerp(col,float3(1,1,0),RectMask(q,float2(.07,.93),cornerHalf));
                        float3 faceColour=float3(1,.08,.08);
                        if(face>.5&&face<1.5) faceColour=float3(.05,1,1);
                        else if(face>1.5&&face<2.5) faceColour=float3(.08,.18,1);
                        else if(face>2.5&&face<3.5) faceColour=float3(.08,1,.12);
                        else if(face>3.5&&face<4.5) faceColour=float3(1,1,.05);
                        else if(face>4.5) faceColour=float3(1,.05,1);
                        col=lerp(col,faceColour,RectMask(q,float2(.5,.5),float2(.03,.03)));
                    }
                }
                float barX=frac(_TimeSeconds*.18);
                col=lerp(col,float3(1,1,1),RectMask(uv,float2(barX,.985),float2(max(2.0/px.x,0.0005),.012)));
                return half4(col,1);
            }
            ENDCG
        }
    }
}
