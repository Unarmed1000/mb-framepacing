//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A calibrated camera rig (EXPERIMENTAL camera support): a high speed camera mounted at a fixed position in front of the screen, with the
//* two marker zones it sees. Calibrated once (CameraCalibrator), saved as <name>.camera-rig.json, and verified before every camera capture.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Capture.Camera
{
  /// <summary>
  /// A calibrated camera rig: a high speed camera mounted at a fixed position in front of the screen, with the two marker zones it sees.
  /// Calibrated once (<see cref="CameraCalibrator"/>), saved as <c>&lt;name&gt;.camera-rig.json</c> and verified before every camera capture.
  /// </summary>
  public sealed record CameraRig
  {
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".camera-rig.json";

    /// <summary>Shown wherever camera capture is offered or used.</summary>
    public const string ExperimentalNotice =
      "Camera capture is VERY EXPERIMENTAL: results are not validated against reference hardware yet and may be wrong. "
      + "Prefer a capture card where possible.";

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Written into every rig file so nobody mistakes it for a validated setup.</summary>
    public string Experimental { get; init; } = ExperimentalNotice;

    /// <summary>The name the rig is saved under in the camera library (<see cref="CameraRigLibrary"/>), if any.</summary>
    public string? Name { get; init; }

    public DateTime CreatedUtc { get; init; }

    /// <summary>What was calibrated (device name or clip path), for people.</summary>
    public string Source { get; init; } = string.Empty;

    public int CameraWidth { get; init; }
    public int CameraHeight { get; init; }

    /// <summary>The camera frame rate measured from the calibration timestamps.</summary>
    public double CameraFps { get; init; }

    /// <summary>The live device mode (WxH@fps) and input format the rig was calibrated with, if it was a live device.</summary>
    public string? Mode { get; init; }
    public string? InputFormat { get; init; }

    /// <summary>The zones in scanout order: the zone the scanout reaches first is [0] and times the frames.</summary>
    public IReadOnlyList<CameraZone> Zones { get; init; } = Array.Empty<CameraZone>();

    /// <summary>Time the scanout takes from the first zone to the second one.</summary>
    public double? ScanoutDelayMs { get; init; }

    /// <summary>The refresh rate estimated from the calibration (median time between consecutive frames).</summary>
    public double? RefreshHz { get; init; }

    public IReadOnlyList<CameraCheck> Checks { get; init; } = Array.Empty<CameraCheck>();

    [JsonIgnore]
    public bool HasFailures => Checks.Any(c => c.Level == CameraCheckLevel.Fail);

    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, g_jsonOptions);

    /// <summary>Write the rig file; the version it replaces is kept in the backup folder next to it (<see cref="SettingsFile"/>).</summary>
    public void Save(string path) => SettingsFile.Write(path, ToJson(), FormatVersion);

    public static CameraRig FromJson(string json)
    {
      var rig = JsonSerializer.Deserialize<CameraRig>(json, g_jsonOptions) ?? throw new InvalidDataException("The camera rig file is empty");
      if (rig.FormatVersion > CurrentFormatVersion)
        throw new InvalidDataException(
          $"Camera rig format {rig.FormatVersion} was written by a newer mb-framepacing (this one reads {CurrentFormatVersion}); update the tools"
        );
      if (rig.FormatVersion != CurrentFormatVersion)
        throw new InvalidDataException($"Camera rig format {rig.FormatVersion} is not supported (expected {CurrentFormatVersion}); recalibrate");
      if (rig.Zones.Count == 0 || rig.CameraWidth <= 0 || rig.CameraHeight <= 0)
        throw new InvalidDataException("The camera rig file has no zones; recalibrate");
      return rig;
    }

    /// <summary>Load a rig file. Errors name the file and its newest backup (<see cref="SettingsFile"/>).</summary>
    public static CameraRig Load(string path)
    {
      try
      {
        return FromJson(File.ReadAllText(path));
      }
      catch (Exception ex) when (ex is InvalidDataException or JsonException)
      {
        throw new InvalidDataException($"'{path}': {ex.Message.TrimEnd('.')}.{SettingsFile.BackupHint(path)}", ex);
      }
    }
  }
}
