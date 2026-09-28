#version 330 core
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker: reads each module's bit from the module matrix's packed bits (211 bytes per frame; the fastest). Per frame, copy
// ModuleMatrix::Bits() as they are into 14 uvec4 (224 bytes, the rest zero) and set them with glUniform4uiv(location, 14, words).
// Module i (row * size + column) is bit 7 - (i % 8) of byte i / 8, most significant first; a set bit is dark. The words hold 4 bytes
// each, little endian (the first byte in the lowest 8 bits), as a plain copy on every platform the tools run on.
// OpenGL ES 3.0: replace the first line with "#version 300 es" followed by "precision highp float; precision highp int;".

uniform float moduleSizePx;
uniform float quietZoneModules;
uniform float size;
uniform uvec4 bits[14];

in vec2 uv;
out vec4 color;

void main()
{
  int n = int(size);
  ivec2 module = ivec2(floor(uv / moduleSizePx)) - int(quietZoneModules);
  float luma = 1.0;
  if (all(greaterThanEqual(module, ivec2(0))) && all(lessThan(module, ivec2(n))))
  {
    int index = module.y * n + module.x;
    int byteIndex = index >> 3;
    uint word = bits[byteIndex >> 4][(byteIndex >> 2) & 3];
    uint packedByte = (word >> (8u * uint(byteIndex & 3))) & 0xFFu;
    luma = ((packedByte >> uint(7 - (index & 7))) & 1u) != 0u ? 0.0 : 1.0;
  }
  color = vec4(luma, luma, luma, 1.0);
}
