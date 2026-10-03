//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where playback reports go: a folder each in the analysis's playback folder, named like the SVG reports (analysis/playback/<prefix>,
//* analysis/playback/<prefix>-<from>s-<to>s), holding the page (index.html), the video it plays when that is the folder's own, and
//* playback.json. A report's folder is all of it: saving the same report again replaces that folder's files, and no other report's.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Globalization;
using System.IO;
using System.Text;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackFiles
  {
    /// <summary>The playback folder's name, in the analysis folder: a folder per report goes in it.</summary>
    public const string DirectoryName = "playback";

    /// <summary>The page in a report's folder.</summary>
    public const string PageName = "index.html";

    /// <summary>The name of a report's own video, before its extension.</summary>
    public const string VideoName = "video";

    /// <summary>The whole run, or <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> of it when either is given.</summary>
    public static RunSection Section(ChartRun run, double? fromSeconds, double? toSeconds)
    {
      var whole = RunSection.Whole(run);
      return fromSeconds.HasValue || toSeconds.HasValue ? RunSection.Create(run, fromSeconds ?? 0, toSeconds ?? whole.ToSeconds) : whole;
    }

    /// <summary>The folder of the report of <paramref name="section"/>, the run's file prefix <paramref name="prefix"/>, in <paramref name="playbackDirectory"/>.</summary>
    public static string FolderOf(string playbackDirectory, string prefix, RunSection section) =>
      Path.Combine(playbackDirectory, prefix + SectionSuffix(section));

    /// <summary>Write the page of <paramref name="section"/> into <paramref name="folder"/>, playing <paramref name="video"/>. Returns its path.</summary>
    public static string Write(RunSection section, string folder, PlaybackVideo video, ReportOptions? options = null, string toolVersion = "")
    {
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, PageName);
      string temporary = path + ".tmp";
      File.WriteAllText(temporary, PlaybackPage.Build(section, video, options, toolVersion), new UTF8Encoding(false));
      File.Move(temporary, path, overwrite: true);
      return path;
    }

    private static string SectionSuffix(RunSection section) =>
      section.IsWholeRun
        ? string.Empty
        : $"-{section.FromSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s-{section.ToSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
  }
}
