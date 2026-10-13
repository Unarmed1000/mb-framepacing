//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Keeps a Texture2D with the marker at module resolution (one texel per module, quiet zone included), for UI or anything that shows an
//* image: a RawImage, Graphics.DrawTexture or a material. Draw it scaled up by a whole number with point filtering, on whole pixels, so
//* every module covers exactly the same pixels as the geometry would. Encode the marker with MarkerGenerator.TryGenerateModules and pass
//* the matrix to Update. Update allocates only when the texture's size changes (another quiet zone, or a sync marker after a main one).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace MB.FramePacing.Marker.Unity
{
  public sealed class FrameMarkerTexture : IDisposable
  {
    private byte[] m_rows = Array.Empty<byte>();
    private byte[] m_pixels = Array.Empty<byte>();

    /// <summary>The marker, RGBA32, point filtered, clamped, without mip maps; null before the first Update and after Dispose.</summary>
    public Texture2D Texture { get; private set; }

    /// <summary>
    /// Fill the texture with the encoded marker and its quiet zone: (matrix.Size + 2 x quiet zone) texels per side. A quiet zone outside
    /// 0 to <see cref="Options.MaxQuietZoneModules"/> is kept within it, as <see cref="Options"/> does. Returns false (and leaves the
    /// texture unchanged) if the matrix is empty.
    /// </summary>
    public bool Update(ModuleMatrix matrix, int quietZoneModules = Options.RecommendedQuietZoneModules)
    {
      var options = new Options(1, quietZoneModules);
      if (matrix.IsEmpty)
        return false;
      // The quiet zone Options kept: the texture is exactly the marker it draws
      int size = matrix.Size + (2 * options.QuietZoneModules);
      int rowBytes = size * 4;
      if (Texture == null || Texture.width != size)
      {
        DestroyTexture();
        Texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
          name = "MB Frame Marker",
          hideFlags = HideFlags.HideAndDontSave,
          filterMode = FilterMode.Point,
          wrapMode = TextureWrapMode.Clamp,
        };
        m_rows = new byte[rowBytes * size];
        m_pixels = new byte[rowBytes * size];
      }
      if (!FrameMarker.ModulesToBitmap(matrix, options, default, m_rows, size, size, PixelFormat.R8G8B8A8))
        return false;
      // The marker's rows run top down, a texture's bottom up
      for (int y = 0; y < size; ++y)
        Array.Copy(m_rows, y * rowBytes, m_pixels, (size - 1 - y) * rowBytes, rowBytes);
      Texture.SetPixelData(m_pixels, 0);
      Texture.Apply(false);
      return true;
    }

    /// <summary>
    /// Draw the texture now into the current render target (<paramref name="outputWidth"/> x <paramref name="outputHeight"/> pixels), scaled
    /// by the module size with its top-left corner at <paramref name="origin"/>: exactly the pixels the geometry would cover.
    /// </summary>
    public void DrawNow(in Options options, Point origin, int outputWidth, int outputHeight)
    {
      if (Texture == null)
        return;
      int size = Texture.width * options.ModuleSizePx;
      GL.PushMatrix();
      GL.LoadPixelMatrix(0f, outputWidth, outputHeight, 0f);
      Graphics.DrawTexture(new Rect(origin.X, origin.Y, size, size), Texture);
      GL.PopMatrix();
    }

    public void Dispose() => DestroyTexture();

    private void DestroyTexture()
    {
      if (Texture == null)
        return;
      if (Application.isPlaying)
        UnityEngine.Object.Destroy(Texture);
      else
        UnityEngine.Object.DestroyImmediate(Texture);
      Texture = null;
    }
  }
}
#endif
