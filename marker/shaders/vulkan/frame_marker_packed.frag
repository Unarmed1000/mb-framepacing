#version 450
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, Vulkan (GLSL 4.50): reads each module's bit from the module matrix's packed bits in the uniform buffer (211 bytes per
// frame; the fastest). Per frame, copy ModuleMatrix::Bits() as they are into 'bits' (224 bytes, the rest zero). Module i (row * size +
// column) is bit 7 - (i % 8) of byte i / 8, most significant first; a set bit is dark. The words hold 4 bytes each, little endian (the
// first byte in the lowest 8 bits), as a plain copy on every platform the tools run on.

layout(set = 0, binding = 0, std140) uniform FrameMarkerConstants
{
  vec2 outputSize;
  vec2 origin;
  float moduleSizePx;
  float quietZoneModules;
  float size;
  float unused;
  uvec4 bits[14];
} constants;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 color;

void main()
{
  int n = int(constants.size);
  ivec2 module = ivec2(floor(uv / constants.moduleSizePx)) - int(constants.quietZoneModules);
  float luma = 1.0;
  if (all(greaterThanEqual(module, ivec2(0))) && all(lessThan(module, ivec2(n))))
  {
    int index = module.y * n + module.x;
    int byteIndex = index >> 3;
    uint word = constants.bits[byteIndex >> 4][(byteIndex >> 2) & 3];
    uint packedByte = (word >> (8u * uint(byteIndex & 3))) & 0xFFu;
    luma = ((packedByte >> uint(7 - (index & 7))) & 1u) != 0u ? 0.0 : 1.0;
  }
  color = vec4(luma, luma, luma, 1.0);
}
