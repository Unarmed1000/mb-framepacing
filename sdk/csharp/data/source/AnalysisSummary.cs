//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json (doc/analysis-output-format.md): the capture, the analysis settings and every run's counts, statistics, pacing and histograms.
//* Its formatVersion covers the CSV files it names. Written indented, camelCase, without null values.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Data
{
  /// <param name="FormatVersion">The format of the analysis output (this file and the CSVs it names). A file without it is format 1.</param>
  /// <param name="ToolVersion">The version of the tools that wrote it.</param>
  /// <param name="Experimental">A notice when the analysis is experimental (a camera capture), else null.</param>
  /// <param name="Scanout">"SingleScanout" (a capture card) or "Camera".</param>
  /// <param name="Capture">The capture's capture.json, as it was when analysed.</param>
  /// <param name="FrameSize">The stored frame size, "WIDTHxHEIGHT".</param>
  /// <param name="TimeSource">The clock the capture times come from: "Device" or "Host".</param>
  public sealed record AnalysisSummary(
    int FormatVersion,
    string? ToolVersion,
    string? Experimental,
    string? Scanout,
    DateTime AnalysedUtc,
    string? CaptureDirectory,
    JsonElement? Capture,
    string? FrameSize,
    string? TimeSource,
    double CapturePeriodMs,
    double MeasurementResolutionMs,
    double ErrorThresholdMs,
    IReadOnlyList<SummaryMarker> Markers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SummaryRun> Runs
  )
  {
    /// <summary>The format this library reads and writes.</summary>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions g_options = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The options summary.json is written with: indented, camelCase, no null values, enums as their names.</summary>
    public static JsonSerializerOptions JsonOptions => g_options;

    public static AnalysisSummary Read(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parse summary.json. Throws <see cref="InvalidDataException"/> for a newer format version.</summary>
    public static AnalysisSummary Parse(string json)
    {
      var summary = JsonSerializer.Deserialize<AnalysisSummary>(json, g_options) ?? throw new InvalidDataException("summary.json is empty");
      int version = summary.FormatVersion == 0 ? 1 : summary.FormatVersion;
      if (version > CurrentFormatVersion)
        throw new InvalidDataException(
          $"The analysis output has format version {version}, newer than this reader reads ({CurrentFormatVersion}): update the tools or the library"
        );
      return summary with
      {
        FormatVersion = version,
        Markers = summary.Markers ?? Array.Empty<SummaryMarker>(),
        Warnings = summary.Warnings ?? Array.Empty<string>(),
        Runs = summary.Runs ?? Array.Empty<SummaryRun>(),
      };
    }

    public string ToJson() => JsonSerializer.Serialize(this, g_options);

    /// <summary>Write the file (UTF-8 without a byte order mark).</summary>
    public void Write(string path) => File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
  }
}
