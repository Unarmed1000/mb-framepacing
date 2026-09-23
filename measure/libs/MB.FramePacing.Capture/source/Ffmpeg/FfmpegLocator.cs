//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds the ffmpeg executable. FFmpeg is never bundled: the user installs it (see licenses/README.md) and we run it as a separate process.
//* Order: an explicit path (--ffmpeg / GUI), the MB_FFMPEG environment variable, mb-framepacing.json (ffmpegPath), then PATH.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class FfmpegLocator
  {
    public const string EnvironmentVariable = "MB_FFMPEG";

    /// <summary>The ffmpeg.org download section for this operating system.</summary>
    public static string DownloadPage =>
      RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "https://ffmpeg.org/download.html#build-windows"
      : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "https://ffmpeg.org/download.html#build-mac"
      : "https://ffmpeg.org/download.html#build-linux";

    /// <summary>Resolve ffmpeg: the explicit path, then MB_FFMPEG, then the configuration file, then PATH.</summary>
    public static string Find(string? explicitPath = null, FramePacingConfig? config = null)
    {
      if (!string.IsNullOrWhiteSpace(explicitPath))
        return ValidateFile(explicitPath, "--ffmpeg");

      var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
      if (!string.IsNullOrWhiteSpace(fromEnvironment))
        return ValidateFile(fromEnvironment, EnvironmentVariable);

      if (!string.IsNullOrWhiteSpace(config?.FfmpegPath))
        return ValidateFile(config.FfmpegPath, $"ffmpegPath in {config.SourcePath ?? FramePacingConfig.FileName}");

      return TryFindInstalled()
        ?? throw new FileNotFoundException(
          $"ffmpeg was not found. {InstallHint()} Then set \"ffmpegPath\" in {FramePacingConfig.ResolvePath()} (or use {EnvironmentVariable} / --ffmpeg)."
        );
    }

    /// <summary>
    /// Look for an installed ffmpeg: PATH first, then the places the usual installers put it (winget, Chocolatey, Scoop, Homebrew, apt, snap).
    /// The install locations matter right after an install, when this process still sees the old PATH.
    /// </summary>
    public static string? TryFindInstalled()
    {
      var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
      var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        var candidate = Path.Combine(directory.Trim('"'), executable);
        if (File.Exists(candidate))
          return candidate;
      }
      foreach (var candidate in KnownInstallLocations())
      {
        if (File.Exists(candidate))
          return candidate;
      }
      return null;
    }

    private static System.Collections.Generic.IEnumerable<string> KnownInstallLocations()
    {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        var wingetPackages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
          foreach (var package in Directory.EnumerateDirectories(wingetPackages, "*FFmpeg*"))
          {
            var options = new EnumerationOptions
            {
              RecurseSubdirectories = true,
              IgnoreInaccessible = true,
              MaxRecursionDepth = 4,
            };
            foreach (var candidate in Directory.EnumerateFiles(package, "ffmpeg.exe", options))
              yield return candidate;
          }
        }
        yield return Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "chocolatey", "bin", "ffmpeg.exe");
        yield return Path.Combine(home, "scoop", "shims", "ffmpeg.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe");
        yield return @"C:\ffmpeg\bin\ffmpeg.exe";
      }
      else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        yield return "/opt/homebrew/bin/ffmpeg";
        yield return "/usr/local/bin/ffmpeg";
        yield return "/opt/local/bin/ffmpeg";
      }
      else
      {
        yield return "/usr/bin/ffmpeg";
        yield return "/usr/local/bin/ffmpeg";
        yield return "/snap/bin/ffmpeg";
        yield return Path.Combine(home, ".local", "bin", "ffmpeg");
      }
    }

    /// <summary>A one line install command for this operating system (shown in the GUI setup dialog).</summary>
    public static string InstallCommand =>
      RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "winget install Gyan.FFmpeg"
      : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "brew install ffmpeg"
      : "sudo apt install ffmpeg";

    public static string InstallHint()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return $"Install it with 'winget install Gyan.FFmpeg' or download a build from {DownloadPage}.";
      if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return $"Install it with 'brew install ffmpeg' (see {DownloadPage}).";
      return $"Install it with your package manager, e.g. 'sudo apt install ffmpeg' (see {DownloadPage}).";
    }

    private static string ValidateFile(string path, string source)
    {
      var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
      if (!File.Exists(fullPath))
        throw new FileNotFoundException($"ffmpeg from {source} does not exist: '{fullPath}'");
      return fullPath;
    }
  }
}
