//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How large the marker is drawn: the size of one QR module in source pixels and the white border around the symbol in modules. Always valid:
//* the module size is within [MinModuleSizePx, MaxModuleSizePx] and the quiet zone within [0, MaxQuietZoneModules]; the constructor clamps a
//* value outside into its range. default(Options) is Options.Default, as C++'s Options{} is.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  public readonly struct Options : IEquatable<Options>
  {
    // Kept relative to the defaults: a struct's zeroed default(Options) is then the default options, not an invalid module size of 0
    private readonly int m_moduleSizeFromDefault;
    private readonly int m_quietZoneFromDefault;

    /// <summary>See doc/marker-format.md "Sizing", or <see cref="Recommended"/> for a capture's scaling. The QR specification asks for a quiet zone of 4.</summary>
    public Options(int moduleSizePx, int quietZoneModules = FrameMarker.RecommendedQuietZoneModules)
    {
      m_moduleSizeFromDefault = Math.Clamp(moduleSizePx, FrameMarker.MinModuleSizePx, FrameMarker.MaxModuleSizePx) - FrameMarker.DefaultModuleSizePx;
      m_quietZoneFromDefault = Math.Clamp(quietZoneModules, 0, FrameMarker.MaxQuietZoneModules) - FrameMarker.RecommendedQuietZoneModules;
    }

    /// <summary><see cref="FrameMarker.DefaultModuleSizePx"/> pixel modules and the recommended quiet zone, the same defaults as the C++ library.</summary>
    public static Options Default => default;

    /// <summary>Size of one QR module in source pixels.</summary>
    public int ModuleSizePx => m_moduleSizeFromDefault + FrameMarker.DefaultModuleSizePx;

    /// <summary>White border around the symbol in modules.</summary>
    public int QuietZoneModules => m_quietZoneFromDefault + FrameMarker.RecommendedQuietZoneModules;

    /// <summary>The quiet zone in source pixels: the offset from the marker's origin to its symbol.</summary>
    public int QuietZonePx => QuietZoneModules * ModuleSizePx;

    /// <summary>
    /// The recommended options for a capture that stores the <paramref name="sourceHeight"/> pixel high output <paramref name="storedHeight"/>
    /// pixels high: 3 stored pixels per module (4 when the capture card delivers MJPEG) after all scaling (source -> capture -> stored), and the
    /// recommended quiet zone. A height of 0 or less means no scaling.
    /// </summary>
    public static Options Recommended(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      new Options(ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight));

    /// <summary>The smallest module size that still decodes: 2 stored pixels per module after all scaling, and the recommended quiet zone.</summary>
    public static Options Minimum(int sourceHeight, int storedHeight) => new Options(ModuleSizeForStoredPx(2, sourceHeight, storedHeight));

    /// <summary>
    /// Width and height in source pixels of a marker (symbol + quiet zone). Frame, start and end markers have one size, the sync marker is
    /// smaller.
    /// </summary>
    public int MarkerSizePx(MarkerKind kind = MarkerKind.Frame) => (FrameMarker.QrModuleCountFor(kind) + (2 * QuietZoneModules)) * ModuleSizePx;

    /// <summary>
    /// Recommended origin of a marker in a <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> output: the main marker (frame, start
    /// and end) top-left, the sync marker bottom-left, <see cref="FrameMarker.RecommendedInsetPx"/> from the edges. <paramref name="alignPx"/>
    /// should be the capture's integer downscale ratio (1 if none) so module edges land on stored pixel edges.
    /// </summary>
    public Point RecommendedOrigin(MarkerKind kind, int sourceWidth, int sourceHeight, int alignPx = 1)
    {
      int inset = AlignUp(FrameMarker.RecommendedInsetPx, alignPx);
      if (kind == MarkerKind.Sync)
        return new Point(inset, AlignDown(sourceHeight - inset - MarkerSizePx(kind), alignPx));
      return new Point(inset, inset);
    }

    public bool Equals(Options other) =>
      m_moduleSizeFromDefault == other.m_moduleSizeFromDefault && m_quietZoneFromDefault == other.m_quietZoneFromDefault;

    public override bool Equals(object obj) => obj is Options other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(m_moduleSizeFromDefault, m_quietZoneFromDefault);

    public static bool operator ==(Options left, Options right) => left.Equals(right);

    public static bool operator !=(Options left, Options right) => !left.Equals(right);

    public override string ToString() => $"{{module {ModuleSizePx} px, quiet zone {QuietZoneModules}}}";

    private static int CeilDiv(long numerator, long denominator) => (int)((numerator + denominator - 1) / denominator);

    private static int AlignDown(int value, int alignment) => alignment <= 1 ? value : (value / alignment) * alignment;

    private static int AlignUp(int value, int alignment) => alignment <= 1 ? value : CeilDiv(value, alignment) * alignment;

    /// <summary>
    /// The module size that gives <paramref name="storedPxPerModule"/> stored pixels per module, ceil(storedPxPerModule x sourceHeight /
    /// storedHeight), within the valid module sizes.
    /// </summary>
    private static int ModuleSizeForStoredPx(int storedPxPerModule, int sourceHeight, int storedHeight)
    {
      if (sourceHeight <= 0 || storedHeight <= 0)
        return storedPxPerModule;
      long size = (((long)storedPxPerModule * sourceHeight) + storedHeight - 1) / storedHeight;
      return (int)Math.Clamp(size, storedPxPerModule, FrameMarker.MaxModuleSizePx);
    }
  }
}
