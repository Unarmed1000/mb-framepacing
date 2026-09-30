#version 450
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, Vulkan (GLSL 4.50): looks each module up in a 41 x 41 VK_FORMAT_R8_UNORM image, one texel per module (1681 bytes per
// frame), bound as a combined image sampler (nearest). Per frame, fill it with ModulesToBitmap(matrix, Options(1, 0), {0, 0}, texels, 41,
// 41, PixelFormat::R8), row 0 the symbol's top row; texelFetch reads the rows as uploaded. A sync marker uses its top-left 25 x 25.

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

layout(set = 0, binding = 1) uniform sampler2D modules;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 color;

void main()
{
  ivec2 module = ivec2(floor(uv / constants.moduleSizePx)) - int(constants.quietZoneModules);
  float luma = 1.0;
  if (all(greaterThanEqual(module, ivec2(0))) && all(lessThan(module, ivec2(int(constants.size)))))
    luma = texelFetch(modules, module, 0).r < 0.5 ? 0.0 : 1.0;
  color = vec4(luma, luma, luma, 1.0);
}
