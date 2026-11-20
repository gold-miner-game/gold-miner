Shader "GoldMiner/RhinoTintOverlap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RedEnabled ("Charge surface", Float) = 1
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            fixed4 _ChargeColor, _DigestColor;
            float4 _RedShape;
            float _GreenTop, _Circular, _RedEnabled;
            struct outputData
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
            };
            outputData vert(appdata_t input)
            {
                v2f sprite = SpriteVert(input);
                outputData output;
                output.vertex = sprite.vertex;
                output.color = sprite.color;
                output.uv = sprite.texcoord;
                input.vertex.xy *= _Flip.xy;
                output.world = mul(unity_ObjectToWorld, input.vertex).xy;
                return output;
            }
            fixed4 frag(outputData input) : SV_Target
            {
                float red = _RedEnabled * (_Circular > .5
                    ? step(distance(input.world, _RedShape.xy), _RedShape.z)
                    : step(input.world.y, _RedShape.w));
                float green = step(input.world.y, _GreenTop);
                clip(max(red, green) - .5);
                fixed4 tint = green > .5 ? _DigestColor : _ChargeColor;
                fixed4 color = SampleSpriteTexture(input.uv) * input.color * tint;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
