//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a trace with a run's tracks: a recorded trace with the capture's measurements added (the augmented trace), or the capture alone
//* as a trace of its own. The tracks first, on the trace's own clock by its snapshots, then what the recorded trace holds
//* (TraceTrackWriter says why). And reads the result back with the trace processor: every track has to have what was written.
//*
//* The file is written next to its place and moved over it when it is whole: one that could not be finished is not left behind.
//*
//* An augmented trace holds everything the recorded one did: it names the device and its programs.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Writes a run's tracks into a trace, and checks the result.</summary>
  public static class TraceAugmenter
  {
    /// <summary>What a run's augmented trace's file name ends in, after the run's prefix (run-&lt;id&gt;).</summary>
    public const string FileSuffix = "-augmented.pftrace";

    /// <summary>The file name of the run whose files begin with <paramref name="runFilePrefix"/>.</summary>
    public static string FileName(string runFilePrefix) => runFilePrefix + FileSuffix;

    /// <summary>
    /// The recorded trace of <paramref name="join"/> with its run's tracks added, at <paramref name="outputPath"/>. Returns what was
    /// written per track ("slices &lt;name&gt;": slices and moments, "values &lt;name&gt;": counter values).
    /// </summary>
    public static IReadOnlyDictionary<string, long> Write(string outputPath, TraceRunJoin join)
    {
      var attributes = new List<KeyValuePair<string, string>>
      {
        new("run", join.Run.Name),
        new("frames", Text(join.Run.Frames.Count)),
        new("placed by", TraceAnchorText.Of(join.Alignment.Anchor)),
        new("capture clock minus application clock (ns)", Text(join.Alignment.CaptureMinusAppNs)),
        new("application clock", TraceClockText.NameOfId(FrameRecordText.ClockId)!),
        new("application clock offset (ns)", Text(join.Alignment.AppClockOffsetNs)),
        new("vertical blanks", join.VBlankNote),
      };
      return Write(outputPath, join.Options.Path, join.Run, join.Alignment, join.Data.Snapshots, attributes);
    }

    /// <summary>The capture's tracks as a trace of their own, on the capture's clock.</summary>
    public static IReadOnlyDictionary<string, long> WriteCaptureAlone(string outputPath, TraceRun run)
    {
      var (anchor, offset) = TraceAlignmentUtil.CaptureMinusApp(run.Frames);
      var alignment = new TraceAlignment(anchor, offset, AppClockOffsetNs: offset);
      var attributes = new List<KeyValuePair<string, string>>
      {
        new("run", run.Name),
        new("frames", Text(run.Frames.Count)),
        new("clock", "the capture's, from its start"),
        new("the markers' times placed by", TraceAnchorText.Of(anchor)),
        new("capture clock minus application clock (ns)", Text(offset)),
      };
      return Write(outputPath, null, run, alignment, new List<ClockSnapshot>(), attributes);
    }

    /// <summary>
    /// Read the trace that was written back, and say what is not as it was written: a track that has fewer events than were written, and
    /// errors the trace processor counts that it did not count in the recorded trace. Empty when all is as written.
    /// </summary>
    public static IReadOnlyList<string> Check(
      string outputPath,
      string processorPath,
      string runName,
      IReadOnlyDictionary<string, long> written,
      IReadOnlyDictionary<string, long> errorsBefore,
      CancellationToken cancellationToken = default
    )
    {
      using var processor = TraceProcessor.Start(processorPath, outputPath, cancellationToken: cancellationToken);
      return Check(processor, runName, written, errorsBefore);
    }

    internal static IReadOnlyList<string> Check(
      ITraceQueries result,
      string runName,
      IReadOnlyDictionary<string, long> written,
      IReadOnlyDictionary<string, long> errorsBefore
    )
    {
      var (slices, values) = TraceReader.Added(result, TraceTrackWriter.RootName(runName));
      var errors = TraceReader.Errors(result);
      var problems = new List<string>();
      foreach (var (key, count) in written)
      {
        int space = key.IndexOf(' ', System.StringComparison.Ordinal);
        string kind = key[..space];
        string name = key[(space + 1)..];
        long found = (kind == "slices" ? slices : values).GetValueOrDefault(name);
        if (found != count)
          problems.Add($"the track '{name}' has {Text(found)} of the {Text(count)} {kind} that were written");
      }
      foreach (var (name, count) in errors)
      {
        long before = errorsBefore.GetValueOrDefault(name);
        if (count > before)
          problems.Add($"the trace processor counts {Text(count)} '{name}' where the recorded trace had {Text(before)}");
      }
      return problems;
    }

    private static Dictionary<string, long> Write(
      string outputPath,
      string? sourcePath,
      TraceRun run,
      TraceAlignment alignment,
      IReadOnlyList<ClockSnapshot> snapshots,
      IReadOnlyList<KeyValuePair<string, string>> attributes
    )
    {
      string partial = outputPath + ".partial";
      TraceTrackWriter writer;
      try
      {
        using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
          writer = new TraceTrackWriter(file, run.Key, run.Name, time => TraceAlignmentUtil.ToTraceNs(time, snapshots));
          writer.WriteAttributes(attributes);
          foreach (var track in TraceTrackText.All)
            writer.WriteTrack(track, TraceEvents.Of(track, run.Frames, run.Captures, alignment, run.PeriodNs));
          if (sourcePath != null)
            TraceTrackWriter.CopySource(sourcePath, file);
        }
        File.Move(partial, outputPath, overwrite: true);
      }
      finally
      {
        if (File.Exists(partial))
          File.Delete(partial);
      }

      var written = new Dictionary<string, long>(System.StringComparer.Ordinal);
      foreach (var track in TraceTrackText.All)
      {
        string name = TraceTrackText.NameOf(track);
        if (writer.Slices.GetValueOrDefault(track) > 0)
          written[$"slices {name}"] = writer.Slices[track];
        if (writer.Values.GetValueOrDefault(track) > 0)
          written[$"values {name}"] = writer.Values[track];
      }
      return written;
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>How many events were written in all.</summary>
    public static long Total(IReadOnlyDictionary<string, long> written) => written.Values.Sum();
  }
}
