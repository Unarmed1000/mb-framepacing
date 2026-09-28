// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker: the module lookup of a dedicated marker shader, shared by FrameMarkerShaders.hlsl and the Unity package's shaders.
//
// The marker is one opaque quad whose texture coordinate is the marker-local pixel coordinate: (0, 0) top-left, +y down, whatever the
// API's y axis. A fragment at a pixel centre then gets (px + 0.5, py + 0.5), and floor(uv / module size) is exact on every platform.
// Needs integer operations (shader model 4.0, or Unity's target 3.5 with integers).

#ifndef MB_FRAME_MARKER_HLSL
#define MB_FRAME_MARKER_HLSL

// The module under the marker-local pixel coordinate uv: (column, row) of the symbol, false in the quiet zone (light).
bool FrameMarkerModuleAt(float2 uv, float moduleSizePx, float quietZoneModules, float size, out int2 module)
{
  module = int2(floor(uv / moduleSizePx)) - (int)quietZoneModules;
  return all(module >= 0) && all(module < (int)size);
}

// Module (column, row)'s index in the module matrix: row * size + column.
int FrameMarkerModuleIndex(int2 module, float size)
{
  return (module.y * (int)size) + module.x;
}

// Whether module `index` is dark, from the packed byte that holds it (byte index / 8): bit 7 - (index % 8), most significant first.
bool FrameMarkerIsDark(uint packedByte, int index)
{
  return ((packedByte >> (7u - (uint)(index & 7))) & 1u) != 0u;
}

// Packed byte `byteIndex` of the module matrix's bytes copied as they are into 32 bit words (little endian: the word holds bytes
// byteIndex & ~3 .. + 3, the first in its lowest 8 bits).
uint FrameMarkerByteOfWord(uint word, int byteIndex)
{
  return (word >> (8u * (uint)(byteIndex & 3))) & 0xFFu;
}

// The colour of a module: pure black or pure white, opaque.
float4 FrameMarkerColour(bool dark)
{
  float luma = dark ? 0.0 : 1.0;
  return float4(luma, luma, luma, 1.0);
}

#endif
