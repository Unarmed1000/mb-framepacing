//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* C# twin of the C++ marker generator: renders the marker into a GrayImage. Used by the synthetic capture source and the tests; applications
//* use the marker libraries (marker/cpp/, marker/csharp/); this draws with the C# one. The symbol parameters and sizing rules are defined in doc/marker-format.md.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FrameMarker;

namespace MB.FramePacing.Marker
{
  public static class MarkerRenderer
  {
    /// <summary>Every marker (frame, start and end) is QR version 6 (41x41 modules).</summary>
    public const int QrVersion = FM.Marker.QrVersion;

    public const int QrModuleCount = FM.Marker.QrModuleCount;

    /// <summary>The sync marker (<see cref="MarkerKind.Sync"/>) is QR version 2 (25x25 modules).</summary>
    public const int SyncQrModuleCount = FM.Marker.SyncQrModuleCount;
    public const int RecommendedQuietZoneModules = FM.Marker.RecommendedQuietZoneModules;
    public const int RecommendedInsetPx = FM.Marker.RecommendedInsetPx;

    // The generator keeps scratch buffers and is not thread safe; captures and analyses render on several threads
    [ThreadStatic]
    private static FM.MarkerGenerator? g_generator;

    [ThreadStatic]
    private static FM.ModuleMatrix? g_matrix;

    /// <summary>Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker.</summary>
    public static int QrModuleCountFor(MarkerKind kind) => FM.Marker.QrModuleCountFor((FM.MarkerKind)kind);

    /// <summary>Marker size (symbol + quiet zone): frame, start and end markers have one size, the sync marker is smaller.</summary>
    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules, MarkerKind kind = MarkerKind.Frame) =>
      (QrModuleCountFor(kind) + (2 * quietZoneModules)) * moduleSizePx;

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

      int size = (modules.Size + (2 * quietZoneModules)) * moduleSizePx;
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
