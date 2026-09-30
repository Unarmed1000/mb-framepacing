//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One marker slot as the calibrated camera sees it (EXPERIMENTAL camera support): the module to camera transform, and the area a camera
//* capture rectifies and stores for it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Camera
{
  /// <summary>One marker slot as the calibrated camera sees it: the module to camera transform, and the area a camera capture stores.</summary>
  /// <param name="ModuleToCamera">Module coordinates (the symbol's top-left corner is 0,0) to camera pixels.</param>
  /// <param name="ModuleSizePx">Smallest module size in camera pixels (the far edge of the marker).</param>
  /// <param name="Black">Camera luma of a dark module.</param>
  /// <param name="White">Camera luma of a light module.</param>
  /// <param name="ScanDelayMs">How much later than the first zone the scanout reaches this zone (0 for the first zone).</param>
  /// <param name="DecodeRate">Share of the calibration frames in which this zone decoded.</param>
  /// <param name="Kind">The marker in this zone: the main marker (<see cref="MarkerKind.Frame"/>) or the sync marker.</param>
  public sealed record CameraZone(
    Homography ModuleToCamera,
    double ModuleSizePx,
    double Black,
    double White,
    double ScanDelayMs,
    double DecodeRate,
    MarkerKind Kind = MarkerKind.Frame
  )
  {
    /// <summary>The zone of the main marker (frame, start and end) and of the sync marker in a rig and in a stored camera capture.</summary>
    public const int MainZone = 0;

    public const int SyncZone = 1;

    /// <summary>Modules per side of this zone's symbol.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ModuleCount => MarkerRenderer.QrModuleCountFor(Kind);

    /// <summary>Modules stored around the marker's symbol: the quiet zone plus 2 modules of margin for the analyzer's search.</summary>
    public const int MarginModules = MarkerRenderer.RecommendedQuietZoneModules + 2;

    /// <summary>Stored pixels per module after rectification.</summary>
    public const int StoredPxPerModule = 4;

    /// <summary>Side of the square area stored per zone, in modules: the main marker plus the margin on both sides (the sync marker fits too).</summary>
    public const int StoredModules = MarkerRenderer.QrModuleCount + (2 * MarginModules);

    /// <summary>Side of the stored (rectified) zone image in pixels.</summary>
    public const int StoredSizePx = StoredModules * StoredPxPerModule;

    /// <summary>Where the marker origin (top-left of the quiet zone) lands in the stored zone image.</summary>
    public const int StoredMarkerOriginPx = (MarginModules - MarkerRenderer.RecommendedQuietZoneModules) * StoredPxPerModule;

    /// <summary>
    /// The marker lock of zone <paramref name="index"/> in a stored camera capture: the zones are stacked, the main marker's first
    /// (<see cref="MainZone"/>), then the sync marker's (<see cref="SyncZone"/>).
    /// </summary>
    public static MarkerLock StoredLock(int index) =>
      MarkerLock.At(
        StoredMarkerOriginPx,
        (index * StoredSizePx) + StoredMarkerOriginPx,
        StoredPxPerModule,
        index == MainZone ? MarkerKind.Frame : MarkerKind.Sync
      );

    /// <summary>The camera positions of the stored square's corners: top-left, top-right, bottom-left, bottom-right.</summary>
    public ImagePoint[] StoredQuad()
    {
      double near = -MarginModules;
      double far = MarkerRenderer.QrModuleCount + MarginModules;
      return new[]
      {
        ModuleToCamera.Map(new ImagePoint(near, near)),
        ModuleToCamera.Map(new ImagePoint(far, near)),
        ModuleToCamera.Map(new ImagePoint(near, far)),
        ModuleToCamera.Map(new ImagePoint(far, far)),
      };
    }

    /// <summary>The camera region (clipped to the frame) that holds the stored square.</summary>
    public PixelRect StoredBounds(int cameraWidth, int cameraHeight)
    {
      var quad = StoredQuad();
      double minX = double.MaxValue,
        minY = double.MaxValue,
        maxX = double.MinValue,
        maxY = double.MinValue;
      foreach (var point in quad)
      {
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
      }
      int left = (int)Math.Floor(minX) - 1;
      int top = (int)Math.Floor(minY) - 1;
      int right = (int)Math.Ceiling(maxX) + 1;
      int bottom = (int)Math.Ceiling(maxY) + 1;
      return new PixelRect(left, top, right - left, bottom - top).Intersect(new PixelRect(0, 0, cameraWidth, cameraHeight));
    }

    /// <summary>The camera region of the zone's marker itself (quiet zone included), for a quick decode.</summary>
    public PixelRect FrameMarkerBounds(int cameraWidth, int cameraHeight, int marginModules = 2)
    {
      double near = -MarkerRenderer.RecommendedQuietZoneModules - marginModules;
      double far = ModuleCount + MarkerRenderer.RecommendedQuietZoneModules + marginModules;
      double minX = double.MaxValue,
        minY = double.MaxValue,
        maxX = double.MinValue,
        maxY = double.MinValue;
      foreach (var module in new ImagePoint[] { new(near, near), new(far, near), new(near, far), new(far, far) })
      {
        var point = ModuleToCamera.Map(module);
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
      }
      int left = (int)Math.Floor(minX);
      int top = (int)Math.Floor(minY);
      return new PixelRect(left, top, (int)Math.Ceiling(maxX) - left, (int)Math.Ceiling(maxY) - top).Intersect(
        new PixelRect(0, 0, cameraWidth, cameraHeight)
      );
    }

    /// <summary>The centre of the zone's symbol in camera pixels.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ImagePoint Centre => ModuleToCamera.Map(new ImagePoint(ModuleCount / 2.0, ModuleCount / 2.0));
  }
}
