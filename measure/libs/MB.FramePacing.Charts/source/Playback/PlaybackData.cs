//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data a playback page carries inline (a JSON script element): the report's title and tiles, the plots of its card (where each panel is
//* on the card and which seconds it shows, so the page can draw the playhead and seek where it is clicked), the video, and the section's
//* frames as columns of whole ticks. Times are the capture's: for an import, the video's own timestamps (originTicks is the run's first
//* frame), so a video time in seconds times 10^7 minus originTicks is the report's time. The JSON escapes '<', '>' and '&', so it cannot
//* end its script element.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackData
  {
    /// <summary>The format of the data; the page's script reads this one.</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The data of <paramref name="section"/> drawn as <paramref name="card"/> (its <see cref="ReportCard"/>), playing <paramref name="video"/>
    /// from a page in <paramref name="pageDirectory"/>.
    /// </summary>
    public static string Json(
      RunSection section,
      CardDrawing card,
      ReportOptions options,
      PlaybackVideo video,
      string pageDirectory,
      string analysisDirectory,
      string toolVersion
    )
    {
      var run = section.Run;
      var data = section.Data;
      using var stream = new MemoryStream();
      using (var json = new Utf8JsonWriter(stream))
      {
        json.WriteStartObject();
        json.WriteNumber("formatVersion", FormatVersion);
        json.WriteString("title", options.Title ?? RunHeadline.Title(run.Run));
        if (run.Run.StartTimeUtc is { } start)
          json.WriteString("startedUtc", start.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
        json.WriteString("toolVersion", toolVersion);

        json.WriteStartObject("section");
        json.WriteNumber("from", section.FromSeconds);
        json.WriteNumber("to", section.ToSeconds);
        json.WriteBoolean("whole", section.IsWholeRun);
        json.WriteEndObject();

        json.WriteNumber("periodTicks", run.CapturePeriod.Ticks);
        json.WriteNumber("originTicks", data.Origin.Ticks);
        // Two captures far apart (the run's first and last frames' first sightings) give the video frame of any time without the drift of
        // a rounded period: video frame = first + (time - its time) * (last - first) / (last's time - first's time)
        if (data.Frames.Count > 0)
        {
          var first = data.Frames[0];
          var last = data.Frames[^1];
          json.WriteStartObject("captures");
          json.WriteNumber("firstIndex", first.FirstCaptureIndex);
          json.WriteNumber("firstTicks", (first.FirstSeenTime - data.Origin).Ticks);
          json.WriteNumber("lastIndex", last.FirstCaptureIndex);
          json.WriteNumber("lastTicks", (last.FirstSeenTime - data.Origin).Ticks);
          json.WriteEndObject();
        }
        if (run.Run.Pacing is { } pacing)
          json.WriteNumber("refreshHz", pacing.RefreshHz);

        json.WriteStartArray("tiles");
        foreach (var tile in RunHeadline.Shown(section.Section, options))
        {
          json.WriteStartObject();
          json.WriteString("caption", tile.Caption);
          json.WriteString("value", tile.Value);
          json.WriteString("detail", tile.Detail);
          json.WriteBoolean("warning", tile.Warning);
          json.WriteString("explanation", tile.Explanation);
          json.WriteEndObject();
        }
        json.WriteEndArray();

        json.WriteStartObject("card");
        json.WriteNumber("width", card.Width);
        json.WriteNumber("height", card.Height);
        json.WriteEndObject();
        json.WriteStartArray("plots");
        // Every panel of the report card has the time across: each with its own range (the refresh strip may show only its first seconds)
        foreach (var plot in card.Plots)
        {
          json.WriteStartObject();
          json.WriteString("id", plot.Id);
          json.WriteNumber("left", plot.Left);
          json.WriteNumber("top", plot.Top);
          json.WriteNumber("right", plot.Right);
          json.WriteNumber("bottom", plot.Bottom);
          json.WriteNumber("xFrom", plot.XFrom);
          json.WriteNumber("xTo", plot.XTo);
          json.WriteEndObject();
        }
        json.WriteEndArray();

        WriteVideo(json, video, pageDirectory, analysisDirectory);
        WriteFrames(json, section);
        json.WriteEndObject();
      }
      return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteVideo(Utf8JsonWriter json, PlaybackVideo video, string pageDirectory, string analysisDirectory)
    {
      string path = video.PathIn(pageDirectory);
      json.WriteStartObject("video");
      json.WriteString("url", PlaybackUrl.For(pageDirectory, path));
      json.WriteString("name", Path.GetFileName(video.Source));
      json.WriteString("kind", video.Kind.ToString().ToLowerInvariant());
      json.WriteBoolean("playable", video.Playable);
      json.WriteString("description", video.Description);
      if (!video.Playable && video.Problem != null)
      {
        json.WriteString("problem", video.Problem);
        json.WriteString("command", $"mb-framepacing render \"{analysisDirectory}\" --playback --playback-transcode yes");
      }
      json.WriteEndObject();
    }

    /// <summary>
    /// The section's frames, a column each: first seen (ticks since the run's first frame), the application's frame index, the first capture
    /// that showed it (the video frame), its display time step (until the next frame; null for the run's last), the animation error of the step
    /// into it (null when not judged), the kind of its hold (<see cref="HoldKind"/>) and its flags (<see cref="PresentedFrameFlags"/>).
    /// </summary>
    private static void WriteFrames(Utf8JsonWriter json, RunSection section)
    {
      var data = section.Data;
      var frames = data.Frames;
      var holds = data.HoldKinds;
      json.WriteStartObject("frames");
      WriteColumn(json, "t", section, i => (frames[i].FirstSeenTime - data.Origin).Ticks);
      WriteColumn(json, "index", section, i => (long)frames[i].FrameIndex);
      WriteColumn(json, "capture", section, i => frames[i].FirstCaptureIndex);
      WriteColumn(json, "step", section, i => i + 1 < frames.Count ? frames[i + 1].DisplayDelta?.Ticks : null);
      WriteColumn(json, "error", section, i => frames[i].AnimationError?.Ticks);
      WriteColumn(json, "hold", section, i => (long)holds[i]);
      WriteColumn(json, "flags", section, i => (long)frames[i].Flags);
      json.WriteEndObject();

      json.WriteStartArray("holdKinds");
      foreach (var kind in Enum.GetValues<HoldKind>())
        json.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));
      json.WriteEndArray();
      json.WriteStartObject("flagBits");
      foreach (var flag in Enum.GetValues<PresentedFrameFlags>().Where(f => f != PresentedFrameFlags.None))
        json.WriteNumber(JsonNamingPolicy.CamelCase.ConvertName(flag.ToString()), (int)flag);
      json.WriteEndObject();
    }

    private static void WriteColumn(Utf8JsonWriter json, string name, RunSection section, Func<int, long?> value)
    {
      json.WriteStartArray(name);
      for (int i = section.Start; i < section.End; ++i)
      {
        if (value(i) is { } number)
          json.WriteNumberValue(number);
        else
          json.WriteNullValue();
      }
      json.WriteEndArray();
    }
  }
}
