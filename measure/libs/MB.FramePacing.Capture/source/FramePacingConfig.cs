//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* mb-framepacing.json - the user's configuration, shared by the command line tool and the GUI. It points the tools at the user's ffmpeg
//* installation and sets the default capture folder.
//*
//* Lookup order: an explicit path (--config), a file next to the executable (portable installs), the per user configuration folder:
//*   Windows  %APPDATA%\mb-framepacing\mb-framepacing.json
//*   macOS    ~/Library/Application Support/mb-framepacing/mb-framepacing.json
//*   Linux    $XDG_CONFIG_HOME/mb-framepacing/mb-framepacing.json (default ~/.config/...)
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Capture
{
  public sealed record FramePacingConfig
  {
    public const string FileName = "mb-framepacing.json";

    /// <summary>Full path of the ffmpeg executable. Null = use MB_FFMPEG or PATH.</summary>
    public string? FfmpegPath { get; init; }

    /// <summary>Folder that receives new captures (each capture gets its own sub folder). Null = Documents/mb-framepacing.</summary>
    public string? CaptureDirectory { get; init; }

    /// <summary>The file this configuration was loaded from, null if none exists yet.</summary>
    [JsonIgnore]
    public string? SourcePath { get; init; }

    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The per user configuration file.</summary>
    public static string UserConfigPath
    {
      get
      {
        string root;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
          root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
          root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else
        {
          var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
          root = !string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(root, "mb-framepacing", FileName);
      }
    }

    /// <summary>A configuration file next to the executable wins over the per user one (portable installs).</summary>
    public static string PortableConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Where a configuration is read from and written to: the explicit path, the portable file if it exists, else the user file.</summary>
    public static string ResolvePath(string? explicitPath = null)
    {
      if (!string.IsNullOrWhiteSpace(explicitPath))
        return Path.GetFullPath(explicitPath);
      return File.Exists(PortableConfigPath) ? PortableConfigPath : UserConfigPath;
    }

    /// <summary>Load the configuration. A missing file gives an empty configuration; an explicit path must exist.</summary>
    public static FramePacingConfig Load(string? explicitPath = null)
    {
      var path = ResolvePath(explicitPath);
      if (!File.Exists(path))
      {
        if (!string.IsNullOrWhiteSpace(explicitPath))
          throw new FileNotFoundException($"The configuration file '{path}' does not exist", path);
        return new FramePacingConfig();
      }
      try
      {
        var config = JsonSerializer.Deserialize<FramePacingConfig>(File.ReadAllText(path), g_jsonOptions) ?? new FramePacingConfig();
        return config with { SourcePath = path };
      }
      catch (JsonException ex)
      {
        throw new InvalidDataException($"The configuration file '{path}' is not valid JSON: {ex.Message}", ex);
      }
    }

    /// <summary>Write the configuration (creating the folder) and return the path written.</summary>
    public string Save(string? explicitPath = null)
    {
      var path = explicitPath != null ? Path.GetFullPath(explicitPath) : SourcePath ?? ResolvePath();
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      File.WriteAllText(path, JsonSerializer.Serialize(this, g_jsonOptions));
      return path;
    }

    /// <summary>Create the configuration file with a commented template if it does not exist yet. Returns its path.</summary>
    public static string EnsureExists(string? explicitPath = null)
    {
      var path = ResolvePath(explicitPath);
      if (!File.Exists(path))
      {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Template);
      }
      return path;
    }

    public const string Template = """
      {
        // mb-framepacing configuration (JSON, comments allowed). Used by both mb-framepacing and mb-framepacing-gui.

        // Full path of your ffmpeg executable (FFmpeg 5.1 or newer). Remove the line to use MB_FFMPEG or PATH instead.
        // Windows: "C:\\ffmpeg\\bin\\ffmpeg.exe"   macOS (Homebrew): "/opt/homebrew/bin/ffmpeg"   Linux: "/usr/bin/ffmpeg"
        // "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe",

        // Folder that receives new captures (each capture gets its own sub folder).
        // "captureDirectory": "D:\\captures"
      }

      """;
  }
}
