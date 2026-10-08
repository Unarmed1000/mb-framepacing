//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a run's SVG reports (ReportCard) next to its other reports: the whole run or a section (<prefix>-report.svg,
//* <prefix>-report-<from>s-<to>s.svg), optionally the detail sections around the worst animation error and the worst 2 s of late frames, the
//* distribution cards (DistributionCard, <prefix>-<card>.svg) and the frame timeline, and optionally each as a PNG through a headless browser.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

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
      var section = Section(run, fromSeconds, toSeconds);
      written.AddRange(WriteOne(ReportCard.Render(section, options), Path.Combine(directory, prefix + "-report" + SectionSuffix(section)), png));
      if (details)
      {
        foreach (var (name, detail) in Details(run))
          written.AddRange(WriteOne(ReportCard.Render(detail, options), Path.Combine(directory, $"{prefix}-report-{name}"), png));
      }
      return written;
    }

    /// <summary>
    /// Write the distribution cards <paramref name="cards"/> (<see cref="DistributionCard"/> ids) of <paramref name="run"/>, all of it or
    /// <paramref name="fromSeconds"/> to <paramref name="toSeconds"/>, as &lt;prefix&gt;-&lt;card&gt;.svg (and .png). Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> WriteCards(
      ChartRun run,
      string prefix,
      string directory,
      IEnumerable<string> cards,
      double? fromSeconds = null,
      double? toSeconds = null,
      bool png = false
    )
    {
      var section = Section(run, fromSeconds, toSeconds);
      var written = new List<string>();
      foreach (string card in cards)
        written.AddRange(WriteOne(DistributionCard.Render(card, section), Path.Combine(directory, $"{prefix}-{card}{SectionSuffix(section)}"), png));
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
      var origin = frames[0].FirstSeenTime;
      double Seconds(PresentedFrame f) => (f.FirstSeenTime - origin).TotalSeconds;
      var worstError = frames.Where(f => f.AnimationError.HasValue).MaxBy(f => f.AnimationError!.Value.Duration());
      if (worstError != null && worstError.AnimationError != NanosecondTimeSpan.Zero)
      {
        double t = Seconds(worstError);
        yield return ("worst-error", RunSection.Create(run, t - DetailSeconds, t + DetailSeconds));
      }
      if (run.Run.Pacing is { LateFrames: > 0 })
      {
        // As LateShare.Worst: only the windows that lie completely inside the run (the first frames' windows are nearly empty)
        var shares = LateShare.Rolling(frames, LateShare.Window);
        int worst = 0;
        for (int i = 0; i < frames.Count; ++i)
        {
          if (frames[i].FirstSeenTime - origin >= LateShare.Window && shares[i] > shares[worst])
            worst = i;
        }
        double t = Seconds(frames[worst]);
        yield return ("worst-late", RunSection.Create(run, t - LateShare.WindowSeconds - (DetailSeconds / 2), t + (DetailSeconds / 2)));
      }
    }

    /// <summary>The seconds the frame timeline shows when only its start is given.</summary>
    public const double TimelineSeconds = 0.25;

    /// <summary>
    /// Write the frame timeline card (<see cref="FrameTimelineCard"/>) of <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> of
    /// <paramref name="run"/> as &lt;prefix&gt;-timeline-&lt;from&gt;s-&lt;to&gt;s.svg (and .png). Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> WriteTimeline(
      ChartRun run,
      string prefix,
      string directory,
      double fromSeconds,
      double toSeconds,
      bool png = false
    )
    {
      var section = RunSection.Create(run, fromSeconds, toSeconds);
      return WriteOne(FrameTimelineCard.Render(section), Path.Combine(directory, prefix + "-timeline" + SectionSuffix(section)), png).ToList();
    }

    /// <summary>The whole run, or <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> of it when either is given.</summary>
    private static RunSection Section(ChartRun run, double? fromSeconds, double? toSeconds)
    {
      var whole = RunSection.Whole(run);
      return fromSeconds.HasValue || toSeconds.HasValue ? RunSection.Create(run, fromSeconds ?? 0, toSeconds ?? whole.ToSeconds) : whole;
    }

    private static IEnumerable<string> WriteOne(string content, string pathWithoutExtension, bool png)
    {
      string svg = pathWithoutExtension + ".svg";
      File.WriteAllText(svg, content, new UTF8Encoding(false));
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
