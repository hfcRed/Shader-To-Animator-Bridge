#ifndef SHADER_BRIDGE_INCLUDED
#define SHADER_BRIDGE_INCLUDED

#include "UnityCG.cginc"

float _BridgeBits;
float _BridgeHysteresis;
float _Force;
float _BurnBudget;
float _Armed0, _Armed1, _Armed2, _Armed3, _Armed4, _Armed5, _Armed6, _Armed7;
float _State0, _State1, _State2, _State3, _State4, _State5, _State6, _State7;

float BridgeValue();

uint BridgeBits()
{
    return (uint)clamp(_BridgeBits, 1, 8);
}

uint BridgeBelievedLevel()
{
    uint g = (_State0 > 0.5 ? 1u : 0u) | (_State1 > 0.5 ? 2u : 0u) | (_State2 > 0.5 ? 4u : 0u) | (_State3 > 0.5 ? 8u : 0u)
        | (_State4 > 0.5 ? 16u : 0u) | (_State5 > 0.5 ? 32u : 0u) | (_State6 > 0.5 ? 64u : 0u) | (_State7 > 0.5 ? 128u : 0u);
    g &= (1u << BridgeBits()) - 1;

    g ^= g >> 1;
    g ^= g >> 2;
    g ^= g >> 4;
    return g;
}

uint BridgeGray()
{
    uint maxValue = (1u << BridgeBits()) - 1;
    float x = saturate(BridgeValue()) * maxValue;
    uint q = (uint)round(x);

    uint held = BridgeBelievedLevel();
    if (abs(x - (float)held) < 0.5 + max(_BridgeHysteresis, 0)) q = held;
    return q ^ (q >> 1);
}

bool BridgeMismatch(float armed, float state, uint gray, uint channel)
{
    return channel < BridgeBits() && armed > 0.5 && ((gray >> channel) & 1u) != (state > 0.5 ? 1u : 0u);
}

float _VRChatMirrorMode;
float _VRChatCameraMode;

bool BridgeShouldSignal()
{
    if (_VRChatMirrorMode != 0 || _VRChatCameraMode != 0) return false;
    if (_ScreenParams.x * _ScreenParams.y < 256 * 256) return false;
    if (_Force > 0.5) return true;

    float armedSum = _Armed0 + _Armed1 + _Armed2 + _Armed3 + _Armed4 + _Armed5 + _Armed6 + _Armed7;
    if (armedSum < 0.5) return false;

    uint g = BridgeGray();
    return BridgeMismatch(_Armed0, _State0, g, 0) || BridgeMismatch(_Armed1, _State1, g, 1)
        || BridgeMismatch(_Armed2, _State2, g, 2) || BridgeMismatch(_Armed3, _State3, g, 3)
        || BridgeMismatch(_Armed4, _State4, g, 4) || BridgeMismatch(_Armed5, _State5, g, 5)
        || BridgeMismatch(_Armed6, _State6, g, 6) || BridgeMismatch(_Armed7, _State7, g, 7);
}

struct BridgeAppdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct BridgeV2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

BridgeV2f BridgeVert(BridgeAppdata v, uint id : SV_VertexID)
{
    BridgeV2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(BridgeV2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float2 corner = float2(id & 1 ? 1 : -1, id & 2 ? 1 : -1);
    o.pos = BridgeShouldSignal() ? float4(corner, 0.5, 1) : float4(0, 0, 0, 0);
    return o;
}

float4 BridgeFrag(BridgeV2f i) : SV_Target
{
    uint n = min((uint)(_BurnBudget * 1e6 / max(_ScreenParams.x * _ScreenParams.y, 1)), 200000u);
    float x = frac(i.pos.x * 0.618 + i.pos.y * 0.382);
    [loop] for (uint k = 0; k < n; k++)
        x = frac(sin(x * 12.9898 + k) * 43758.5453);

    return float4(x, x, x, 0) * 1e-7;
}

#endif
