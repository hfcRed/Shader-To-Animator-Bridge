<div align="center">

# Shader To Animator Bridge

[![Generic badge](https://img.shields.io/badge/Unity-2022.3.22f1-informational.svg)](https://unity.com/releases/editor/whats-new/2022.3.22)
[![Generic badge](https://img.shields.io/badge/SDK-AvatarSDK3-lightblue.svg)](https://vrchat.com/home/download)
[![Generic badge](https://img.shields.io/github/license/hfcRed/Shader-To-Animator-Bridge)](https://github.com/hfcRed/Shader-To-Animator-Bridge/blob/main/LICENSE)
[![Generic badge](https://img.shields.io/github/downloads/hfcRed/Shader-To-Animator-Bridge/total)](https://github.com/hfcRed/Shader-To-Animator-Bridge/releases)
[![Generic badge](https://img.shields.io/twitter/follow/hfcRedddd?style=social)](https://twitter.com/hfcRedddd)

Send a value from a shader to your avatar's animator.

[![Generic badge](https://img.shields.io/badge/-Download_Unitypackage_​_​_​-success?style=for-the-badge)](https://github.com/hfcRed/Shader-To-Animator-Bridge/releases/latest)

[![Generic badge](https://img.shields.io/badge/-Add_To_Creator_Companion​-informational?style=for-the-badge)](https://hfcred.github.io/VPM-Listing/)

</div>

---

All you have to write is one function:

```hlsl
// 0..1
float BridgeValue()
{
    return /* your logic */;
}
```

and your animator gets the result as a parameter.

The included tool creates a self-contained instance that you can bundle with your package.

## How it works

Since you can't script on avatars, sending data from the GPU to the CPU is not possible, but the GPU can make a frame take longer, and an animator can measure frame times. When the value changes, the shader spends a little extra GPU time on one frame, and the animator picks up on the slow frame one bit at a time. When nothing changes, the cost of the system is effectively zero.

## Quick start

1. Open **hfcRed/Tools/Shader To Animator Bridge**.
2. Set **Shader** to **Light Level (Example)**, pick your avatar and press **Create And Install**.
3. Enter play mode and watch `ShaderBridge/Value` in your FX controller follow how brightly the avatar is lit.

## Making your own

1. Pick a name and the number of **Bits**, set **Shader** to **Template (Value Slider)** and **Output Folder** to whatever folder you like, then press **Create Instance** (or **Create And Install** to test it out).
2. The template sends its **Value** slider. Drag the slider on `<Name> Emitter.mat` in play mode and watch `<Prefix>/Value` follow.
3. Replace `BridgeValue()` in `Emitter/<Name> Emitter.shader`. Creating the instance again wont overwrite this file.
4. Use the output value for your animator logic.
5. If you want to bundle the system with a package, delete the `Avatars` subfolder if there is one, it only holds files the installer made for your test avatar.

### Writing `BridgeValue()`

- Return 0..1. It gets rounded to one of 2^bits levels.
- Use the same value for the whole frame. Use uniforms, globals or textures sampled with `tex2Dlod` at fixed coordinates, instead of something that depends on the vertex or pixel.
- Keep the bridge parts of the template. The hidden properties, queue tag, render states, `BridgeVert` / `BridgeFrag` and the `ShaderBridge.cginc` include. To use a shader you already have, add those and pick **Existing Shader** before instancing.
- Hysteresis (`_BridgeHysteresis`, 0.25 levels by default) stops flickering when the value sits right between two levels. A 1 bit system turns on above 0.75 and off below 0.25. Set it to 0 for a plain 0.5 threshold.

## Parameters

| Parameter | Type | |
|---|---|---|
| `<Prefix>/Value` | Float | The value, 0..1. Wearer only. |
| `<Prefix>/Level` | Float | The same as a whole number, 0..2^bits−1. |
| `<Prefix>/RawValue`, `<Prefix>/RawLevel` | Float | Faster, but can briefly show in-between values. Same as `Value`/`Level` without Stable Output. |
| `<Prefix>/Synced` | Bool or Int | Synced copy for everyone. |

All other `<Prefix>/...` parameters are internal.

## Good to know

- It's slow. A flag takes around 10 to 25 frames to update and a 4 bit value around 50 to 60, roughly doubled with Stable Output.
- One bridge per avatar. Bridges see each other as noise. To send several values, use one system with more bits.

## Performance stats

```c++
FX Animator Layers:      11 + bits (one less without sync)
Skinned Meshes:          1
Material Slots:          1
Synced Parameter Bits:   1 for 1 bit, 8 for more (0 without sync)
```

## Hierarchy layout

```html
Avatar
└── <Name>
    └── Emitter
```

## Support

If you run into problems or need help, message me on Discord: hfcRed

## License

Shader To Animator Bridge is under the [MIT License](https://github.com/hfcRed/Shader-To-Animator-Bridge/blob/main/LICENSE) and free to use and redistribute. The systems you make with it are yours to ship however you like.
