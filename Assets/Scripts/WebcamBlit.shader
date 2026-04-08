// Minimal unlit texture shader with ZTest Always and ZWrite Off.
// Used by KinectDirectRenderer and WebcamDebugRenderer to draw the webcam
// feed as a fullscreen GL quad without being occluded by scene geometry.
Shader "Hidden/WebcamBlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // GL.LoadOrtho() sets the legacy GL matrices but NOT
                // UNITY_MATRIX_MVP, so UnityObjectToClipPosition won't work.
                // Manually map 0-1 input vertices to -1..1 clip space.
                o.pos   = float4(v.vertex.x * 2.0 - 1.0,
                                 v.vertex.y * 2.0 - 1.0,
                                 v.vertex.z, 1.0);
                o.uv    = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
