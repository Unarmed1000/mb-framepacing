//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a run's SVG reports (ReportSvg) next to its other reports: the whole run or a section (<prefix>-report.svg,
//* <prefix>-report-<from>s-<to>s.svg), optionally the detail sections around the worst animation error and the worst 2 s of late frames, and
//* optionally each as a PNG through a headless browser.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class ReportFiles
  {
    /// <summary>The seconds a detail section shows before and after its moment.</summary>
    public const double DetailSeconds = 2;

    /// <summary>
    /// Write the report of <paramref name="run"/> (all of it, or <paramref name="fromSeconds"/> to <paramref name="toSeconds"/>) into
    /// <paramref name="directory"/>, named after <paramref name="prefix"/>; with <paramref name="details"/> also the worst moments; with
    /// <paramref name="png"/> each also as a PNG; <paramref name="options"/> chooses the card's items. Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> Write(
      ChartRun run,
      string prefix,
      string directory,
      double? fromSeconds = null,
      double? toSeconds = null,
      bool details = false,
      bool png = false,
      ReportOptions? options = null
    )
    {
      var written = new List<string>();
      var whole = RunSection.Whole(run);
      var section = fromSeconds.HasValue || toSeconds.HasValue ? RunSection.Create(run, fromSeconds ?? 0, toSeconds ?? whole.ToSeconds) : whole;
      written.AddRange(WriteOne(section, Path.Combine(directory, prefix + "-report" + SectionSuffix(section)), png, options));
      if (details)
      {
        foreach (var (name, detail) in Details(run))
          written.AddRange(WriteOne(detail, Path.Combine(directory, $"{prefix}-report-{name}"), png, options));
      }
      return written;
    }

    /// <summary>
    /// The sections around the run's worst moments: the frame with the largest animation error ("worst-error") and the end of the 2 s window
    /// with the highest share of late frames ("worst-late"), <see cref="DetailSeconds"/> either way.
    /// </summary>
    public static IEnumerable<(string Name, RunSection Section)> Details(ChartRun run)
    {
      var frames = run.Run.Frames;
      if (frames.Count == 0)
        yield break;
      long origin = frames[0].FirstSeenTicks;
      double Seconds(PresentedFrame f) => (f.FirstSeenTicks - origin) / (double)TimeSpan.TicksPerSecond;
      var worstError = frames.Where(f => f.AnimationErrorTicks.HasValue).MaxBy(f => Math.Abs(f.AnimationErrorTicks!.Value));
      if (worstError != null && worstError.AnimationErrorTicks != 0)
      {
        double t = Seconds(worstError);
        yield return ("worst-error", RunSection.Create(run, t - DetailSeconds, t + DetailSeconds));
      }
      if (run.Run.Pacing is { LateFrames: > 0 })
      {
        // As LateShare.Worst: only the windows that lie completely inside the run (the first frames' windows are nearly empty)
        var shares = LateShare.Rolling(frames, LateShare.WindowTicks);
        int worst = 0;
        for (int i = 0; i < frames.Count; ++i)
        {
          if (frames[i].FirstSeenTicks - origin >= LateShare.WindowTicks && shares[i] > shares[worst])
            worst = i;
        }
        double t = Seconds(frames[worst]);
        yield return ("worst-late", RunSection.Create(run, t - LateShare.WindowSeconds - (DetailSeconds / 2), t + (DetailSeconds / 2)));
      }
    }

    private static IEnumerable<string> WriteOne(RunSection section, string pathWithoutExtension, bool png, ReportOptions? options)
    {
      string svg = pathWithoutExtension + ".svg";
      File.WriteAllText(svg, ReportSvg.Render(section, options), new UTF8Encoding(false));
      yield return svg;
      if (png)
      {
        string image = pathWithoutExtension + ".png";
        HeadlessBrowser.SavePng(svg, image);
        yield return image;
      }
    }

    private static string SectionSuffix(RunSection section) =>
      section.IsWholeRun
        ? string.Empty
        : $"-{section.FromSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s-{section.ToSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
  }
}
