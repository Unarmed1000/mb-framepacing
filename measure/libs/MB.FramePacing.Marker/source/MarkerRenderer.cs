//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* C# twin of the C++ marker generator: renders the marker into a GrayImage. Used by the synthetic capture source and the tests; applications
//* use the marker libraries (marker/cpp/, marker/csharp/); this draws with the C# one. The symbol parameters and sizing rules are defined in doc/marker-format.md.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using FM = MB.FrameMarker;

namespace MB.FramePacing.Marker
{
  public static class MarkerRenderer
  {
    /// <summary>Frame and end markers are fixed to QR version 2 (25x25 modules).</summary>
    public const int FrameQrVersion = FM.Marker.FrameQrVersion;

    /// <summary>Start markers use the smallest version in [FrameQrVersion, MaxQrVersion] that fits the metadata.</summary>
    public const int MaxQrVersion = FM.Marker.MaxQrVersion;

    public const int FrameQrModuleCount = FM.Marker.FrameQrModuleCount;
    public const int MaxQrModuleCount = FM.Marker.MaxQrModuleCount;
    public const int RecommendedQuietZoneModules = FM.Marker.RecommendedQuietZoneModules;
    public const int RecommendedInsetPx = FM.Marker.RecommendedInsetPx;

    // The generator keeps scratch buffers and is not thread safe; captures and analyses render on several threads
    [ThreadStatic]
    private static FM.MarkerGenerator? g_generator;

    [ThreadStatic]
    private static FM.ModuleMatrix? g_matrix;

    /// <summary>Marker size (symbol + quiet zone) for a frame or end marker.</summary>
    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules) =>
      MarkerSizePx(moduleSizePx, quietZoneModules, FrameQrModuleCount);

    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules, int moduleCount) => (moduleCount + (2 * quietZoneModules)) * moduleSizePx;

    /// <summary>Largest possible start marker; the area the analyzer must search around the marker origin.</summary>
    public static int MaxMarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules) =>
      MarkerSizePx(moduleSizePx, quietZoneModules, MaxQrModuleCount);

    /// <summary>Hard minimum module size in source pixels: 2 stored pixels per module after all scaling.</summary>
    public static int MinimumModuleSizePx(int sourceHeight, int storedHeight) => FM.Marker.MinimumModuleSizePx(sourceHeight, storedHeight);

    /// <summary>Recommended module size in source pixels: 3 stored pixels per module (4 for MJPEG capture).</summary>
    public static int RecommendModuleSizePx(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      FM.Marker.RecommendModuleSizePx(sourceHeight, storedHeight, mjpeg);

    /// <summary>Build the QR module matrix for the payload. The metadata is only used by start markers.</summary>
    public static ModuleMatrix GenerateModules(MarkerPayload payload, StartMetadata? metadata = null)
    {
      // The same encoder applications use (MB.FrameMarker), so the synthetic capture shows exactly what a game draws
      var generator = g_generator ??= new FM.MarkerGenerator();
      var matrix = g_matrix ??= new FM.ModuleMatrix();
      if (!generator.GenerateModules(payload.ToFrameMarker(), (metadata ?? StartMetadata.Empty).ToFrameMarker(), matrix))
        throw new ArgumentException($"The start name is longer than {MarkerPayload.MaxStartNameBytes} bytes as UTF-8", nameof(metadata));

      var modules = new ModuleMatrix(matrix.Size);
      for (int y = 0; y < matrix.Size; ++y)
      {
        for (int x = 0; x < matrix.Size; ++x)
          modules.Set(x, y, matrix.IsDark(x, y));
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
  }
}
