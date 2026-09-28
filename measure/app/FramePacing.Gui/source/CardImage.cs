//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Saves a report card as the GUI draws it: SVG through SvgCardWriter (the same file the command line writes), or PNG by rendering a CardView
//* offscreen at twice the card's size (no browser needed).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using MB.FramePacing.Charts;
using MB.FramePacing.Gui.Views;

namespace MB.FramePacing.Gui
{
  public static class CardImage
  {
    /// <summary>Write <paramref name="drawing"/> to <paramref name="path"/>: PNG for a .png file, SVG otherwise.</summary>
    public static void Save(CardDrawing drawing, string path)
    {
      if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        SavePng(drawing, path);
      else
        File.WriteAllText(path, SvgCardWriter.Write(drawing), new UTF8Encoding(false));
    }

    /// <summary>The card rendered by a CardView at <paramref name="scale"/> times its size, as a PNG file.</summary>
    public static void SavePng(CardDrawing drawing, string path, double scale = 2)
    {
      var size = new Size(drawing.Width, drawing.Height);
      var view = new CardView
      {
        Drawing = drawing,
        Width = size.Width,
        Height = size.Height,
      };
      view.Measure(size);
      view.Arrange(new Rect(size));
      var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
      using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
      bitmap.Render(view);
      bitmap.Save(path, new PngBitmapEncoderOptions());
    }
  }
}
