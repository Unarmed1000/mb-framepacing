// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker: complete shaders that draw the marker as one opaque quad (Direct3D 11 and 12; Vulkan through dxc -spirv, the vertex
// shader with -fvk-invert-y). See ../README.md for how to draw them.
//
//   FrameMarkerVS         the quad from the vertex id: draw 4 vertices as a triangle strip, no vertex or index buffer
//   FrameMarkerPackedPS   reads the module matrix's packed bits from the constant buffer (211 bytes per frame; the fastest)
//   FrameMarkerModulesPS  reads a 41 x 41 R8 texture, one texel per module (1681 bytes per frame)
//
// Per frame: encode the marker (GenerateModules), copy ModuleMatrix::Bits() as they are into Bits (the rest stays zero), set the rest of
// the constants, and draw last, after post effects and UI, with blending, depth test and culling off. A sync marker is a second draw with
// its own constants (Size 25, its origin). Compile: fxc /T vs_4_0 /E FrameMarkerVS, fxc /T ps_4_0 /E FrameMarkerPackedPS (or dxc
// -T vs_6_0 / ps_6_0).

#include "FrameMarker.hlsl"

cbuffer FrameMarkerConstants : register(b0)
{
  float2 OutputSize;       // the render target, in pixels
  float2 Origin;           // the marker's top-left pixel (RecommendedOrigin), +y down
  float ModuleSizePx;      // Options::ModuleSizePx
  float QuietZoneModules;  // Options::QuietZoneModules
  float Size;              // modules per side: 41, or 25 for the sync marker
  float Unused;
  uint4 Bits[14];          // FrameMarkerPackedPS: the packed module bits, 211 bytes (79 for the sync marker) copied as they are
};

Texture2D<float> Modules : register(t0);  // FrameMarkerModulesPS: ModulesToBitmap(matrix, {1, 0}, {0, 0}, ..., 41, 41, Gray8), R8

struct FrameMarkerVaryings
{
  float4 Position : SV_Position;
  float2 Uv : TEXCOORD0;  // the marker-local pixel coordinate
};

FrameMarkerVaryings FrameMarkerVS(uint id : SV_VertexID)
{
  // Vertex 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
  float2 corner = float2(id & 1u, id >> 1u);
  float markerSizePx = (Size + (2.0 * QuietZoneModules)) * ModuleSizePx;
  FrameMarkerVaryings output;
  output.Uv = corner * markerSizePx;
  float2 pixel = Origin + output.Uv;
  output.Position = float4(((pixel.x / OutputSize.x) * 2.0) - 1.0, 1.0 - ((pixel.y / OutputSize.y) * 2.0), 0.0, 1.0);
  return output;
}

float4 FrameMarkerPackedPS(FrameMarkerVaryings input) : SV_Target
{
  int2 module;
  if (!FrameMarkerModuleAt(input.Uv, ModuleSizePx, QuietZoneModules, Size, module))
    return FrameMarkerColour(false);
  int index = FrameMarkerModuleIndex(module, Size);
  int byteIndex = index >> 3;
  // 16 bytes per uint4, 4 per word
  uint word = Bits[byteIndex >> 4][(byteIndex >> 2) & 3];
  return FrameMarkerColour(FrameMarkerIsDark(FrameMarkerByteOfWord(word, byteIndex), index));
}

float4 FrameMarkerModulesPS(FrameMarkerVaryings input) : SV_Target
{
  int2 module;
  if (!FrameMarkerModuleAt(input.Uv, ModuleSizePx, QuietZoneModules, Size, module))
    return FrameMarkerColour(false);
  return FrameMarkerColour(Modules.Load(int3(module, 0)) < 0.5);
}
