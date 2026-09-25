//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where the marker was found in the source and the region a fast capture stores (FfmpegMarkerLocator).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Globalization;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>Where the marker was found in the source and the region a fast capture stores.</summary>
  /// <param name="SourceLock">The frame marker in source pixels.</param>
  public sealed record MarkerLocateResult(MarkerLock SourceLock, MarkerCropResult Crop, int SourceWidth, int SourceHeight)
  {
    /// <summary>The stored size, or null when the crop is stored at source resolution.</summary>
    public (int Width, int Height)? Scale => Crop.Factor > 1 ? (Crop.StoredWidth, Crop.StoredHeight) : null;

    /// <summary>Bytes per record in frames.mbfc with the crop.</summary>
    public int RecordSize => new CaptureFileHeader(Crop.StoredWidth, Crop.StoredHeight, default).RecordSize;

    /// <summary>Bytes per record in frames.mbfc for whole source frames.</summary>
    public int FullFrameRecordSize => new CaptureFileHeader(SourceWidth, SourceHeight, default).RecordSize;

    /// <summary>The capture options with the crop (and its downscale) applied.</summary>
    public FfmpegCaptureOptions Apply(FfmpegCaptureOptions options) => options with { Roi = Crop.Roi, Scale = Scale };

    /// <summary>Where the marker is and what a fast capture stores, for people.</summary>
    public string Summary =>
      string.Create(
        CultureInfo.InvariantCulture,
        $"Marker at {SourceLock.Bounds.X},{SourceLock.Bounds.Y} in the {SourceWidth}x{SourceHeight} source, {SourceLock.ModuleSizePx:0.0} px per module. "
          + $"Storing {Crop.Roi.Width}x{Crop.Roi.Height} at {Crop.Roi.X},{Crop.Roi.Y} as {Crop.StoredWidth}x{Crop.StoredHeight} "
          + $"({Crop.StoredModulePx:0.0} px per module): {RecordSize / 1024.0:0.#} KiB per captured frame instead of "
          + $"{FullFrameRecordSize / 1024.0:0} KiB ({(double)FullFrameRecordSize / RecordSize:0}x less)."
      );

    /// <summary>The 'capture' arguments that store the same region without locating the marker again.</summary>
    public string Arguments =>
      Scale is { } scale
        ? string.Create(CultureInfo.InvariantCulture, $"--roi {Crop.Roi} --scale {scale.Width}x{scale.Height}")
        : string.Create(CultureInfo.InvariantCulture, $"--roi {Crop.Roi}");
  }
}
