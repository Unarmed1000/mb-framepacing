//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Remembered GUI state (per user, in the local application data folder). The ffmpeg installation and the capture folder live in the
//* shared mb-framepacing.json configuration file instead (see FramePacingConfig), so the command line tool uses them too.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text.Json;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Gui
{
  public sealed class GuiSettings
  {
    private static readonly string g_path = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "mb-framepacing",
      "gui-settings.json"
    );

    /// <summary>The format this version writes and the newest it reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Set when the file was written by a newer version: it is not overwritten.</summary>
    private bool m_keepFile;

    /// <summary>The file's format. Files from before the field existed count as 1.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string? LastDevice { get; set; }
    public string? Mode { get; set; }
    public string? InputFormat { get; set; }
    public string? Scale { get; set; }
    public string? Roi { get; set; }
    public string? Duration { get; set; }
    public bool WaitForStart { get; set; }
    public bool StopAtEnd { get; set; }
    public string? LastCaptureDirectory { get; set; }
    public string? MediaPath { get; set; }
    public string? ImageFps { get; set; }
    public string? TimestampFile { get; set; }

    /// <summary>EXPERIMENTAL camera capture: film the screen with the calibrated rig, its file, a slow motion clip's recorded fps.</summary>
    public bool UseCamera { get; set; }
    public string? CameraRig { get; set; }
    public string? CameraRecordedFps { get; set; }

    /// <summary>The Analyze page's time source (a TimeSource name).</summary>
    public string? TimeSource { get; set; }

    /// <summary>The page that was open when the window closed.</summary>
    public int SelectedTab { get; set; }

    public static GuiSettings Load()
    {
      // A demo or an explicit --output-root run (also DocImages) starts from the defaults: its result must not depend on, or show, what
      // the user did last
      if (Program.Demo || Program.OutputRoot != null)
        return new GuiSettings();
      if (!File.Exists(g_path))
        return new GuiSettings();
      // The file, then the version the last save replaced: a damaged file falls back to it
      foreach (var path in new[] { g_path, SettingsFile.PreviousPath(g_path) })
      {
        try
        {
          if (JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(path)) is not { } settings)
            continue;
          // Written by a newer version: use the defaults and leave its file alone
          if (settings.FormatVersion > CurrentFormatVersion)
            return new GuiSettings { m_keepFile = true };
          return settings;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
      }
      return new GuiSettings();
    }

    public void Save()
    {
      // A demo or an explicit --output-root run must not change the remembered settings
      if (Program.Demo || Program.OutputRoot != null || m_keepFile)
        return;
      FormatVersion = CurrentFormatVersion;
      try
      {
        // The previous version is kept in the backup folder next to it
        SettingsFile.Write(g_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), CurrentFormatVersion);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
