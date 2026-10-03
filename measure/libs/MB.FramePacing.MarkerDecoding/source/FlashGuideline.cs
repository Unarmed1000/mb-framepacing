//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker against the guideline for flashing content (WCAG 2.3.1 "Three Flashes or Below Threshold"): a marker changes every frame,
//* far more than three times a second, so it is within the guideline only while it is small. WCAG allows a flashing area of a quarter of
//* a 10 degree field of view, and exempts a fine, balanced pattern whose squares are smaller than 0.1 degree. Its estimate for a screen at
//* a usual distance is a 10 degree field of 341 x 256 of 1024 x 768 pixels: a third of the screen's width and of its height, which is
//* what this uses for any resolution. A screen that fills more of the view (a close or very large display, a headset) makes the marker
//* larger than this says. The guideline lowers the risk for people with photosensitive epilepsy; it does not remove it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using FM = MB.FramePacing.Marker;

namespace MB.FramePacing.MarkerDecoding
{
  public static class FlashGuideline
  {
    /// <summary>The field of view the guideline looks at, in degrees.</summary>
    public const double FieldDegrees = 10;

    /// <summary>The share of the screen's width (and of its height) that field takes at a usual distance: WCAG's 341 of 1024 pixels.</summary>
    public const double FieldOfScreen = 1.0 / 3;

    /// <summary>The share of the screen that may flash: a quarter of the field (WCAG's 21,824 of 1024 x 768 pixels, 2.8 %).</summary>
    public const double AreaLimit = 0.25 * FieldOfScreen * FieldOfScreen;

    /// <summary>A fine, balanced pattern is exempt while its squares are smaller than this, in degrees.</summary>
    public const double FinePatternDegrees = 0.1;

    /// <summary>
    /// The share of a <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> screen the main marker's symbol covers with
    /// <paramref name="modulePx"/> pixel modules: all of it counts as changing (its fixed patterns too), and the sync marker, far from
    /// it at the screen's other edge, is in another field of view.
    /// </summary>
    public static double AreaShare(int modulePx, int sourceWidth, int sourceHeight)
    {
      double symbolPx = FM.ModuleMatrix.MainSize * (double)modulePx;
      return symbolPx * symbolPx / ((double)sourceWidth * sourceHeight);
    }

    /// <summary>The size of one module of <paramref name="modulePx"/> pixels on a screen <paramref name="sourceWidth"/> wide, in degrees of view.</summary>
    public static double ModuleDegrees(int modulePx, int sourceWidth) => modulePx * FieldDegrees / (sourceWidth * FieldOfScreen);
  }
}
