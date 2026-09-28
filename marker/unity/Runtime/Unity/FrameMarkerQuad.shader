// SPDX-License-Identifier: BSD-3-Clause
//
// The marker as one opaque quad: the fragment looks its module up in a module texture (FrameMarkerQuad fills it). The quad's UV is the
// marker-local pixel coordinate, top-left (0, 0), +y down, so a fragment at a pixel centre gets (px + 0.5, py + 0.5) on every platform and
// floor(uv / module size) is exact. Texture.Load needs shader model 3.5 (ES 3, Metal, Vulkan, D3D11).
Shader "Hidden/MB/FrameMarkerQuad"
{
  Properties
  {
    _Modules ("Modules (R8, one texel per module, top row first)", 2D) = "white" {}
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
      #include "UnityCG.cginc"

      Texture2D _Modules;
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
        int2 module = int2(floor(input.uv / _ModuleSizePx)) - int(_QuietZoneModules);
        float luma = 1.0;
        if (all(module >= 0) && all(module < int(_Size)))
          luma = _Modules.Load(int3(module, 0)).r < 0.5 ? 0.0 : 1.0;
        return fixed4(luma, luma, luma, 1.0);
      }
      ENDCG
    }
  }
}
