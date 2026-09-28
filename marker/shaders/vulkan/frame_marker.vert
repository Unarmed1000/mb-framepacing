#version 450
// SPDX-License-Identifier: BSD-3-Clause
//
// MB Frame Marker, Vulkan (GLSL 4.50, compile to SPIR-V with glslangValidator -V or glslc): the marker's quad from the vertex index. Draw 4
// vertices as VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP with no vertex or index buffer, with frame_marker_packed.frag or
// frame_marker_modules.frag. Vulkan's clip space has +y down, the same way as the marker's pixels, so there is no flip.

// The same block as the fragment shaders' (std140; the layout of FrameMarkerShaders.hlsl's constant buffer)
layout(set = 0, binding = 0, std140) uniform FrameMarkerConstants
{
  vec2 outputSize;        // the render target, in pixels
  vec2 origin;            // the marker's top-left pixel (RecommendedOrigin), +y down
  float moduleSizePx;     // Options::ModuleSizePx
  float quietZoneModules; // Options::QuietZoneModules
  float size;             // modules per side: 41, or 25 for the sync marker
  float unused;
  uvec4 bits[14];         // frame_marker_packed.frag: the packed module bits
} constants;

layout(location = 0) out vec2 uv; // the marker-local pixel coordinate: (0, 0) top-left, +y down

void main()
{
  // Vertex 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
  vec2 corner = vec2(float(gl_VertexIndex & 1), float(gl_VertexIndex >> 1));
  float markerSizePx = (constants.size + 2.0 * constants.quietZoneModules) * constants.moduleSizePx;
  uv = corner * markerSizePx;
  vec2 pixel = constants.origin + uv;
  gl_Position = vec4(pixel.x / constants.outputSize.x * 2.0 - 1.0, pixel.y / constants.outputSize.y * 2.0 - 1.0, 0.0, 1.0);
}
