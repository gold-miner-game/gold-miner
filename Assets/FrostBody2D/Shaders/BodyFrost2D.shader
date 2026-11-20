Shader "FrostBody2D/AttachedSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Body sprite", 2D) = "white" {}
        _FrostNoise ("Original game frost noise", 2D) = "gray" {}
        [HideInInspector] _FrostColor ("Frost colour", Color) = (.545,.682,1,.706)
        [HideInInspector] _EdgeWidth ("Edge width", Float) = .045
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        [HideInInspector] _BodyFrost ("Body frost", Float) = .18
        [HideInInspector] _TargetAlpha ("Target alpha", Float) = 1
        [HideInInspector] _CoverageTex ("Touched frost patches", 2D) = "white" {}
        [HideInInspector] _HasCoverage ("Use coverage", Float) = 0
        [HideInInspector] _StrokeMode ("Line silhouette", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _FrostNoise, _CoverageTex;
            float2 _UVOrigin, _UVX, _UVY;
            float4 _BodyRect, _FrostColor;
            float _EdgeWidth, _EffectTime, _Opacity, _BodyFrost, _TargetAlpha;
            float4 _CoverageRect;
            float _HasCoverage, _StrokeMode;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
            struct v2f {float4 vertex:SV_POSITION;float2 local:TEXCOORD0;float2 world:TEXCOORD1;fixed alpha:COLOR;};
            v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.local=v.uv;o.world=mul(unity_ObjectToWorld,v.vertex).xy;o.alpha=v.color.a;return o;}
            float alphaAt(float2 p)
            {
                float inside=step(_BodyRect.x,p.x)*step(_BodyRect.y,p.y)*step(p.x,_BodyRect.z)*step(p.y,_BodyRect.w);
                return tex2D(_MainTex,_UVOrigin+_UVX*p.x+_UVY*p.y).a*inside;
            }
            float hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.local;
                float2 coverageUV = _StrokeMode > .5 ? p : (p - _CoverageRect.xy) / max(_CoverageRect.zw - _CoverageRect.xy, .00001);
                float coverage = _HasCoverage > .5 ? saturate(tex2D(_CoverageTex, coverageUV).r * 12) : 1;
                clip(coverage - .001);
                float a=alphaAt(p), blur=a*2;
                if (_StrokeMode < .5)
                {
                    [unroll] for(int k=0;k<12;k++)
                    {
                        float angle=k*6.2831853/12;
                        float2 direction=float2(cos(angle),sin(angle));
                        float full=alphaAt(p+direction*_EdgeWidth), halfStep=alphaAt(p+direction*_EdgeWidth*.5);
                        blur+=full*.65+halfStep*1.35;
                    }
                }
                blur/=26;
                float inner=saturate(a-blur)*1.8, outer=saturate(blur-a)*1.8;
                if (_StrokeMode > .5) { a=1; inner=pow(saturate(abs(p.y-.5)*2),4); outer=0; p=i.world; }
                float noise=tex2D(_FrostNoise,p*3.5+float2(_EffectTime*.025,-_EffectTime*.018)).r;
                float rim=(inner+outer*.65)*(.2+smoothstep(.2,.75,noise)*1.15);
                float glaze=a*_BodyFrost*(.55+noise*.65);
                float2 cells=p/max(_EdgeWidth*.42,.001), cell=floor(cells);
                float2 center=frac(cells)-.5;
                float seed=hash21(cell);
                float twinkle=pow(saturate(sin(_EffectTime*(1.8+seed*2)+seed*40)*.5+.5),8);
                float crystal=step(.84,seed)*saturate(1-(abs(center.x)+abs(center.y))*3.5)*twinkle;
                float spark=crystal*saturate((inner+outer)*2.5)*.9;
                float opacity=saturate((rim*.88+glaze+spark)*_Opacity*_TargetAlpha*_FrostColor.a)*coverage;
                if (_StrokeMode > .5) opacity*=i.alpha;
                float3 color=lerp(_FrostColor.rgb,float3(.93,.98,1),saturate(rim*.55+spark));
                return float4(color*opacity,opacity);
            }
            ENDCG
        }
    }
}
