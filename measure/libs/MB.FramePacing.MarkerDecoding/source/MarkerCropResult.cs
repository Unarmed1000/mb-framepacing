//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The region of the source a fast capture stores (see MarkerCrop) and the integer area downscale applied to it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.MarkerDecoding
{
  /// <summary>The region of the source a fast capture stores and the integer area downscale applied to it.</summary>
  /// <param name="Roi">Crop in source pixels; its size and its distance to the marker origin are multiples of <paramref name="Factor"/>.</param>
  /// <param name="Factor">Integer downscale of the crop (1 = stored at source resolution).</param>
  /// <param name="StoredModulePx">Marker module size in stored pixels.</param>
  /// <param name="SyncRoi">The sync marker's crop, stored below <paramref name="Roi"/> (the narrower of the two padded to the wider one's
  /// width); empty when the source has no sync marker, or when one crop holds both markers.</param>
  public readonly record struct MarkerCropResult(PixelRect Roi, int Factor, double StoredModulePx, PixelRect SyncRoi = default)
  {
    /// <summary>The source has a second crop, stacked below the first.</summary>
    public bool HasSyncRoi => !SyncRoi.IsEmpty;

    public int StoredWidth => Math.Max(Roi.Width, HasSyncRoi ? SyncRoi.Width : 0) / Factor;
    public int StoredHeight => (Roi.Height + (HasSyncRoi ? SyncRoi.Height : 0)) / Factor;
  }
}
