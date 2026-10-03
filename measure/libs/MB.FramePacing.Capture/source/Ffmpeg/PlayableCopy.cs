//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Makes a copy of a recording that web browsers play (FfmpegCommandBuilder.BuildPlayableCopy: a remux, or an H.264 transcode), with every
//* frame and its timestamp kept. ffmpeg writes a temporary file next to the target, which replaces the target only once it is whole; a
//* cancelled or failed copy leaves nothing behind.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static class PlayableCopy
  {
    /// <summary>
    /// Copy <paramref name="source"/> (described by <paramref name="info"/>) into the MP4 file <paramref name="target"/>; reports the share
    /// done (0 to 1) when the duration is known. Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static void Make(
      string ffmpegPath,
      string source,
      string target,
      VideoCodecInfo info,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default
    )
    {
      string partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, Path.GetFileNameWithoutExtension(target) + ".partial.mp4");
      var startInfo = new ProcessStartInfo(ffmpegPath)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      foreach (var argument in FfmpegCommandBuilder.BuildPlayableCopy(Path.GetFullPath(source), partial, info))
        startInfo.ArgumentList.Add(argument);
      try
      {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg");
        process.StandardInput.Close();
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
          if (e.Data != null)
            lock (errors)
              errors.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();
        using (cancellationToken.Register(() => Kill(process)))
        {
          string? line;
          while ((line = process.StandardOutput.ReadLine()) != null)
          {
            if (progress != null && info.Duration is { Ticks: > 0 } duration && ProgressTime(line) is { } done)
              progress.Report(Math.Clamp((double)done.Ticks / duration.Ticks, 0, 1));
          }
          process.WaitForExit();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || !File.Exists(partial))
        {
          string text;
          lock (errors)
            text = errors.ToString().Trim();
          throw new InvalidOperationException(
            $"ffmpeg could not make a playable copy of {Path.GetFileName(source)} (exit code {process.ExitCode})"
              + (text.Length > 0 ? ": " + string.Join(" ", text.Split('\n').TakeLast(3).Select(l => l.Trim())) : string.Empty)
              + (
                text.Contains("libx264", StringComparison.Ordinal)
                  ? " - this ffmpeg has no H.264 encoder (libx264): install a full build"
                  : string.Empty
              )
          );
        }
        File.Move(partial, target, overwrite: true);
        progress?.Report(1);
      }
      finally
      {
        if (File.Exists(partial))
          File.Delete(partial);
      }
    }

    /// <summary>The time a "-progress" line says is done ("out_time_us=1234567"), or null for another line.</summary>
    internal static TimeSpan? ProgressTime(string line)
    {
      const string Key = "out_time_us=";
      if (!line.StartsWith(Key, StringComparison.Ordinal))
        return null;
      return long.TryParse(line.AsSpan(Key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds) && microseconds >= 0
        ? TimeSpan.FromTicks(microseconds * 10)
        : null;
    }

    private static void Kill(Process process)
    {
      try
      {
        if (!process.HasExited)
          process.Kill(entireProcessTree: true);
      }
      catch (InvalidOperationException) { }
    }
  }
}
