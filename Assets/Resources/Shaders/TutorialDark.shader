// Owner: Penny (Level map & tutorial)
// The tutorial's dark over the UI (TutorialUIDark): a full-screen UI image that is see-through
// inside up to 8 rounded rectangles (the arena and the TutorialUIHoles). Based on UI/Default.
// Hole coordinates are in the root canvas's space, like RectMask2D's clip rect.
Shader "GoldMiner/TutorialDark"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex    : SV_POSITION;
                fixed4 color     : COLOR;
                float2 canvasPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _Rects[8];       // xMin, yMin, xMax, yMax
            float4 _RectStyle[8];   // corner radius, feather (soft edge width), unused, unused
            float _RectCount;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.canvasPos = v.vertex.xy;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float visible = 1.0;
                for (int h = 0; h < 8; h++)
                {
                    if (h >= _RectCount) break;
                    float4 r = _Rects[h];
                    float corner = _RectStyle[h].x;
                    float feather = _RectStyle[h].y;
                    float2 centre = (r.xy + r.zw) * 0.5;
                    float2 halfSize = (r.zw - r.xy) * 0.5;
                    // Signed distance to the rounded rectangle: < 0 inside, > 0 outside
                    float2 q = abs(i.canvasPos - centre) - halfSize + corner;
                    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
                    visible *= feather > 0.0 ? saturate(d / feather) : step(0.0, d);
                }
                fixed4 c = i.color;
                c.a *= visible;
                return c;
            }
            ENDCG
        }
    }
}
