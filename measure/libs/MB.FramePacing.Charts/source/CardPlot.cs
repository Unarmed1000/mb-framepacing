//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A plot area of a report card: where it is on the card (pixels, y down) and the data range it shows, so the GUI can map the pointer to a
//* time or a value (zoom, pan, hover) and tests can read a shape's value back. The x range grows to the right, the y range upwards; a
//* logarithmic count axis holds log10 of the counts.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Charts
{
  /// <param name="Id">What it shows: a report item (<see cref="ReportItem"/>) or a card id (<see cref="DistributionCard"/>).</param>
  /// <param name="Left">The plot's left edge on the card.</param>
  /// <param name="Top">Its top edge.</param>
  /// <param name="Right">Its right edge.</param>
  /// <param name="Bottom">Its bottom edge.</param>
  /// <param name="XFrom">The value at the left edge.</param>
  /// <param name="XTo">The value at the right edge.</param>
  /// <param name="YFrom">The value at the bottom edge.</param>
  /// <param name="YTo">The value at the top edge.</param>
  public sealed record CardPlot(string Id, double Left, double Top, double Right, double Bottom, double XFrom, double XTo, double YFrom, double YTo)
  {
    public double PixelX(double value) => Left + ((Right - Left) * (value - XFrom) / (XTo - XFrom));

    public double PixelY(double value) => Bottom - ((Bottom - Top) * (value - YFrom) / (YTo - YFrom));

    public double ValueX(double pixel) => XFrom + ((XTo - XFrom) * (pixel - Left) / (Right - Left));

    public double ValueY(double pixel) => YFrom + ((YTo - YFrom) * (Bottom - pixel) / (Bottom - Top));

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
  }
}
