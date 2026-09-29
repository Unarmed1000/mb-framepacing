#version 330 core
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker: the marker's quad from the vertex id. Draw 4 vertices as GL_TRIANGLE_STRIP with an empty vertex array object (no
// vertex or index buffer), with frame_marker_packed.frag or frame_marker_modules.frag. The uniforms are shared with the fragment shader.
// OpenGL ES 3.0: replace the first line with "#version 300 es" followed by "precision highp float; precision highp int;".

uniform vec2 outputSize;        // the render target, in pixels
uniform vec2 origin;            // the marker's top-left pixel (RecommendedOrigin), +y down
uniform float moduleSizePx;     // Options::ModuleSizePx
uniform float quietZoneModules; // Options::QuietZoneModules
uniform float size;             // modules per side: 41, or 25 for the sync marker

out vec2 uv; // the marker-local pixel coordinate: (0, 0) top-left, +y down

void main()
{
  // Vertex 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
  vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
  float markerSizePx = (size + 2.0 * quietZoneModules) * moduleSizePx;
  uv = corner * markerSizePx;
  vec2 pixel = origin + uv;
  gl_Position = vec4(pixel.x / outputSize.x * 2.0 - 1.0, 1.0 - pixel.y / outputSize.y * 2.0, 0.0, 1.0);
}
