//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Opening a trace in Perfetto's viewer at ui.perfetto.dev: the link that has it load a trace from this machine, at the run's stretch of
//* time with the capture's tracks pinned. The viewer loads a trace from this machine only from http://127.0.0.1:9001 (its content security
//* policy; Perfetto's own tools/open_trace_in_ui serves from there), and runs in the browser: the trace is not uploaded.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The link that opens a trace served from this machine in ui.perfetto.dev.</summary>
  public static class PerfettoUi
  {
    /// <summary>The viewer's origin: what a server of the trace allows to read it.</summary>
    public const string Origin = "https://ui.perfetto.dev";

    /// <summary>The one port on this machine the viewer loads a trace from.</summary>
    public const int Port = 9001;

    /// <summary>The address a trace named <paramref name="fileName"/> is served at.</summary>
    public static string TraceUrl(string fileName, int port = Port) =>
      $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/{Uri.EscapeDataString(fileName)}";

    /// <summary>
    /// The link that has the viewer load the trace served at <paramref name="traceUrl"/>: at <paramref name="visible"/> (raw times of the
    /// trace's clock, in nanoseconds) when given, with the tracks of a run's capture expanded and its refreshes, its frames on screen and
    /// their CPU work pinned to the top, next to the application's frame record.
    /// </summary>
    public static string Link(string traceUrl, (long FirstNs, long LastNs)? visible)
    {
      string link = Origin + "/#!/?url=" + Uri.EscapeDataString(traceUrl);
      if (visible is { } range && range.LastNs > range.FirstNs)
        link += FormattableString.Invariant($"&visStart={range.FirstNs}&visEnd={range.LastNs}");
      return link + "&startupCommands=" + Uri.EscapeDataString(StartupCommands());
    }

    /// <summary>
    /// Where the tracks the tools added to <paramref name="tracePath"/> are, read with the trace processor at <paramref name="processorPath"/>:
    /// the stretch to open the viewer at. Null where the trace processor can not tell (the viewer then shows the whole trace).
    /// </summary>
    public static (long FirstNs, long LastNs)? AddedRangeOf(string tracePath, string processorPath)
    {
      try
      {
        using var trace = TraceProcessor.Start(processorPath, tracePath);
        return TraceReader.AddedRange(trace);
      }
      catch (TraceException)
      {
        return null;
      }
    }

    /// <summary>The viewer's commands the link runs once the trace is loaded, as the JSON array it takes.</summary>
    public static string StartupCommands()
    {
      string pinned = string.Join(
        "|",
        new[]
        {
          TraceTrackText.NameOf(TraceTrack.Refreshes),
          TraceTrackText.NameOf(TraceTrack.OnScreen),
          TraceTrackText.NameOf(TraceTrack.Cpu),
          FrameRecordText.TrackName,
        }.Select(EscapeRegex)
      );
      var commands = new object[]
      {
        new { id = "dev.perfetto.ExpandTracksByRegex", args = new[] { "^" + EscapeRegex(TraceTrackWriter.RootNamePrefix) + ".*" } },
        new { id = "dev.perfetto.PinTracksByRegex", args = new[] { "^(" + pinned + ")$" } },
      };
      return JsonSerializer.Serialize(commands);
    }

    /// <summary>A name as a regular expression that matches it alone: the few characters our names have that a regular expression reads.</summary>
    private static string EscapeRegex(string name)
    {
      var text = new System.Text.StringBuilder(name.Length + 8);
      foreach (char c in name)
      {
        if ("\\^$.|?*+()[]{}".Contains(c, StringComparison.Ordinal))
          text.Append('\\');
        text.Append(c);
      }
      return text.ToString();
    }
  }
}
