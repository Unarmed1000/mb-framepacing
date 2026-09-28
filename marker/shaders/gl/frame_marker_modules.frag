#version 330 core
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker: looks each module up in a 41 x 41 single channel texture, one texel per module (1681 bytes per frame). Per frame, fill
// it with ModulesToBitmap(matrix, {1, 0}, {0, 0}, texels, 41, 41, PixelFormat::Gray8) and upload it as GL_R8 (no filtering, no mip
// maps), row 0 the symbol's top row; texelFetch reads the rows as uploaded, so it needs no flip. A sync marker uses its top-left 25 x 25.
// OpenGL ES 3.0: replace the first line with "#version 300 es" followed by "precision highp float; precision highp int;".

uniform float moduleSizePx;
uniform float quietZoneModules;
uniform float size;
uniform sampler2D modules;

in vec2 uv;
out vec4 color;

void main()
{
  ivec2 module = ivec2(floor(uv / moduleSizePx)) - int(quietZoneModules);
  float luma = 1.0;
  if (all(greaterThanEqual(module, ivec2(0))) && all(lessThan(module, ivec2(int(size)))))
    luma = texelFetch(modules, module, 0).r < 0.5 ? 0.0 : 1.0;
  color = vec4(luma, luma, luma, 1.0);
}
