Shader "hfcRed/Shader To Animator Bridge/Light Level (Example)"
{
    Properties
    {
        _BridgeHysteresis ("Bridge Hysteresis (Levels)", Range(0, 0.45)) = 0.25

        [HideInInspector] _BridgeBits ("Bridge Bits", Float) = 1
        [HideInInspector] _BurnBudget ("Burn Budget", Float) = 1000
        [HideInInspector] _Force ("Force", Float) = 0
        [HideInInspector] _Armed0 ("Armed 0", Float) = 0
        [HideInInspector] _Armed1 ("Armed 1", Float) = 0
        [HideInInspector] _Armed2 ("Armed 2", Float) = 0
        [HideInInspector] _Armed3 ("Armed 3", Float) = 0
        [HideInInspector] _Armed4 ("Armed 4", Float) = 0
        [HideInInspector] _Armed5 ("Armed 5", Float) = 0
        [HideInInspector] _Armed6 ("Armed 6", Float) = 0
        [HideInInspector] _Armed7 ("Armed 7", Float) = 0
        [HideInInspector] _State0 ("State 0", Float) = 0
        [HideInInspector] _State1 ("State 1", Float) = 0
        [HideInInspector] _State2 ("State 2", Float) = 0
        [HideInInspector] _State3 ("State 3", Float) = 0
        [HideInInspector] _State4 ("State 4", Float) = 0
        [HideInInspector] _State5 ("State 5", Float) = 0
        [HideInInspector] _State6 ("State 6", Float) = 0
        [HideInInspector] _State7 ("State 7", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay+1000" "RenderType"="Overlay" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex BridgeVert
            #pragma fragment BridgeFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ VERTEXLIGHT_ON

            #include "ShaderBridge.cginc"
            #include "UnityLightingCommon.cginc"

            float BridgeValue()
            {
                float3 light = max(0, ShadeSH9(float4(0, 0, 0, 1)));

                light += _LightColor0.rgb;

                #if defined(VERTEXLIGHT_ON)
                    float3 pos = unity_ObjectToWorld._m03_m13_m23;
                    [unroll] for (int i = 0; i < 4; i++)
                    {
                        float3 d = float3(unity_4LightPosX0[i], unity_4LightPosY0[i], unity_4LightPosZ0[i]) - pos;
                        light += unity_LightColor[i].rgb / (1 + dot(d, d) * unity_4LightAtten0[i]);
                    }
                #endif

                return dot(light, float3(0.2126, 0.7152, 0.0722));
            }
            ENDCG
        }
    }
}
