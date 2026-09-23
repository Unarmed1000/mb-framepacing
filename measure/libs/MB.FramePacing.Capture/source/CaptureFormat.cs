//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The stored frame format a source delivers.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  /// <summary>The stored frame format a source delivers.</summary>
  /// <param name="Width">Stored frame width (after crop and scale).</param>
  /// <param name="Height">Stored frame height (after crop and scale).</param>
  /// <param name="SourceWidth">Device mode width before crop/scale, 0 if unknown.</param>
  /// <param name="SourceHeight">Device mode height before crop/scale, 0 if unknown.</param>
  /// <param name="Roi">Crop in source pixels applied before scaling, empty if none.</param>
  public sealed record CaptureFormat(int Width, int Height, FrameRate FrameRate, int SourceWidth = 0, int SourceHeight = 0, PixelRect Roi = default)
  {
    public int PixelByteCount => checked(Width * Height);

    public CaptureFileHeader ToFileHeader() => new CaptureFileHeader(Width, Height, FrameRate, SourceWidth, SourceHeight, Roi);
  }
}
