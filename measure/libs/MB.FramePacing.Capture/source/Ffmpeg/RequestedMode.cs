//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A requested mode: "1920x1080@240", "1920x1080" or "@120".
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>A requested mode: "1920x1080@240", "1920x1080" or "@120".</summary>
  public readonly record struct RequestedMode(int Width, int Height, double Fps)
  {
    public bool HasSize => Width > 0 && Height > 0;
    public bool HasFps => Fps > 0;

    public static RequestedMode Parse(string text)
    {
      int at = text.IndexOf('@');
      string size = at >= 0 ? text.Substring(0, at) : text;
      string fps = at >= 0 ? text.Substring(at + 1) : string.Empty;
      int width = 0;
      int height = 0;
      if (size.Length > 0)
        (width, height) = ParseSize(size, text);
      double rate = 0;
      if (fps.Length > 0 && (!double.TryParse(fps, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || rate <= 0))
        throw new FormatException($"Invalid frame rate in mode '{text}'");
      return new RequestedMode(width, height, rate);
    }

    public static (int Width, int Height) ParseSize(string size, string context)
    {
      var parts = size.ToLowerInvariant().Split('x');
      if (
        parts.Length != 2
        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height)
        || width <= 0
        || height <= 0
      )
        throw new FormatException($"Expected a size as WIDTHxHEIGHT in '{context}'");
      return (width, height);
    }
  }
}
