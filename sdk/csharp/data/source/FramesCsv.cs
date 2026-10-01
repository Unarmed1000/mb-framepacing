//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes a run's frames CSV (run-<id>-frames.csv): a header line, then one line per presented frame, comma separated, UTF-8 without
//* a byte order mark. Reading goes by column name. Camera captures add two columns.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MB.FramePacing.Data
{
  public static class FramesCsv
  {
    public const string Header =
      "segment,frameIndex,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,displayDeltaMs,animationDeltaMs,animationErrorMs,driftMs,flags,"
      + "intendedDisplayMs,markerTargetMs,targetMs,markerPreferredMs,preferredMs,pacingErrorMs,predictionErrorMs,latenessMs,lastSeenMs,cpuStartMs,cpuBusyMs,frameTimeMs,cpuWaitMs,olderFrames";

    /// <summary>The columns an EXPERIMENTAL camera capture adds.</summary>
    public const string CameraColumns = ",mainMarkerFirstSeenMs,scanoutDelayMs";

    public static void Write(string path, IEnumerable<FrameRow> rows, bool camera)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      Write(writer, rows, camera);
    }

    public static void Write(TextWriter writer, IEnumerable<FrameRow> rows, bool camera)
    {
      writer.WriteLine(Header + (camera ? CameraColumns : string.Empty));
      foreach (var row in rows)
      {
        writer.WriteLine(
          string.Join(
            ',',
            row.Segment.ToString(CultureInfo.InvariantCulture),
            row.FrameIndex.ToString(CultureInfo.InvariantCulture),
            Milliseconds.Format(row.AnimationTime),
            row.FirstCaptureIndex.ToString(CultureInfo.InvariantCulture),
            Milliseconds.Format(row.FirstSeenTime),
            Milliseconds.Format(row.OnScreen),
            row.Captures.ToString(CultureInfo.InvariantCulture),
            row.SkippedBefore.ToString(CultureInfo.InvariantCulture),
            Optional(row.DisplayDelta),
            Optional(row.AnimationDelta),
            Optional(row.AnimationError),
            Milliseconds.Format(row.Drift),
            string.Join('|', row.Flags),
            Optional(row.IntendedDisplayTime),
            Optional(row.MarkerTargetFrameTime),
            Optional(row.TargetFrameTime),
            Optional(row.MarkerPreferredFrameTime),
            Optional(row.PreferredFrameTime),
            Optional(row.PacingError),
            Optional(row.PredictionError),
            Optional(row.Lateness),
            Optional(row.LastSeenTime),
            Optional(row.CpuStartTime),
            Optional(row.CpuBusy),
            Optional(row.FrameTime),
            Optional(row.CpuWait),
            string.Join(
              '|',
              row.OlderFrames.Select(o => o.FrameIndex.ToString(CultureInfo.InvariantCulture) + "@" + Milliseconds.Format(o.CaptureTime))
            )
          ) + (camera ? "," + Optional(row.MainMarkerFirstSeenTime) + "," + Optional(row.ScanoutDelay) : string.Empty)
        );
      }
    }

    public static IReadOnlyList<FrameRow> Read(string path)
    {
      using var reader = new StreamReader(path);
      return Read(reader, path);
    }

    public static IReadOnlyList<FrameRow> Read(TextReader reader, string name = "frames CSV")
    {
      var column = CsvRow.Columns(reader.ReadLine() ?? throw new InvalidDataException($"'{name}' is empty"));
      int Column(string columnName) => column.TryGetValue(columnName, out int index) ? index : -1;
      int segment = Column("segment");
      int frameIndex = Column("frameIndex");
      int animation = Column("animationMs");
      int firstCapture = Column("firstCaptureIndex");
      int firstSeen = Column("firstSeenMs");
      int onScreen = Column("onScreenMs");
      int captures = Column("captures");
      int skipped = Column("skippedBefore");
      int display = Column("displayDeltaMs");
      int animationDelta = Column("animationDeltaMs");
      int error = Column("animationErrorMs");
      int drift = Column("driftMs");
      int flags = Column("flags");
      int intended = Column("intendedDisplayMs");
      int markerTarget = Column("markerTargetMs");
      int target = Column("targetMs");
      int markerPreferred = Column("markerPreferredMs");
      int preferred = Column("preferredMs");
      int pacing = Column("pacingErrorMs");
      int prediction = Column("predictionErrorMs");
      int lateness = Column("latenessMs");
      int lastSeen = Column("lastSeenMs");
      int cpuStart = Column("cpuStartMs");
      int cpuBusy = Column("cpuBusyMs");
      int frameTime = Column("frameTimeMs");
      int cpuWait = Column("cpuWaitMs");
      int older = Column("olderFrames");
      int mainSeen = Column("mainMarkerFirstSeenMs");
      int scanoutDelay = Column("scanoutDelayMs");

      var rows = new List<FrameRow>();
      string? line;
      while ((line = reader.ReadLine()) != null)
      {
        if (line.Length == 0)
          continue;
        var row = new CsvRow(line.Split(','));
        string flagText = row.Cell(flags);
        rows.Add(
          new FrameRow(
            int.Parse(row.Cell(segment), CultureInfo.InvariantCulture),
            ulong.Parse(row.Cell(frameIndex), CultureInfo.InvariantCulture),
            Milliseconds.ParseMilliseconds(row.Cell(animation)),
            long.Parse(row.Cell(firstCapture), CultureInfo.InvariantCulture),
            new TickCount64(Milliseconds.ParseMilliseconds(row.Cell(firstSeen))),
            Milliseconds.ParseMilliseconds(row.Cell(onScreen)),
            int.Parse(row.Cell(captures), CultureInfo.InvariantCulture),
            ulong.Parse(row.Cell(skipped), CultureInfo.InvariantCulture),
            row.Span(display),
            row.Span(animationDelta),
            row.Span(error),
            Milliseconds.ParseMilliseconds(row.Cell(drift)),
            flagText.Length == 0 ? Array.Empty<string>() : flagText.Split('|'),
            row.Time(intended),
            row.Span32(markerTarget),
            row.Span(target),
            row.Span32(markerPreferred),
            row.Span(preferred),
            row.Span(pacing),
            row.Span(prediction),
            row.Span(lateness),
            row.Time(lastSeen),
            row.Time(cpuStart),
            row.Span32(cpuBusy),
            row.Span(frameTime),
            row.Span(cpuWait),
            OlderFrames(row.Cell(older)),
            row.Time(mainSeen),
            row.Span(scanoutDelay)
          )
        );
      }
      return rows;
    }

    /// <summary>The olderFrames cell: <c>frameIndex@captureMs</c> entries separated by <c>|</c>, empty when none.</summary>
    private static IReadOnlyList<OlderFrame> OlderFrames(string cell) =>
      cell.Length == 0
        ? Array.Empty<OlderFrame>()
        : cell.Split('|')
          .Select(entry =>
          {
            int at = entry.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0)
              throw new InvalidDataException($"Invalid olderFrames entry '{entry}'");
            return new OlderFrame(
              ulong.Parse(entry.AsSpan(0, at), CultureInfo.InvariantCulture),
              new TickCount64(Milliseconds.ParseMilliseconds(entry.Substring(at + 1)))
            );
          })
          .ToArray();

    private static string Optional(TimeSpan? span) => span is { } value ? Milliseconds.Format(value) : string.Empty;

    private static string Optional(TickCount64? time) => time is { } value ? Milliseconds.Format(value) : string.Empty;

    private static string Optional(TimeSpan32? span) => span is { } value ? Milliseconds.Format(value) : string.Empty;
  }
}
