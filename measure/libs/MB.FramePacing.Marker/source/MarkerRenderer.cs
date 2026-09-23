//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* C# twin of the C++ marker generator: renders the marker into a GrayImage. Used by the synthetic capture source and the tests; applications
//* use the C++20 library (marker/cpp/). The symbol parameters and sizing rules are defined in doc/marker-format.md.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Text;
using ZXing;
using ZXing.QrCode.Internal;
using QrEncoder = ZXing.QrCode.Internal.Encoder;

namespace MB.FramePacing.Marker
{
  /// <summary>A QR module matrix. <see cref="IsDark"/> is true for dark modules.</summary>
  public sealed class ModuleMatrix
  {
    private readonly bool[] m_modules;

    public ModuleMatrix(int size)
    {
      Size = size;
      m_modules = new bool[size * size];
    }

    public int Size { get; }

    public bool IsDark(int x, int y) => m_modules[(y * Size) + x];

    internal void Set(int x, int y, bool dark) => m_modules[(y * Size) + x] = dark;
  }

  public static class MarkerRenderer
  {
    /// <summary>Frame and end markers are fixed to QR version 2 (25x25 modules).</summary>
    public const int FrameQrVersion = 2;

    /// <summary>Start markers use the smallest version in [FrameQrVersion, MaxQrVersion] that fits the metadata.</summary>
    public const int MaxQrVersion = 6;

    public const int FrameQrModuleCount = (4 * FrameQrVersion) + 17;
    public const int MaxQrModuleCount = (4 * MaxQrVersion) + 17;
    public const int RecommendedQuietZoneModules = 4;
    public const int RecommendedInsetPx = 32;

    // ISO-8859-1 maps every byte 0-255 to the char with the same value, so the payload bytes survive the string based ZXing API unchanged.
    private static readonly Encoding g_latin1 = Encoding.Latin1;

    /// <summary>Marker size (symbol + quiet zone) for a frame or end marker.</summary>
    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules) =>
      MarkerSizePx(moduleSizePx, quietZoneModules, FrameQrModuleCount);

    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules, int moduleCount) => (moduleCount + (2 * quietZoneModules)) * moduleSizePx;

    /// <summary>Largest possible start marker; the area the analyzer must search around the marker origin.</summary>
    public static int MaxMarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules) =>
      MarkerSizePx(moduleSizePx, quietZoneModules, MaxQrModuleCount);

    /// <summary>Hard minimum module size in source pixels: 2 stored pixels per module after all scaling.</summary>
    public static int MinimumModuleSizePx(int sourceHeight, int storedHeight) => ModuleSizeForStoredPx(2, sourceHeight, storedHeight);

    /// <summary>Recommended module size in source pixels: 3 stored pixels per module (4 for MJPEG capture).</summary>
    public static int RecommendModuleSizePx(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      ModuleSizeForStoredPx(mjpeg ? 4 : 3, sourceHeight, storedHeight);

    /// <summary>Build the QR module matrix for the payload. The metadata is only used by start markers.</summary>
    public static ModuleMatrix GenerateModules(MarkerPayload payload, StartMetadata? metadata = null)
    {
      var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "ISO-8859-1", [EncodeHintType.DISABLE_ECI] = true };
      // Frame and end markers are pinned to one version so the symbol never changes size. Start markers let ZXing pick the smallest version.
      if (payload.Kind != MarkerKind.SequenceStart)
        hints[EncodeHintType.QR_VERSION] = FrameQrVersion;

      var content = g_latin1.GetString(payload.Encode(metadata));
      var qrCode = QrEncoder.encode(content, ErrorCorrectionLevel.M, hints);
      if (qrCode.Version.VersionNumber > MaxQrVersion)
        throw new InvalidOperationException($"Unexpected QR version {qrCode.Version.VersionNumber}");

      var matrix = qrCode.Matrix;
      var modules = new ModuleMatrix(matrix.Width);
      for (int y = 0; y < matrix.Height; ++y)
      {
        for (int x = 0; x < matrix.Width; ++x)
          modules.Set(x, y, matrix[x, y] == 1);
      }
      return modules;
    }

    /// <summary>Render the marker (quiet zone + symbol) with its top-left corner at (originX, originY).</summary>
    public static void Render(
      GrayImage target,
      MarkerPayload payload,
      int originX,
      int originY,
      int moduleSizePx,
      int quietZoneModules = RecommendedQuietZoneModules,
      StartMetadata? metadata = null
    )
    {
      Render(target, GenerateModules(payload, metadata), originX, originY, moduleSizePx, quietZoneModules);
    }

    /// <summary>Render a pre-generated module matrix (see <see cref="GenerateModules"/>).</summary>
    public static void Render(
      GrayImage target,
      ModuleMatrix modules,
      int originX,
      int originY,
      int moduleSizePx,
      int quietZoneModules = RecommendedQuietZoneModules
    )
    {
      if (moduleSizePx < 1)
        throw new ArgumentOutOfRangeException(nameof(moduleSizePx));

      int size = MarkerSizePx(moduleSizePx, quietZoneModules, modules.Size);
      target.FillRect(new PixelRect(originX, originY, size, size), 255);

      int symbolLeft = originX + (quietZoneModules * moduleSizePx);
      int symbolTop = originY + (quietZoneModules * moduleSizePx);
      for (int y = 0; y < modules.Size; ++y)
      {
        for (int x = 0; x < modules.Size; ++x)
        {
          if (modules.IsDark(x, y))
            target.FillRect(new PixelRect(symbolLeft + (x * moduleSizePx), symbolTop + (y * moduleSizePx), moduleSizePx, moduleSizePx), 0);
        }
      }
    }

    private static int ModuleSizeForStoredPx(int storedPxPerModule, int sourceHeight, int storedHeight)
    {
      if (sourceHeight <= 0 || storedHeight <= 0)
        return storedPxPerModule;
      int size = (int)((((long)storedPxPerModule * sourceHeight) + storedHeight - 1) / storedHeight);
      return Math.Max(size, storedPxPerModule);
    }
  }
}
