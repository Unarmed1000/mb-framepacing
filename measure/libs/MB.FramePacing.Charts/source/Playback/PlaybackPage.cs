//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A playback page: one HTML file that runs from the disk (no server, nothing loaded from the network) with the run's report card next to
//* the recording it was measured from, a player bar to play, step and scrub it, and a playhead on the report where the frame on screen is.
//* The page's markup, style and script are the template PlaybackPage.html (an embedded resource); this fills in the title, the report cards
//* (SVG, inline: the whole report, and the zoom steps that fit, PlaybackZoom, with their scrolling layers kept for the page to move) and the
//* data (PlaybackData).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackPage
  {
    private const string TemplateName = "MB.FramePacing.Charts.Playback.PlaybackPage.html";
    private const string TitleToken = "__PB_TITLE__";
    private const string ReportToken = "<!--PB:REPORT-->";
    private const string DataToken = "<!--PB:DATA-->";

    /// <summary>The id of the script element that holds the data.</summary>
    public const string DataElementId = "pb-data";

    private static readonly Lazy<string> g_template = new Lazy<string>(() =>
    {
      using var stream =
        Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateName)
        ?? throw new InvalidOperationException($"The resource {TemplateName} is missing");
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd();
    });

    /// <summary>The page of <paramref name="section"/> in <paramref name="pageDirectory"/>, playing <paramref name="video"/>.</summary>
    public static string Build(
      RunSection section,
      PlaybackVideo video,
      string pageDirectory,
      string analysisDirectory,
      ReportOptions? options = null,
      string toolVersion = ""
    )
    {
      options ??= ReportOptions.Default;
      var cards = Cards(section, options);
      // The data and every card's SVG at once: they only read the cards
      var data = Task.Run(() => PlaybackData.Json(section, cards, options, video, pageDirectory, analysisDirectory, toolVersion));
      var svgs = new string[cards.Count];
      Parallel.For(
        0,
        cards.Count,
        i => svgs[i] = SvgCardWriter.Write(cards[i].Drawing, null, cards[i].SecondsPerScreen != null ? $"pb-card-{i}" : null)
      );
      string json = data.GetAwaiter().GetResult();
      string title = (options.Title ?? RunHeadline.Title(section.Run.Run)) + SectionTitle(section) + " · playback";
      // Each card in its own view, the whole report first and shown. A zoomed card keeps its scrolling layers, and waits as text (an inert
      // script element) until it is first shown: a page of an hour parses only the cards it shows
      var views = new StringBuilder();
      for (int i = 0; i < cards.Count; ++i)
      {
        string svg = svgs[i];
        if (i == 0)
        {
          views.Append(CultureInfo.InvariantCulture, $"<div class=\"pb-card-view\" data-card=\"{i}\">{svg}</div>");
          continue;
        }
        // Card text has no script end tag or comment, which would end or confuse the script element
        if (svg.Contains("</script", StringComparison.OrdinalIgnoreCase) || svg.Contains("<!--", StringComparison.Ordinal))
          throw new InvalidOperationException("A report card's SVG can not be kept in a script element");
        views.Append(CultureInfo.InvariantCulture, $"<div class=\"pb-card-view\" data-card=\"{i}\" hidden></div>");
        views.Append(CultureInfo.InvariantCulture, $"<script type=\"text/plain\" id=\"pb-card-source-{i}\">{svg}</script>");
      }
      return g_template
        .Value.Replace(TitleToken, WebUtility.HtmlEncode(title), StringComparison.Ordinal)
        .Replace(ReportToken, views.ToString(), StringComparison.Ordinal)
        .Replace(DataToken, $"<script id=\"{DataElementId}\" type=\"application/json\">{json}</script>", StringComparison.Ordinal);
    }

    /// <summary>
    /// The page's report cards of <paramref name="section"/>: the whole report, then each zoom step that fits (<see cref="PlaybackZoom"/>),
    /// its plots showing the section's first seconds. The page's header has the title and the headline tiles (from the same options): the
    /// cards have the rest. The cards are built at once (each also builds its panels at once); the run's prepared data they share is made
    /// once (RunChartData).
    /// </summary>
    public static IReadOnlyList<PlaybackCard> Cards(RunSection section, ReportOptions options)
    {
      var cardOptions = options.Hide(new[] { ReportItem.Title, ReportItem.Tiles });
      double from = section.FromSeconds;
      var steps = PlaybackZoom.Steps(section.ToSeconds - from, section.FrameCount, ReportCard.PlotWidth(ReportCard.Width));
      var cards = new PlaybackCard[steps.Count + 1];
      Parallel.For(
        0,
        cards.Length,
        i =>
          cards[i] =
            i == 0
              ? new PlaybackCard(null, ReportCard.Build(section, cardOptions))
              : new PlaybackCard(steps[i - 1], ReportCard.Build(section, cardOptions, visible: (from, from + steps[i - 1])))
      );
      return cards;
    }

    private static string SectionTitle(RunSection section) =>
      section.IsWholeRun ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", {section.FromSeconds:0.###}–{section.ToSeconds:0.###} s");
  }
}
