// 輪郭リング表示。
// Pass 1: 押し出し無しのメッシュでデプスだけ書く（色は出さない）。
// Pass 2: 法線方向に _OutlineWidth 押し出したメッシュの裏面だけ描く。
//         Pass 1 のデプスに隠れてモデル内側は消え、輪郭の外側のリングだけが残る。
// 2 パスを同じマテリアルに入れているので、別オブジェクトの描画順に依存しない。
// _OutlineWidth はオブジェクト空間（OBJ 単位）。メートル指定は C# 側で lossyScale で割る。
Shader "M3T/OutlineSilhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0.75, 1, 0.85)
        _OutlineWidth ("Outline Width", Float) = 0.048
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        // Pass 1: depth only
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }

        // Pass 2: extruded back faces = ring
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 normal = normalize(v.normal);
                v.vertex.xyz += normal * _OutlineWidth;
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
