//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes a run's frames CSV (run-<id>-frames.csv): a header line, then one line per presented frame, comma separated, UTF-8 without
//* a byte order mark. Reading goes by column name. Camera captures add two columns. Every time is written as its 100 ns ticks, a whole
//* number: a marker's value is in the file exactly as the marker carried it.
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
      "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,displayDeltaTicks,animationDeltaTicks,animationErrorTicks,driftTicks,flags,"
      + "intendedDisplayTicks,markerTargetTicks,targetTicks,markerPreferredTicks,preferredTicks,pacingErrorTicks,predictionErrorTicks,latenessTicks,lastSeenTicks,cpuStartTicks,cpuBusyTicks,frameTimeTicks,cpuWaitTicks,olderFrames";

    /// <summary>The columns an EXPERIMENTAL camera capture adds.</summary>
    public const string CameraColumns = ",mainMarkerFirstSeenTicks,scanoutDelayTicks";

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
            Ticks(row.AnimationTime),
            row.FirstCaptureIndex.ToString(CultureInfo.InvariantCulture),
            Ticks(row.FirstSeenTime),
            Ticks(row.OnScreen),
            row.Captures.ToString(CultureInfo.InvariantCulture),
            row.SkippedBefore.ToString(CultureInfo.InvariantCulture),
            Ticks(row.DisplayDelta),
            Ticks(row.AnimationDelta),
            Ticks(row.AnimationError),
            Ticks(row.Drift),
            string.Join('|', row.Flags),
            Ticks(row.IntendedDisplayTime),
            Ticks(row.MarkerTargetFrameTime),
            Ticks(row.TargetFrameTime),
            Ticks(row.MarkerPreferredFrameTime),
            Ticks(row.PreferredFrameTime),
            Ticks(row.PacingError),
            Ticks(row.PredictionError),
            Ticks(row.Lateness),
            Ticks(row.LastSeenTime),
            Ticks(row.CpuStartTime),
            Ticks(row.CpuBusy),
            Ticks(row.FrameTime),
            Ticks(row.CpuWait),
            string.Join('|', row.OlderFrames.Select(o => o.FrameIndex.ToString(CultureInfo.InvariantCulture) + "@" + Ticks(o.CaptureTime)))
          ) + (camera ? "," + Ticks(row.MainMarkerFirstSeenTime) + "," + Ticks(row.ScanoutDelay) : string.Empty)
        );
      }
    }

    /// <summary>Read a frames CSV. Throws <see cref="InvalidDataException"/> for content that is not one.</summary>
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
      int animation = Column("animationTicks");
      int firstCapture = Column("firstCaptureIndex");
      int firstSeen = Column("firstSeenTicks");
      int onScreen = Column("onScreenTicks");
      int captures = Column("captures");
      int skipped = Column("skippedBefore");
      int display = Column("displayDeltaTicks");
      int animationDelta = Column("animationDeltaTicks");
      int error = Column("animationErrorTicks");
      int drift = Column("driftTicks");
      int flags = Column("flags");
      int intended = Column("intendedDisplayTicks");
      int markerTarget = Column("markerTargetTicks");
      int target = Column("targetTicks");
      int markerPreferred = Column("markerPreferredTicks");
      int preferred = Column("preferredTicks");
      int pacing = Column("pacingErrorTicks");
      int prediction = Column("predictionErrorTicks");
      int lateness = Column("latenessTicks");
      int lastSeen = Column("lastSeenTicks");
      int cpuStart = Column("cpuStartTicks");
      int cpuBusy = Column("cpuBusyTicks");
      int frameTime = Column("frameTimeTicks");
      int cpuWait = Column("cpuWaitTicks");
      int older = Column("olderFrames");
      int mainSeen = Column("mainMarkerFirstSeenTicks");
      int scanoutDelay = Column("scanoutDelayTicks");

      var rows = new List<FrameRow>();
      int lineNumber = 1;
      string? line;
      while ((line = reader.ReadLine()) != null)
      {
        ++lineNumber;
        if (line.Length == 0)
          continue;
        var row = new CsvRow(line.Split(','));
        try
        {
          string flagText = row.Cell(flags);
          rows.Add(
            new FrameRow(
              row.RequiredInt(segment),
              row.RequiredULong(frameIndex),
              row.RequiredSpan(animation),
              row.RequiredLong(firstCapture),
              row.RequiredTime(firstSeen),
              row.RequiredSpan(onScreen),
              row.RequiredInt(captures),
              row.RequiredULong(skipped),
              row.Span(display),
              row.Span(animationDelta),
              row.Span(error),
              row.RequiredSpan(drift),
              Flags(flagText),
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
        catch (InvalidDataException exception)
        {
          throw new InvalidDataException($"'{name}' line {lineNumber}: {exception.Message}", exception);
        }
      }
      return rows;
    }

    /// <summary>The flags cell: names separated by <c>|</c>, empty when none.</summary>
    private static string[] Flags(string cell)
    {
      if (cell.Length == 0)
        return Array.Empty<string>();
      string[] flags = cell.Split('|');
      if (Array.IndexOf(flags, string.Empty) >= 0)
        throw new InvalidDataException($"An empty entry in flags '{cell}'");
      return flags;
    }

    /// <summary>The olderFrames cell: <c>frameIndex@captureTicks</c> entries separated by <c>|</c>, empty when none.</summary>
    private static IReadOnlyList<OlderFrame> OlderFrames(string cell) =>
      cell.Length == 0
        ? Array.Empty<OlderFrame>()
        : cell.Split('|')
          .Select(entry =>
          {
            int at = entry.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0)
              throw new InvalidDataException($"Invalid olderFrames entry '{entry}'");
            return new OlderFrame(CsvRow.ParseULong(entry.AsSpan(0, at), ulong.MaxValue), new TickCount64(CsvRow.ParseLong(entry.AsSpan(at + 1))));
          })
          .ToArray();

    private static string Ticks(TimeSpan span) => span.Ticks.ToString(CultureInfo.InvariantCulture);

    private static string Ticks(TickCount64 time) => time.Ticks.ToString(CultureInfo.InvariantCulture);

    private static string Ticks(TimeSpan? span) => span is { } value ? Ticks(value) : string.Empty;

    private static string Ticks(TickCount64? time) => time is { } value ? Ticks(value) : string.Empty;

    private static string Ticks(TimeSpan32? span) => span is { } value ? value.Ticks.ToString(CultureInfo.InvariantCulture) : string.Empty;
  }
}
