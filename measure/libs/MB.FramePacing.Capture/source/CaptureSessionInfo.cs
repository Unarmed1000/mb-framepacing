//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* capture.json - the sidecar written next to captures.mbcd (and frames.mbfc, when the frames were stored) describing how the capture was made
//* and how it went.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MB.FramePacing.Capture.Camera;

namespace MB.FramePacing.Capture
{
  public sealed record CaptureSessionInfo
  {
    public const string FileName = "capture.json";
    public const string FramesFileName = "frames.mbfc";
    public const string DataFileName = "captures.mbcd";

    public string ToolVersion { get; init; } = string.Empty;
    public DateTime StartedUtc { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? FfmpegVersion { get; init; }
    public string? FfmpegCommandLine { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public string? Roi { get; init; }
    public double NominalFps { get; init; }
    public bool WaitedForStart { get; init; }
    public bool StopAtEnd { get; init; }

    public double DurationSeconds { get; init; }
    public long FramesCaptured { get; init; }
    public long FramesWritten { get; init; }
    public long FramesDroppedByRecorder { get; init; }
    public long FramesDroppedBySource { get; init; }
    public long FramesDiscardedBeforeStart { get; init; }
    public string StopReason { get; init; } = string.Empty;
    public uint? SequenceRunId { get; init; }

    /// <summary>The frames themselves were stored (frames.mbfc) next to the capture data.</summary>
    public bool FramesStored { get; init; }

    /// <summary>The start marker's sequence id as text (or in the hex form of a UUID).</summary>
    public string? SequenceId { get; init; }

    /// <summary>The capture's name, given by the user: reports show it instead of the sequence id. Null = none.</summary>
    public string? Name { get; init; }

    /// <summary>The real recording rate of a slow motion clip, when the timestamps were generated from it.</summary>
    public double? RecordedFps { get; init; }

    /// <summary>The frame rate the application aims for (for example 30 on a 60 Hz display); null = the analysis judges it from the frames.</summary>
    public double? TargetFps { get; init; }

    /// <summary>
    /// The display refresh rate the user expects (Hz): the analysis compares it with the refresh rate a camera capture calculates, or
    /// with a capture card's capture rate. Null = no comparison.
    /// </summary>
    public double? ExpectedRefreshHz { get; init; }

    /// <summary>
    /// EXPERIMENTAL: set for a camera capture. The frames are the rig's rectified zones stacked in scanout order, and the analysis treats
    /// differing zones as scanout progress instead of tearing.
    /// </summary>
    public CameraRig? Camera { get; init; }

    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string directory) => File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, g_jsonOptions));

    public static CaptureSessionInfo? TryLoad(string directory)
    {
      var path = Path.Combine(directory, FileName);
      return File.Exists(path) ? JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(path), g_jsonOptions) : null;
    }
  }
}
