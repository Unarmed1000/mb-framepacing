//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds Perfetto's trace processor (trace_processor_shell), the native program that reads a trace. It is never bundled and never
//* downloaded by the tools: the user has it, and we run it as a separate process (see licenses/README.md), as ffmpeg.
//* Order: an explicit path (--trace-processor / GUI), the MB_TRACE_PROCESSOR environment variable, mb-framepacing.json
//* (traceProcessorPath), PATH, then the folder Perfetto's own Python package keeps the copy it downloads in.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. Finds the user's trace_processor_shell.</summary>
  public static class TraceProcessorLocator
  {
    public const string EnvironmentVariable = "MB_TRACE_PROCESSOR";

    /// <summary>Where Perfetto publishes the program: each release has a zip per system that holds trace_processor_shell.</summary>
    public const string DownloadPage = "https://github.com/google/perfetto/releases";

    private static string Executable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "trace_processor_shell.exe" : "trace_processor_shell";

    /// <summary>
    /// Resolve the trace processor: the explicit path, then MB_TRACE_PROCESSOR, then the configuration file, then PATH and the folder
    /// Perfetto's Python package downloads into. A <see cref="FileNotFoundException"/> says where to get it.
    /// </summary>
    public static string Find(string? explicitPath = null, FramePacingConfig? config = null)
    {
      if (!string.IsNullOrWhiteSpace(explicitPath))
        return ValidateFile(explicitPath, "--trace-processor");

      var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
      if (!string.IsNullOrWhiteSpace(fromEnvironment))
        return ValidateFile(fromEnvironment, EnvironmentVariable);

      if (!string.IsNullOrWhiteSpace(config?.TraceProcessorPath))
        return ValidateFile(config.TraceProcessorPath, $"traceProcessorPath in {config.SourcePath ?? FramePacingConfig.FileName}");

      return TryFindInstalled()
        ?? throw new FileNotFoundException(
          $"Perfetto's trace processor ({Executable}) was not found. Download a release for this system from {DownloadPage} (its zip "
            + $"holds {Executable}), then set \"traceProcessorPath\" in {FramePacingConfig.ResolvePath()} (or use {EnvironmentVariable} / "
            + "--trace-processor)."
        );
    }

    /// <summary>Look for the program on PATH, then where Perfetto's Python package keeps the copy it downloaded (the newest there).</summary>
    public static string? TryFindInstalled()
    {
      var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        var candidate = Path.Combine(directory.Trim('"'), Executable);
        if (File.Exists(candidate))
          return candidate;
      }
      return TryFindIn(PackageCacheDirectory);
    }

    /// <summary>The folder Perfetto's Python package downloads its trace processor into, on every system.</summary>
    public static string PackageCacheDirectory =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "perfetto", "prebuilts");

    /// <summary>The newest trace_processor_shell of <paramref name="directory"/> (the package names each copy with a digest); null without one.</summary>
    internal static string? TryFindIn(string directory)
    {
      if (!Directory.Exists(directory))
        return null;
      string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty;
      return Directory
        .EnumerateFiles(directory, "trace_processor_shell*")
        .Where(file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .ThenBy(file => file, StringComparer.Ordinal)
        .FirstOrDefault();
    }

    private static string ValidateFile(string path, string source)
    {
      var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
      if (!File.Exists(fullPath))
        throw new FileNotFoundException($"The trace processor from {source} does not exist: '{fullPath}'");
      return fullPath;
    }
  }
}
