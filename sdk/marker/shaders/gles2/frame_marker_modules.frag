#version 100
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, GLSL ES 1.00 (OpenGL ES 2.0, WebGL 1): looks each module up in a 41 x 41 texture, one texel per module (1681 bytes per
// frame). Per frame, fill it with ModulesToBitmap(matrix, {1, 0}, {0, 0}, texels, 41, 41, PixelFormat::Gray8) and upload it as
// GL_LUMINANCE, GL_UNSIGNED_BYTE (GL_NEAREST, GL_CLAMP_TO_EDGE, no mip maps; glPixelStorei(GL_UNPACK_ALIGNMENT, 1)), row 0 the symbol's
// top row: texture coordinate (column + 0.5, row + 0.5) / 41 reads the rows as uploaded. A sync marker uses its top-left 25 x 25.
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
uniform highp sampler2D modules;

varying vec2 uv;

void main()
{
  vec2 module = floor(uv / moduleSizePx) - quietZoneModules;
  float luma = 1.0;
  if (module.x >= 0.0 && module.y >= 0.0 && module.x < size && module.y < size)
    luma = texture2D(modules, (module + 0.5) / 41.0).r < 0.5 ? 0.0 : 1.0;
  gl_FragColor = vec4(luma, luma, luma, 1.0);
}
