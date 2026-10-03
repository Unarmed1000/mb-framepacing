//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A playback page: one HTML file that runs from the disk (no server, nothing loaded from the network) with the run's report card next to
//* the recording it was measured from, a player bar to play, step and scrub it, and a playhead on the report where the frame on screen is.
//* The page's markup, style and script are the template PlaybackPage.html (an embedded resource); this fills in the title, the report card
//* (SVG, inline) and the data (PlaybackData).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;

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
      // The page's header has the title and the headline tiles (from the same options): the card has the rest
      var card = ReportCard.Build(section, options.Hide(new[] { ReportItem.Title, ReportItem.Tiles }));
      string json = PlaybackData.Json(section, card, options, video, pageDirectory, analysisDirectory, toolVersion);
      string title = (options.Title ?? RunHeadline.Title(section.Run.Run)) + SectionTitle(section) + " · playback";
      return g_template
        .Value.Replace(TitleToken, WebUtility.HtmlEncode(title), StringComparison.Ordinal)
        .Replace(ReportToken, SvgCardWriter.Write(card), StringComparison.Ordinal)
        .Replace(DataToken, $"<script id=\"{DataElementId}\" type=\"application/json\">{json}</script>", StringComparison.Ordinal);
    }

    private static string SectionTitle(RunSection section) =>
      section.IsWholeRun ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", {section.FromSeconds:0.###}–{section.ToSeconds:0.###} s");
  }
}
