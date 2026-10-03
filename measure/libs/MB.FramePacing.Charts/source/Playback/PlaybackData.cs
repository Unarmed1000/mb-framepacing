//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The data a playback page carries inline (a JSON script element): the report's title and tiles, the plots of its card (where each panel is
//* on the card and which seconds it shows, so the page can draw the playhead and seek where it is clicked), the video, and the section's
//* frames as columns of whole ticks. Times are the capture's: for an import, the video's own timestamps (originTicks is the run's first
//* frame), so a video time in seconds times 10^7 minus originTicks is the report's time. The JSON escapes '<', '>' and '&', so it cannot
//* end its script element.
//*
//* The frame columns are written small (an hour at 240 Hz is 864,000 frames), in whole numbers the page turns back exactly:
//* - capture, index: the first value, then each frame's step from the one before;
//* - t: the first value, then what is left of the step from the frame before after its captures' whole periods
//*   (t[i] - t[i-1] - (capture[i] - capture[i-1]) * periodTicks: a tick or two of the recording's timestamps);
//* - step: its difference from the time to the section's next frame (t[i+1] - t[i]; 0 for the last one), null as null;
//* - error, hold, flags: as they are.
//* Each column is then {"values": [...]}, or {"rle": [value, count, value, count, ...]} when that is fewer numbers.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
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
    public const int FormatVersion = 2;

    /// <summary>
    /// The data of <paramref name="section"/> drawn as <paramref name="cards"/> (the whole report, then the zoom steps), playing
    /// <paramref name="video"/>: a file in the page's folder, named by its file name only, as nothing in the page names a local path.
    /// </summary>
    public static string Json(RunSection section, IReadOnlyList<PlaybackCard> cards, ReportOptions options, PlaybackVideo video, string toolVersion)
    {
      using var stream = new MemoryStream();
      Write(stream, section, cards, options, video, toolVersion);
      return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary><see cref="Json"/> written into <paramref name="stream"/> as UTF-8, as the page holds it: no text of it in memory.</summary>
    public static void Write(
      Stream stream,
      RunSection section,
      IReadOnlyList<PlaybackCard> cards,
      ReportOptions options,
      PlaybackVideo video,
      string toolVersion
    )
    {
      var run = section.Run;
      var data = section.Data;
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

        json.WriteStartArray("cards");
        foreach (var card in cards)
        {
          json.WriteStartObject();
          if (card.SecondsPerScreen is { } seconds)
            json.WriteNumber("secondsPerScreen", seconds);
          else
            json.WriteNull("secondsPerScreen");
          json.WriteNumber("width", card.Drawing.Width);
          json.WriteNumber("height", card.Drawing.Height);
          json.WriteStartArray("plots");
          // Every panel of the report card has the time across, each with its own range (the refresh strip may show only its first
          // seconds); a zoomed card's plots show its first screen, and move with its scrolling layers
          foreach (var plot in card.Drawing.Plots)
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
          json.WriteEndObject();
        }
        json.WriteEndArray();

        WriteVideo(json, video);
        WriteFrames(json, section);
        json.WriteEndObject();
      }
    }

    private static void WriteVideo(Utf8JsonWriter json, PlaybackVideo video)
    {
      json.WriteStartObject("video");
      // A video the user named goes in as given: the browser resolves a relative one against the page's address
      if (video.Url is { } named)
        json.WriteString("url", named);
      else if (video.VideoFile is { } file)
        json.WriteString("url", Uri.EscapeDataString(file));
      else
        json.WriteNull("url");
      json.WriteString("name", video.SourceName);
      json.WriteString("kind", video.Kind.ToString().ToLowerInvariant());
      json.WriteBoolean("playable", video.Playable);
      json.WriteString("description", video.Description);
      if (!video.Playable && video.Problem != null)
      {
        json.WriteString("problem", video.Problem);
        json.WriteString("command", "mb-framepacing render <capture folder> --playback --playback-transcode yes");
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
      int first = section.Start;
      long period = section.Run.CapturePeriod.Ticks;
      long Time(int i) => (frames[i].FirstSeenTime - data.Origin).Ticks;
      long CaptureStep(int i) => frames[i].FirstCaptureIndex - frames[i - 1].FirstCaptureIndex;
      long? Step(int i) => i + 1 < frames.Count ? frames[i + 1].DisplayDelta?.Ticks : null;
      json.WriteStartObject("frames");
      // The page reads capture before t, and t before step: each is written against the one before it
      WriteColumn(json, "capture", section, i => i == first ? frames[i].FirstCaptureIndex : CaptureStep(i));
      WriteColumn(json, "index", section, i => i == first ? (long)frames[i].FrameIndex : (long)frames[i].FrameIndex - (long)frames[i - 1].FrameIndex);
      WriteColumn(json, "t", section, i => i == first ? Time(i) : Time(i) - Time(i - 1) - (CaptureStep(i) * period));
      WriteColumn(json, "step", section, i => Step(i) is { } step ? step - (i + 1 < section.End ? Time(i + 1) - Time(i) : 0) : null);
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

    /// <summary>
    /// A column of the section's frames: its values, or runs of equal values (value, count) when that is fewer numbers. The values are
    /// asked for twice (to count the runs, then to write), so nothing of an hour's column is kept.
    /// </summary>
    private static void WriteColumn(Utf8JsonWriter json, string name, RunSection section, Func<int, long?> value)
    {
      int count = section.End - section.Start;
      int runs = 0;
      long? previous = null;
      for (int i = section.Start; i < section.End; ++i)
      {
        long? current = value(i);
        if (i == section.Start || current != previous)
          ++runs;
        previous = current;
      }

      void Number(long? number)
      {
        if (number is { } known)
          json.WriteNumberValue(known);
        else
          json.WriteNullValue();
        // The writer keeps what it wrote until it is flushed: an hour's columns go on to the stream as they are written
        if (json.BytesPending >= FlushBytes)
          json.Flush();
      }

      json.WriteStartObject(name);
      if (runs * 2 < count)
      {
        json.WriteStartArray("rle");
        int length = 0;
        for (int i = section.Start; i < section.End; ++i)
        {
          long? current = value(i);
          if (length > 0 && current != previous)
          {
            Number(previous);
            Number(length);
            length = 0;
          }
          previous = current;
          ++length;
        }
        if (length > 0)
        {
          Number(previous);
          Number(length);
        }
      }
      else
      {
        json.WriteStartArray("values");
        for (int i = section.Start; i < section.End; ++i)
          Number(value(i));
      }
      json.WriteEndArray();
      json.WriteEndObject();
    }

    /// <summary>How much the JSON writer holds before it hands it on to the stream.</summary>
    private const int FlushBytes = 1 << 16;
  }
}
