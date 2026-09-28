#version 100
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, GLSL ES 1.00 (OpenGL ES 2.0, WebGL 1): reads each module's bit from the module matrix's packed bits (211 bytes per
// frame). GLSL ES 1.00 has no integers to shift, so the bit comes from float arithmetic, exact for these small whole numbers; and it has no
// large or dynamically indexed uniform arrays, so the bytes come in a texture. Per frame, upload ModuleMatrix::Bits() as they are into a
// 211 x 1 GL_LUMINANCE, GL_UNSIGNED_BYTE texture (GL_NEAREST, GL_CLAMP_TO_EDGE, no mip maps; glPixelStorei(GL_UNPACK_ALIGNMENT, 1)), the
// rest zero. Module i (row * size + column) is bit 7 - (i % 8) of byte i / 8, most significant first; a set bit is dark.
// It needs highp in the fragment shader: with 32 bit floats every value here is exact, with mediump (all OpenGL ES 2.0 guarantees; some
// GPUs, such as Mali-400, have no more) a pixel near a module edge can land in the wrong module. Without highp it does not compile: draw the
// marker as geometry instead (the static grid with 16 bit indices, or triangles), which is exact on every GPU.

#ifndef GL_FRAGMENT_PRECISION_HIGH
#error MB Frame Marker: this shader needs highp in the fragment shader; draw the marker as geometry instead
#endif
precision highp float;

uniform float moduleSizePx;
uniform float quietZoneModules;
uniform float size;
uniform highp sampler2D bits;

varying vec2 uv;

void main()
{
  vec2 module = floor(uv / moduleSizePx) - quietZoneModules;
  float luma = 1.0;
  if (module.x >= 0.0 && module.y >= 0.0 && module.x < size && module.y < size)
  {
    float index = module.y * size + module.x;
    float byteIndex = floor(index / 8.0);
    float packedByte = floor(texture2D(bits, vec2((byteIndex + 0.5) / 211.0, 0.5)).r * 255.0 + 0.5);
    float bit = mod(floor(packedByte / exp2(7.0 - (index - byteIndex * 8.0))), 2.0);
    luma = bit > 0.5 ? 0.0 : 1.0;
  }
  gl_FragColor = vec4(luma, luma, luma, 1.0);
}
