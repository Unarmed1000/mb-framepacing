#version 100
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, GLSL ES 1.00 (OpenGL ES 2.0, WebGL 1): the marker's quad. There is no gl_VertexID, so draw a vertex buffer of its 4
// corners, (0, 0) (1, 0) (0, 1) (1, 1), as GL_TRIANGLE_STRIP (attribute 'corner', 2 floats each), with frame_marker_packed.frag or
// frame_marker_modules.frag. The uniforms are shared with the fragment shader.

attribute vec2 corner;

uniform vec2 outputSize;        // the render target, in pixels
uniform vec2 origin;            // the marker's top-left pixel (RecommendedOrigin), +y down
uniform float moduleSizePx;     // Options::ModuleSizePx
uniform float quietZoneModules; // Options::QuietZoneModules
uniform float size;             // modules per side: 41, or 25 for the sync marker

varying vec2 uv; // the marker-local pixel coordinate: (0, 0) top-left, +y down

void main()
{
  float markerSizePx = (size + 2.0 * quietZoneModules) * moduleSizePx;
  uv = corner * markerSizePx;
  vec2 pixel = origin + uv;
  gl_Position = vec4(pixel.x / outputSize.x * 2.0 - 1.0, 1.0 - pixel.y / outputSize.y * 2.0, 0.0, 1.0);
}
