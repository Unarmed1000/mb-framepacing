//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker size and position an application should use for a capture setup: its output resolution, the stored capture height and
//* whether the card delivers MJPEG. Uses the marker library's own sizing functions, so the advice matches what the libraries compute.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FramePacing.Marker;

namespace MB.FramePacing.MarkerDecoding
{
  public static class MarkerSizing
  {
    public static MarkerSizingAdvice Advise(int sourceWidth, int sourceHeight, int storedHeight, bool mjpeg = false)
    {
      if (sourceWidth <= 0 || sourceHeight <= 0)
        throw new ArgumentOutOfRangeException(nameof(sourceHeight), "The source size must be positive");
      if (storedHeight <= 0)
        throw new ArgumentOutOfRangeException(nameof(storedHeight), "The stored height must be positive");

      int module = FM.FrameMarker.RecommendModuleSizePx(sourceHeight, storedHeight, mjpeg);
      var options = new FM.Options(module);
      // Integer downscales keep module edges on stored pixel edges when the origin is a multiple of the ratio
      int align = sourceHeight % storedHeight == 0 ? sourceHeight / storedHeight : 1;
      var origin = FM.FrameMarker.RecommendedOrigin(FM.MarkerKind.Frame, sourceWidth, sourceHeight, options, align);
      var syncOrigin = FM.FrameMarker.RecommendedOrigin(FM.MarkerKind.Sync, sourceWidth, sourceHeight, options, align);
      return new MarkerSizingAdvice
      {
        SourceWidth = sourceWidth,
        SourceHeight = sourceHeight,
        StoredHeight = storedHeight,
        Mjpeg = mjpeg,
        RecommendedModulePx = module,
        MinimumModulePx = FM.FrameMarker.MinimumModuleSizePx(sourceHeight, storedHeight),
        StoredPxPerModule = module * (double)storedHeight / sourceHeight,
        MarkerPx = FM.FrameMarker.MarkerSizePx(options),
        SyncMarkerPx = FM.FrameMarker.MarkerSizePx(options, FM.MarkerKind.Sync),
        SyncOriginX = syncOrigin.X,
        SyncOriginY = syncOrigin.Y,
        AlignPx = align,
        OriginX = origin.X,
        OriginY = origin.Y,
      };
    }
  }
}
