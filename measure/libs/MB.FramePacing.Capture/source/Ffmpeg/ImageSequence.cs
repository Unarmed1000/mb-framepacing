//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A folder of images as a capture source. The images are listed in an ffconcat file with one duration per image, so ffmpeg delivers them
//* with the right timestamps: evenly spaced (a frame rate) or taken from a timestamp file.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public static partial class ImageSequence
  {
    /// <summary>Image types ffmpeg reads out of the box.</summary>
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pgm", ".ppm", ".webp", ".tga"];

    /// <summary>List the images of a folder: from the timestamp file when given, otherwise every image in natural name order at <paramref name="fps"/>.</summary>
    public static List<ImageSequenceFrame> Collect(string folder, double? fps, string? timestampFile)
    {
      if (!Directory.Exists(folder))
        throw new DirectoryNotFoundException($"The image folder '{folder}' does not exist");

      if (timestampFile != null)
        return ReadTimestamps(folder, timestampFile);

      if (fps is not > 0)
        throw new ArgumentException("An image sequence needs a frame rate (--fps) or a timestamp file (--timestamps)");

      var files = Directory
        .EnumerateFiles(folder)
        .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .OrderBy(f => Path.GetFileName(f), NaturalComparer.Instance)
        .ToList();
      if (files.Count == 0)
        throw new FileNotFoundException($"No images ({string.Join(", ", Extensions)}) found in '{folder}'");

      double interval = TimeSpan.TicksPerSecond / fps.Value;
      return files.Select((file, index) => new ImageSequenceFrame(file, new TickCount64((long)Math.Round(index * interval)))).ToList();
    }

    /// <summary>Write the ffconcat list ffmpeg plays. Returns the nominal frame rate (median interval).</summary>
    public static double WriteConcatList(IReadOnlyList<ImageSequenceFrame> frames, string listPath)
    {
      if (frames.Count == 0)
        throw new ArgumentException("No images", nameof(frames));

      var intervals = new List<long>();
      var builder = new StringBuilder("ffconcat version 1.0\n");
      for (int i = 0; i < frames.Count; ++i)
      {
        builder.Append("file '").Append(Escape(Path.GetFullPath(frames[i].Path))).Append("'\n");
        long duration;
        if (i + 1 < frames.Count)
        {
          duration = (frames[i + 1].Time - frames[i].Time).Ticks;
          if (duration <= 0)
            throw new InvalidDataException(
              $"The image times must increase ('{Path.GetFileName(frames[i + 1].Path)}' is not later than the image before it)"
            );
          intervals.Add(duration);
        }
        else
          duration = intervals.Count > 0 ? intervals[^1] : TimeSpan.TicksPerSecond / 60;
        builder
          .Append("duration ")
          .Append((duration / (double)TimeSpan.TicksPerSecond).ToString("0.#########", CultureInfo.InvariantCulture))
          .Append('\n');
      }
      // ffmpeg ignores the duration of the last entry unless the file is repeated
      builder.Append("file '").Append(Escape(Path.GetFullPath(frames[^1].Path))).Append("'\n");
      File.WriteAllText(listPath, builder.ToString(), new UTF8Encoding(false));

      if (intervals.Count == 0)
        return 0;
      intervals.Sort();
      return TimeSpan.TicksPerSecond / (double)intervals[intervals.Count / 2];
    }

    /// <summary>
    /// The timestamp file: a header line that names the columns (fileName and timeTicks, found by name; others are ignored), then a line
    /// per image in the order the frames were taken. A time is a whole number of 100 ns ticks, as every file's times are, so the unit is
    /// in the file and nothing goes through a floating point number. Comments (#) and empty lines are skipped.
    /// </summary>
    private static List<ImageSequenceFrame> ReadTimestamps(string folder, string timestampFile)
    {
      const string FileNameColumn = "fileName";
      const string TimeColumn = "timeTicks";
      var frames = new List<ImageSequenceFrame>();
      int fileNameIndex = -1;
      int timeIndex = -1;
      int lineNumber = 0;
      foreach (var raw in File.ReadLines(timestampFile))
      {
        ++lineNumber;
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
          continue;
        // Spaces around a cell are taken: the file is written by hand
        var cells = line.Split(',', StringSplitOptions.TrimEntries);
        if (timeIndex < 0)
        {
          fileNameIndex = Array.IndexOf(cells, FileNameColumn);
          timeIndex = Array.IndexOf(cells, TimeColumn);
          if (fileNameIndex < 0 || timeIndex < 0)
            throw new InvalidDataException(
              $"{timestampFile}:{lineNumber}: expected a header line with the columns {FileNameColumn} and {TimeColumn} (100 ns ticks). "
                + "A file with timeMs is the old format: multiply its times by 10 000."
            );
          continue;
        }
        string name = fileNameIndex < cells.Length ? cells[fileNameIndex] : string.Empty;
        string time = timeIndex < cells.Length ? cells[timeIndex] : string.Empty;
        if (name.Length == 0)
          throw new InvalidDataException($"{timestampFile}:{lineNumber}: the line has no file name");
        // Digits, with a '-' in front when negative, as the analysis output's numbers (AllowLeadingSign takes a '+' too)
        if (time.Length == 0 || time[0] == '+' || !long.TryParse(time, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long ticks))
          throw new InvalidDataException($"{timestampFile}:{lineNumber}: '{time}' is not a whole number of ticks ({TimeColumn})");
        var path = Path.IsPathRooted(name) ? name : Path.Combine(folder, name);
        if (!File.Exists(path))
          throw new FileNotFoundException($"{timestampFile}:{lineNumber}: '{path}' does not exist");
        frames.Add(new ImageSequenceFrame(path, new TickCount64(ticks)));
      }
      if (timeIndex < 0)
        throw new InvalidDataException(
          $"{timestampFile}:{lineNumber}: expected a header line with the columns {FileNameColumn} and {TimeColumn} (100 ns ticks)"
        );
      if (frames.Count == 0)
        throw new InvalidDataException($"The timestamp file '{timestampFile}' lists no images");
      return frames;
    }

    private static string Escape(string path) => path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);

    /// <summary>Orders "frame2" before "frame10".</summary>
    private sealed partial class NaturalComparer : IComparer<string>
    {
      public static readonly NaturalComparer Instance = new NaturalComparer();

      public int Compare(string? x, string? y)
      {
        if (x == null || y == null)
          return string.CompareOrdinal(x, y);
        var a = Chunks().Matches(x);
        var b = Chunks().Matches(y);
        for (int i = 0; i < Math.Min(a.Count, b.Count); ++i)
        {
          string ca = a[i].Value;
          string cb = b[i].Value;
          int result =
            char.IsDigit(ca[0]) && char.IsDigit(cb[0])
              ? decimal.Parse(ca, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(cb, CultureInfo.InvariantCulture))
              : string.Compare(ca, cb, StringComparison.OrdinalIgnoreCase);
          if (result != 0)
            return result;
        }
        return a.Count.CompareTo(b.Count);
      }

      [GeneratedRegex(@"\d+|\D+")]
      private static partial Regex Chunks();
    }
  }
}
