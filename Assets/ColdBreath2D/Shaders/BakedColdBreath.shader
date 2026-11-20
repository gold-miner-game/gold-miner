Shader "ColdBreath2D/BakedOriginal"
{
 Properties { [HideInInspector] _FrameRect ("Frame rectangle", Vector)=(0,0,1,1) _MainTex ("Baked original animation", 2D)="black" {} }
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
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   float4 _FrameRect;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
   v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=_FrameRect.xy+v.uv*_FrameRect.zw;return o;}
   fixed4 frag(v2f i):SV_Target
   {
    fixed3 rgb=tex2D(_MainTex,i.uv).rgb;
    // The capture was composited on black. Preserve its RGB exactly on black,
    // and use a premultiplied luminance key so the empty background disappears.
    return fixed4(rgb,max(rgb.r,max(rgb.g,rgb.b)));
   }
   ENDCG
  }
 }
}
