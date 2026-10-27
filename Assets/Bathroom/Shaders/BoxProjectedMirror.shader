// Mirror surface that reflects the baked room cubemap with box projection, so reflections line up
// with the room's walls. Cheap enough for Quest and single-pass stereo safe.
Shader "MomsBathroom/BoxProjectedMirror"
{
    Properties
    {
        _Cube ("Room Cubemap", Cube) = "" {}
        _Tint ("Tint", Color) = (0.92, 0.93, 0.93, 1)
        _BoxMin ("Box Min (world)", Vector) = (0, 0, 0, 0)
        _BoxMax ("Box Max (world)", Vector) = (3.2, 2.44, 3.12, 0)
        _ProbePos ("Probe Position (world)", Vector) = (1.6, 1.32, 1.56, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            samplerCUBE _Cube;
            float4 _Tint, _BoxMin, _BoxMax, _ProbePos;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 refl : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.refl = reflect(normalize(o.worldPos - _WorldSpaceCameraPos), n);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 r = normalize(i.refl);
                float3 tMax = (_BoxMax.xyz - i.worldPos) / r;
                float3 tMin = (_BoxMin.xyz - i.worldPos) / r;
                float3 t = (r > 0) ? tMax : tMin;
                float dist = min(min(t.x, t.y), t.z);
                float3 dir = i.worldPos + r * dist - _ProbePos.xyz;
                return fixed4(texCUBE(_Cube, dir).rgb * _Tint.rgb, 1);
            }
            ENDCG
        }
    }
}
