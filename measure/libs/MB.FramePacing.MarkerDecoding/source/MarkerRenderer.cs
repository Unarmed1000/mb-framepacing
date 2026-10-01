//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* C# twin of the C++ marker generator: renders the marker into a GrayImage. Used by the synthetic capture source and the tests; applications
//* use the SDK's marker modules (sdk/cpp/marker/, sdk/csharp/marker/); this draws with the C# one. The symbol parameters and sizing rules are defined in sdk/doc/marker-format.md.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using FM = MB.FramePacing.Marker;

namespace MB.FramePacing.MarkerDecoding
{
  public static class MarkerRenderer
  {
    /// <summary>Every main marker (frame, start and end) is QR version 6: 41x41 modules.</summary>
    public const int QrModuleCount = FM.ModuleMatrix.MainSize;

    /// <summary>The sync marker (<see cref="MarkerKind.Sync"/>) is QR version 2 (25x25 modules).</summary>
    public const int SyncQrModuleCount = FM.ModuleMatrix.SyncSize;
    public const int RecommendedQuietZoneModules = FM.Options.RecommendedQuietZoneModules;
    public const int RecommendedInsetPx = FM.Options.RecommendedInsetPx;

    // The generator keeps scratch buffers and is not thread safe; captures and analyses render on several threads
    [ThreadStatic]
    private static FM.MarkerGenerator? g_generator;

    /// <summary>Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker.</summary>
    public static int QrModuleCountFor(MarkerKind kind) => FM.ModuleMatrix.SizeFor((FM.MarkerKind)kind);

    /// <summary>Marker size (symbol + quiet zone): frame, start and end markers have one size, the sync marker is smaller.</summary>
    public static int MarkerSizePx(int moduleSizePx, int quietZoneModules = RecommendedQuietZoneModules, MarkerKind kind = MarkerKind.Frame) =>
      (QrModuleCountFor(kind) + (2 * quietZoneModules)) * moduleSizePx;

    /// <summary>Hard minimum module size in source pixels: 2 stored pixels per module after all scaling.</summary>
    public static int MinimumModuleSizePx(int sourceHeight, int storedHeight) => FM.Options.Minimum(sourceHeight, storedHeight).ModuleSizePx;

    /// <summary>Recommended module size in source pixels: 3 stored pixels per module (4 for MJPEG capture).</summary>
    public static int RecommendModuleSizePx(int sourceHeight, int storedHeight, bool mjpeg = false) =>
      FM.Options.Recommended(sourceHeight, storedHeight, mjpeg).ModuleSizePx;

    /// <summary>Build the QR module matrix for the payload. The metadata is only used by start markers.</summary>
    public static ModuleMatrix GenerateModules(MarkerPayload payload, StartMetadata? metadata = null)
    {
      // The same encoder applications use (MB.FramePacing.Marker), so the synthetic capture shows exactly what a game draws
      var generator = g_generator ??= new FM.MarkerGenerator();
      Span<byte> bits = stackalloc byte[FM.ModuleMatrix.MaxPackedModuleByteCount];
      if (!generator.TryGenerateModules(payload.ToFrameMarker(), (metadata ?? StartMetadata.Empty).ToFrameMarker(), bits, out var matrix))
        throw new InvalidOperationException("The marker payload does not fit its QR code");

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
