//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where the marker was found in the source and the region a fast capture stores (FfmpegMarkerLocator).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Globalization;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>Where the marker was found in the source and the region a fast capture stores.</summary>
  /// <param name="SourceLock">The frame marker in source pixels.</param>
  public sealed record MarkerLocateResult(MarkerLock SourceLock, MarkerCropResult Crop, int SourceWidth, int SourceHeight)
  {
    /// <summary>The sync marker in source pixels, when the source shows one: its region is stored below the main marker's.</summary>
    public MarkerLock? SyncLock { get; init; }

    /// <summary>The stored size of the one region, or null when it is stored at source resolution or there are two regions.</summary>
    public (int Width, int Height)? Scale => Crop.Factor > 1 && !Crop.HasSyncRoi ? (Crop.StoredWidth, Crop.StoredHeight) : null;

    /// <summary>Bytes per record in frames.mbfc with the crop.</summary>
    public int RecordSize => new CaptureFileHeader(Crop.StoredWidth, Crop.StoredHeight, default).RecordSize;

    /// <summary>Bytes per record in frames.mbfc for whole source frames.</summary>
    public int FullFrameRecordSize => new CaptureFileHeader(SourceWidth, SourceHeight, default).RecordSize;

    /// <summary>The capture options with the crop (and its downscale) applied: one region, or the two markers' stacked.</summary>
    public FfmpegCaptureOptions Apply(FfmpegCaptureOptions options) =>
      Crop.HasSyncRoi
        ? options with
        {
          Roi = Crop.Roi,
          SyncRoi = Crop.SyncRoi,
          RoiDownscale = Crop.Factor,
          Scale = null,
        }
        : options with
        {
          Roi = Crop.Roi,
          SyncRoi = null,
          RoiDownscale = 1,
          Scale = Scale,
        };

    /// <summary>Where the marker is and what a fast capture stores, for people.</summary>
    public string Summary
    {
      get
      {
        string sync = Crop.HasSyncRoi
          ? string.Create(
            CultureInfo.InvariantCulture,
            $"and the sync marker's {Crop.SyncRoi.Width}x{Crop.SyncRoi.Height} at {Crop.SyncRoi.X},{Crop.SyncRoi.Y} below it "
          )
          : string.Empty;
        return string.Create(
          CultureInfo.InvariantCulture,
          $"Marker at {SourceLock.Bounds.X},{SourceLock.Bounds.Y} in the {SourceWidth}x{SourceHeight} source, {SourceLock.ModuleSizePx:0.0} px per module. Storing {Crop.Roi.Width}x{Crop.Roi.Height} at {Crop.Roi.X},{Crop.Roi.Y} {sync}as {Crop.StoredWidth}x{Crop.StoredHeight} ({Crop.StoredModulePx:0.0} px per module): {RecordSize / 1024.0:0.#} KiB per captured frame instead of {FullFrameRecordSize / 1024.0:0} KiB ({(double)FullFrameRecordSize / RecordSize:0}x less)."
        );
      }
    }

    /// <summary>
    /// The 'capture' arguments that store the main marker's region without locating the marker again. A region given this way is one
    /// rectangle: a sync marker's is not stored with it.
    /// </summary>
    public string Arguments =>
      Crop.Factor > 1
        ? string.Create(CultureInfo.InvariantCulture, $"--roi {Crop.Roi} --scale {Crop.Roi.Width / Crop.Factor}x{Crop.Roi.Height / Crop.Factor}")
        : string.Create(CultureInfo.InvariantCulture, $"--roi {Crop.Roi}");
  }
}
