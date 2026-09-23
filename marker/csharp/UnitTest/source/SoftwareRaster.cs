//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Software rasterizers for the tests, matching how marker-render draws the golden images: a 128 grey canvas, pixel-edge vertices, drawn
//* in order.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FrameMarker.UnitTest
{
  public static class SoftwareRaster
  {
    /// <summary>Fill pixel (x, y) when Left &lt;= x &lt; Right and Top &lt;= y &lt; Bottom.</summary>
    public static byte[] Quads(IReadOnlyList<Quad> quads, int width, int height)
    {
      var pixels = NewCanvas(width, height);
      foreach (var quad in quads)
      {
        for (int y = Math.Max(quad.Top, 0); y < Math.Min(quad.Bottom, height); ++y)
        {
          for (int x = Math.Max(quad.Left, 0); x < Math.Min(quad.Right, width); ++x)
            pixels[(y * width) + x] = quad.Dark ? (byte)0 : (byte)255;
        }
      }
      return pixels;
    }

    /// <summary>
    /// Sample pixel centres like a GPU: pixel (x, y) is covered when (x + 0.5, y + 0.5) lies inside the triangle or on an edge. With vertices
    /// on pixel corners no centre lies on an axis aligned edge; centres on a quad's diagonal belong to both of its same coloured triangles,
    /// so the tie rule cannot change the image.
    /// </summary>
    public static byte[] Triangles(IReadOnlyList<Vertex> vertices, int width, int height)
    {
      var pixels = NewCanvas(width, height);
      for (int i = 0; i + 2 < vertices.Count; i += 3)
      {
        var v0 = vertices[i];
        var v1 = vertices[i + 1];
        var v2 = vertices[i + 2];
        int minX = Math.Max(0, Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int maxX = Math.Min(width, Math.Max(v0.X, Math.Max(v1.X, v2.X)));
        int minY = Math.Max(0, Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
        int maxY = Math.Min(height, Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)));
        for (int y = minY; y < maxY; ++y)
        {
          for (int x = minX; x < maxX; ++x)
          {
            // Doubled coordinates keep the pixel centres integral
            long px = (2L * x) + 1;
            long py = (2L * y) + 1;
            long e0 = Edge(v0, v1, px, py);
            long e1 = Edge(v1, v2, px, py);
            long e2 = Edge(v2, v0, px, py);
            if ((e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0))
              pixels[(y * width) + x] = v0.Luma;
          }
        }
      }
      return pixels;
    }

    /// <summary>Expand an indexed triangle list into a plain one.</summary>
    public static Vertex[] Expand(Vertex[] vertices, int[] indices, int indexCount, int baseVertex)
    {
      var expanded = new Vertex[indexCount];
      for (int i = 0; i < indexCount; ++i)
        expanded[i] = vertices[indices[i] - baseVertex];
      return expanded;
    }

    private static long Edge(Vertex a, Vertex b, long px, long py) => (2L * (b.X - a.X) * (py - (2L * a.Y))) - (2L * (b.Y - a.Y) * (px - (2L * a.X)));

    private static byte[] NewCanvas(int width, int height)
    {
      var pixels = new byte[width * height];
      Array.Fill(pixels, (byte)128);
      return pixels;
    }
  }
}
