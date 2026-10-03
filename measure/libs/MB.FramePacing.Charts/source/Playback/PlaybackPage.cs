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

    /// <summary>The template's text around its placeholders: before the title, before the report cards, before the data, after it.</summary>
    private static readonly Lazy<string[]> g_template = new Lazy<string[]>(() =>
    {
      using var stream =
        Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateName)
        ?? throw new InvalidOperationException($"The resource {TemplateName} is missing");
      using var reader = new StreamReader(stream);
      string template = reader.ReadToEnd();
      var parts = new List<string>();
      int at = 0;
      foreach (string token in new[] { TitleToken, ReportToken, DataToken })
      {
        int found = template.IndexOf(token, at, StringComparison.Ordinal);
        if (found < 0 || template.IndexOf(token, found + token.Length, StringComparison.Ordinal) >= 0)
          throw new InvalidOperationException($"The template must have {token} once, after the placeholders before it");
        parts.Add(template[at..found]);
        at = found + token.Length;
      }
      parts.Add(template[at..]);
      return parts.ToArray();
    });

    /// <summary>The page of <paramref name="section"/>, playing <paramref name="video"/> (a file in the page's folder), as text.</summary>
    public static string Build(RunSection section, PlaybackVideo video, ReportOptions? options = null, string toolVersion = "")
    {
      using var stream = new MemoryStream();
      Write(stream, section, video, options, toolVersion);
      return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// The page of <paramref name="section"/>, playing <paramref name="video"/>, written into <paramref name="output"/> as UTF-8 piece by
    /// piece: the template's text, each card's SVG, then the data straight from its writer. A page of an hour is tens of megabytes; it is
    /// never all in memory at once.
    /// </summary>
    public static void Write(Stream output, RunSection section, PlaybackVideo video, ReportOptions? options = null, string toolVersion = "")
    {
      options ??= ReportOptions.Default;
      var cards = Cards(section, options);
      var svgs = new string[cards.Count];
      Parallel.For(
        0,
        cards.Count,
        i => svgs[i] = SvgCardWriter.Write(cards[i].Drawing, null, cards[i].SecondsPerScreen != null ? $"pb-card-{i}" : null)
      );
      string title = (options.Title ?? RunHeadline.Title(section.Run.Run)) + SectionTitle(section) + " · playback";
      var template = g_template.Value;
      var encoding = new UTF8Encoding(false);
      using (var writer = new StreamWriter(output, encoding, 1 << 16, leaveOpen: true))
      {
        writer.Write(template[0]);
        writer.Write(WebUtility.HtmlEncode(title));
        writer.Write(template[1]);
        // Each card in its own view, the whole report first and shown. A zoomed card keeps its scrolling layers, and waits as text (an inert
        // script element) until it is first shown: a page of an hour parses only the cards it shows
        for (int i = 0; i < cards.Count; ++i)
        {
          string svg = svgs[i];
          if (i == 0)
          {
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"<div class=\"pb-card-view\" data-card=\"{i}\">"));
            writer.Write(svg);
            writer.Write("</div>");
            continue;
          }
          // Card text has no script end tag or comment, which would end or confuse the script element
          if (svg.Contains("</script", StringComparison.OrdinalIgnoreCase) || svg.Contains("<!--", StringComparison.Ordinal))
            throw new InvalidOperationException("A report card's SVG can not be kept in a script element");
          writer.Write(string.Create(CultureInfo.InvariantCulture, $"<div class=\"pb-card-view\" data-card=\"{i}\" hidden></div>"));
          writer.Write(string.Create(CultureInfo.InvariantCulture, $"<script type=\"text/plain\" id=\"pb-card-source-{i}\">"));
          writer.Write(svg);
          writer.Write("</script>");
          // Written: the page needs it no more
          svgs[i] = null!;
        }
        writer.Write(template[2]);
        writer.Write($"<script id=\"{DataElementId}\" type=\"application/json\">");
      }
      PlaybackData.Write(output, section, cards, options, video, toolVersion);
      using (var writer = new StreamWriter(output, encoding, 1 << 16, leaveOpen: true))
      {
        writer.Write("</script>");
        writer.Write(template[3]);
      }
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
