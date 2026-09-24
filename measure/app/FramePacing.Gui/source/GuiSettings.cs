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

namespace MB.FramePacing.Gui
{
  public sealed class GuiSettings
  {
    private static readonly string g_path = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "mb-framepacing",
      "gui-settings.json"
    );

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

    /// <summary>The Analyze page's time source (a TimeSource name).</summary>
    public string? TimeSource { get; set; }

    /// <summary>The page that was open when the window closed.</summary>
    public int SelectedTab { get; set; }

    public static GuiSettings Load()
    {
      try
      {
        if (File.Exists(g_path))
          return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(g_path)) ?? new GuiSettings();
      }
      catch (IOException) { }
      catch (JsonException) { }
      return new GuiSettings();
    }

    public void Save()
    {
      // A demo or an explicit --output-root run must not change the remembered settings
      if (Program.Demo || Program.OutputRoot != null)
        return;
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(g_path)!);
        File.WriteAllText(g_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
