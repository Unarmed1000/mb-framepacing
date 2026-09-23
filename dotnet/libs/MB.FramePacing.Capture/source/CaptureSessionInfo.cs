//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* capture.json - the sidecar written next to frames.mbfc describing how the capture was made and how it went.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Capture
{
  public sealed record CaptureSessionInfo
  {
    public const string FileName = "capture.json";
    public const string FramesFileName = "frames.mbfc";

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
    public string? SequenceName { get; init; }

    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string directory) => File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, g_jsonOptions));

    public static CaptureSessionInfo? TryLoad(string directory)
    {
      var path = Path.Combine(directory, FileName);
      return File.Exists(path) ? JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(path), g_jsonOptions) : null;
    }
  }
}
