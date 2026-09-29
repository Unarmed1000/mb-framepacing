// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker as one opaque quad, read from its packed module bits: the module matrix's bytes as they are (1 bit per module, row-major,
// most significant bit first, continuous across rows; 211 bytes for the 41x41 main marker, 79 for the sync marker) in a 211 x 1 R8 texture
// (FrameMarkerQuad fills it). Per frame only those bytes change, an eighth of the one-texel-per-module texture. The quad's UV is the
// marker-local pixel coordinate, top-left (0, 0), +y down, as in FrameMarkerQuad.shader; the lookup is the reference shaders'
// (marker/shaders/FrameMarker.hlsl, copied into the package). Texture.Load and the bit operations need shader model 3.5 with integers
// (ES 3, Metal, Vulkan, D3D11).
Shader "Hidden/MB/FrameMarkerQuadPacked"
{
  Properties
  {
    _Bits ("Packed module bits (R8, 211 x 1)", 2D) = "white" {}
    _ModuleSizePx ("Module size (pixels)", Float) = 6
    _QuietZoneModules ("Quiet zone (modules)", Float) = 4
    _Size ("Modules per side", Float) = 41
  }
  SubShader
  {
    Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
    Pass
    {
      Blend Off
      ZWrite Off
      ZTest Always
      Cull Off

      CGPROGRAM
      #pragma vertex Vertex
      #pragma fragment Fragment
      #pragma target 3.5
      #pragma require integers
      #include "UnityCG.cginc"
      #include "FrameMarker.hlsl"

      Texture2D _Bits;
      float _ModuleSizePx;
      float _QuietZoneModules;
      float _Size;

      struct Attributes
      {
        float4 position : POSITION;
        float2 uv : TEXCOORD0;
      };

      struct Varyings
      {
        float4 position : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      Varyings Vertex(Attributes input)
      {
        Varyings output;
        output.position = UnityObjectToClipPos(input.position);
        output.uv = input.uv;
        return output;
      }

      fixed4 Fragment(Varyings input) : SV_Target
      {
        int2 module;
        if (!FrameMarkerModuleAt(input.uv, _ModuleSizePx, _QuietZoneModules, _Size, module))
          return FrameMarkerColour(false);
        // Unity has no unsigned integer arrays for materials: the packed bytes come in an R8 texture, one texel per byte
        int index = FrameMarkerModuleIndex(module, _Size);
        uint packedByte = (uint)(_Bits.Load(int3(index >> 3, 0, 0)).r * 255.0 + 0.5);
        return FrameMarkerColour(FrameMarkerIsDark(packedByte, index));
      }
      ENDCG
    }
  }
}
