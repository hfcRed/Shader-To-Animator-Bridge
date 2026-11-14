Shader "hfcRed/Shader To Animator Bridge/Template"
{
    Properties
    {
        _Value ("Value", Range(0, 1)) = 0
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
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex BridgeVert
            #pragma fragment BridgeFrag
            #pragma multi_compile_instancing

            #include "ShaderBridge.cginc"

            float _Value;

            float BridgeValue()
            {
                return _Value;
            }
            ENDCG
        }
    }
}
