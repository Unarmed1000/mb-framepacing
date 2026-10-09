//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL. A whole run of a frame log as a trace file in the Chrome trace event format (Perfetto's trace viewer opens it): a track
//* per resource, a span per stage named by its frame, arrows that join a frame's stages, the refreshes, and the pacer's expected against
//* the actual. Times are microseconds from the run's first moment, written from whole ticks, so nothing is lost.
//*
//* The text is written piece by piece, in the form the script this is a port of gave: compact, a number that is whole with ".0" where it
//* is a time, text outside ASCII as escapes.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using static MB.FramePacing.Charts.Stages.FrameStagesTips;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. A frame log's run as a Chrome trace event file.</summary>
  public static class FrameStagesTrace
  {
    private const int Process = 1;
    private const int CpuTrack = 1;
    private const int ExpectedTrack = 2;
    private const int VSyncTrack = 4;
    private const int DisplayTrack = 5;
    private const int AnimationTrack = 6;

    // Successive frames' GPU work, their waits and their way from start to shown overlap, and spans on one track may not: they go round a
    // few tracks of the same name
    private const int GpuTrack = 10;
    private const int QueueTrack = 20;
    private const int ActualTrack = 30;
    private const int Pool = 4;

    /// <summary>The trace file's text for the run <paramref name="name"/> with <paramref name="frames"/> (at least one).</summary>
    public static string Write(string name, IReadOnlyList<FrameLogFrame> frames)
    {
      long period = FrameLogStages.RefreshPeriod(frames);
      long zero = frames.Min(frame =>
        new[] { "hostCpuStartTicks", "frameWaitStartTicks", "frameStartTicks" }.Where(frame.Values.ContainsKey).Min(key => frame.Values[key])
      );
      var events = new List<string> { $"{{\"ph\":\"M\",\"pid\":{Process},\"name\":\"process_name\",\"args\":{{\"name\":{Json(name)}}}}}" };

      void Track(int tid, string title, int order)
      {
        events.Add($"{{\"ph\":\"M\",\"pid\":{Process},\"tid\":{tid},\"name\":\"thread_name\",\"args\":{{\"name\":{Json(title)}}}}}");
        events.Add($"{{\"ph\":\"M\",\"pid\":{Process},\"tid\":{tid},\"name\":\"thread_sort_index\",\"args\":{{\"sort_index\":{order}}}}}");
      }

      Track(ExpectedTrack, "Pacer: expected", 0);
      Track(CpuTrack, "CPU", 9);
      for (int slot = 0; slot < Pool; ++slot)
      {
        Track(ActualTrack + slot, "Actual: start to shown", 1 + slot);
        Track(GpuTrack + slot, "GPU", 10 + slot);
        Track(QueueTrack + slot, "Waiting to be shown", 20 + slot);
      }
      Track(DisplayTrack, "Display", 30);
      Track(AnimationTrack, "By its animation time", 31);
      Track(VSyncTrack, "Vertical blank (as read)", 32);

      // A span's arguments: the frame's index first, then what is given, each a name and its value as JSON
      void Span(int tid, string title, long first, long last, FrameLogFrame frame, params (string Name, string Value)[] arguments)
      {
        var details = new StringBuilder($"{{\"frame\":{Whole(frame.Index)}");
        foreach (var (argument, value) in arguments)
          details.Append(',').Append(Json(argument)).Append(':').Append(value);
        details.Append('}');
        events.Add(
          $"{{\"ph\":\"X\",\"pid\":{Process},\"tid\":{tid},\"name\":{Json(title)},\"ts\":{Microseconds(first - zero)},"
            + $"\"dur\":{Microseconds(Math.Max(last - first, 1))},\"args\":{details}}}"
        );
      }

      void Flow(string phase, int tid, long at, FrameLogFrame frame) =>
        events.Add(
          $"{{\"ph\":\"{phase}\",\"pid\":{Process},\"tid\":{tid},\"name\":\"frame\",\"cat\":\"frame\",\"id\":{Whole(frame.Index)},"
            + $"\"ts\":{Microseconds(at - zero)}{(phase == "f" ? ",\"bp\":\"e\"" : string.Empty)}}}"
        );

      var seen = new HashSet<long>();
      long expectedEnd = zero;
      long lastReported = frames[^1].Index - FrameLog.SkipLast;
      var tied = FrameLogStages.AnimationTie(frames);
      for (int position = 0; position < frames.Count; ++position)
      {
        var frame = frames[position];
        string label = $"#{Whole(frame.Index)}";
        int slot = position % Pool;
        var draw = frame.Span("frameStartTicks", "endFrameTicks");
        var stages = new (string Title, (long First, long Last)? Stage)[]
        {
          ("update", frame.Span("hostCpuStartTicks", "hostUpdateEndTicks")),
          ("wait for the frame start", FrameLogStages.StartWait(frame)),
          ("wait for a frame slot", frame.Span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks")),
          ("acquire", frame.Span("acquireCallTicks", "acquireReturnTicks")),
          ("draw", draw),
          ("hold before the present", FrameLogStages.PresentHold(frame)),
          ("present", frame.Span("presentCallTicks", "presentReturnTicks")),
        };
        long cpuEnd = zero;
        foreach (var (title, stage) in stages)
        {
          // Stages on one track may not overlap: one that starts before the last one ended starts where that ended
          if (stage is { } known && known.Last > Math.Max(known.First, cpuEnd))
          {
            Span(CpuTrack, $"{title} {label}", Math.Max(known.First, cpuEnd), known.Last, frame);
            cpuEnd = known.Last;
          }
        }
        // The submit is inside the draw: a span within a span
        if (
          frame.Span("submitCallTicks", "submitReturnTicks") is { } submit
          && draw is { } drawn
          && drawn.First <= submit.First
          && submit.First < submit.Last
          && submit.Last <= drawn.Last
        )
          Span(CpuTrack, $"submit {label}", submit.First, submit.Last, frame);
        var screen = FrameLogStages.OnScreen(frames, position, period);
        var work = frame.Span("gpuWorkBeginTicks", "gpuWorkEndTicks");
        long? ready = frame.At("presentReturnTicks");
        if (work is { } worked)
        {
          Span(GpuTrack + slot, $"GPU {label}", worked.First, worked.Last, frame);
          ready = Math.Max(worked.Last, ready is { } returned and not 0 ? returned : worked.Last);
          if (draw != null)
          {
            Flow("s", CpuTrack, frame.Start, frame);
            Flow("t", GpuTrack + slot, worked.First, frame);
          }
        }
        if (ready is { } readyAt && screen != null && screen.Shown > readyAt)
          Span(QueueTrack + slot, $"waiting {label}", readyAt, screen.Shown, frame);
        if (screen != null && screen.End > screen.Shown)
        {
          bool longer = FrameLogStages.HeldLonger(frame, screen.Shown, screen.End, period);
          var details = new List<(string Name, string Value)>
          {
            ("swap interval", Whole(frame.SwapInterval)),
            ("display time", Json("as the graphics driver reported it")),
          };
          if (FrameLogStages.AnimationError(frames, position) is { } error)
            details.Add(("animation error (ms)", Number(SignedMs(error, 4))));
          Span(DisplayTrack, longer ? $"{label} (longer than asked)" : label, screen.Shown, screen.End, frame, details.ToArray());
          if (screen.UnknownEnd is { } unknownEnd)
          {
            // The log has no display time for the frames presented next: who was on screen is not known
            var reasons = screen
              .Unreported.Select(later => ($"#{Whole(later.Index)}", Json(FrameLogStages.NoDisplayTime(later, lastReported))))
              .ToArray();
            Span(
              DisplayTrack,
              $"not known: {label} or {string.Join(", ", reasons.Select(reason => reason.Item1))}",
              screen.End,
              unknownEnd,
              frame,
              reasons
            );
          }
          if (work is { } gpu && draw != null)
            Flow("f", DisplayTrack, screen.Shown, frame);
          Span(ActualTrack + slot, label, frame.Start, screen.Shown, frame);
        }
        // Where the animation clock has the frame, tied to the display at the run's first shown frame
        long? laterAnimation = position + 1 < frames.Count ? frames[position + 1].At("animationTimeTicks") : null;
        if (
          tied is { } tie
          && frame.At("animationTimeTicks") is { } animation
          && laterAnimation is { } next
          && next > animation
          && animation + tie.Offset >= zero
        )
          Span(AnimationTrack, label, animation + tie.Offset, next + tie.Offset, frame, ("animation time (ms)", Number(Ms(animation, 4))));
        long? expectedStart = position > 0 ? frames[position - 1].At("nextFrameStartTicks") : null;
        if (expectedStart is { } given && frame.At("intendedDisplayTicks") is { } intended)
        {
          // One track: an expected span that begins before the one before it ended begins where that ended
          long start = Math.Max(given, expectedEnd);
          if (intended > start)
          {
            Span(ExpectedTrack, label, start, intended, frame);
            expectedEnd = intended;
          }
        }
        if (frame.At("displayVSyncTicks") is { } blank && blank >= zero && seen.Add(blank))
          events.Add(
            $"{{\"ph\":\"i\",\"pid\":{Process},\"tid\":{VSyncTrack},\"name\":\"vertical blank\",\"ts\":{Microseconds(blank - zero)},\"s\":\"t\"}}"
          );
        var counters = new[] { "swapInterval", "pacerWindowLateFrames" }
          .Where(frame.Values.ContainsKey)
          .Select(key => $"{Json(key)}:{Whole(frame.Values[key])}")
          .ToList();
        if (counters.Count > 0)
          events.Add(
            $"{{\"ph\":\"C\",\"pid\":{Process},\"name\":\"pacer\",\"ts\":{Microseconds(frame.Start - zero)},\"args\":{{{string.Join(',', counters)}}}}}"
          );
      }
      return "{\"displayTimeUnit\":\"ms\",\"traceEvents\":[" + string.Join(',', events) + "]}\n";
    }

    /// <summary>A time in the trace's microseconds, a tick being a tenth of one: written from whole numbers, so nothing is lost.</summary>
    private static string Microseconds(long ticks)
    {
      long whole = FrameLogStages.FloorDivide(ticks, 10);
      return $"{Whole(whole)}.{Whole(ticks - (whole * 10))}";
    }

    /// <summary>
    /// A number written with a fixed count of decimals, as JSON has the number it is: without a '+', without the zeros at its end, and
    /// with one decimal at the least ("0.0100" is 0.01, "+2.0000" is 2.0).
    /// </summary>
    private static string Number(string fixedText)
    {
      string text = fixedText.TrimStart('+').TrimEnd('0');
      return text.EndsWith('.') ? text + "0" : text;
    }

    /// <summary>A text as a JSON string: quotes, backslashes and control characters escaped, and everything outside ASCII as \u escapes.</summary>
    private static string Json(string text)
    {
      var json = new StringBuilder(text.Length + 2).Append('"');
      foreach (char c in text)
      {
        switch (c)
        {
          case '"':
            json.Append("\\\"");
            break;
          case '\\':
            json.Append("\\\\");
            break;
          case '\n':
            json.Append("\\n");
            break;
          case '\r':
            json.Append("\\r");
            break;
          case '\t':
            json.Append("\\t");
            break;
          case '\b':
            json.Append("\\b");
            break;
          case '\f':
            json.Append("\\f");
            break;
          default:
            if (c is < ' ' or > '\u007f')
              json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
              json.Append(c);
            break;
        }
      }
      return json.Append('"').ToString();
    }
  }
}
