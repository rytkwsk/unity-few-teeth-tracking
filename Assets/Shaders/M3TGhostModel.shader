Shader "M3T/GhostModel"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.85, 1, 0.06)
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }

        Pass
        {
            Cull Back
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
