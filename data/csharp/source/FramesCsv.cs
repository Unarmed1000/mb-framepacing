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
using System.Text;

namespace MB.FramePacing.Data
{
  public static class FramesCsv
  {
    public const string Header =
      "segment,frameIndex,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,displayDeltaMs,animationDeltaMs,animationErrorMs,driftMs,flags,"
      + "intendedDisplayMs,markerTargetMs,targetMs,pacingErrorMs,predictionErrorMs,latenessMs,lastSeenMs,cpuStartMs,cpuBusyMs,frameTimeMs,cpuWaitMs";

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
            Milliseconds.Format(row.AnimationTicks),
            row.FirstCaptureIndex.ToString(CultureInfo.InvariantCulture),
            Milliseconds.Format(row.FirstSeenTicks),
            Milliseconds.Format(row.OnScreenTicks),
            row.Captures.ToString(CultureInfo.InvariantCulture),
            row.SkippedBefore.ToString(CultureInfo.InvariantCulture),
            Optional(row.DisplayDeltaTicks),
            Optional(row.AnimationDeltaTicks),
            Optional(row.AnimationErrorTicks),
            Milliseconds.Format(row.DriftTicks),
            string.Join('|', row.Flags),
            Optional(row.IntendedDisplayTicks),
            Optional(row.MarkerTargetTicks),
            Optional(row.TargetTicks),
            Optional(row.PacingErrorTicks),
            Optional(row.PredictionErrorTicks),
            Optional(row.LatenessTicks),
            Optional(row.LastSeenTicks),
            Optional(row.CpuStartTicks),
            Optional(row.CpuBusyTicks),
            Optional(row.FrameTimeTicks),
            Optional(row.CpuWaitTicks)
          ) + (camera ? "," + Optional(row.MainMarkerFirstSeenTicks) + "," + Optional(row.ScanoutDelayTicks) : string.Empty)
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
      int pacing = Column("pacingErrorMs");
      int prediction = Column("predictionErrorMs");
      int lateness = Column("latenessMs");
      int lastSeen = Column("lastSeenMs");
      int cpuStart = Column("cpuStartMs");
      int cpuBusy = Column("cpuBusyMs");
      int frameTime = Column("frameTimeMs");
      int cpuWait = Column("cpuWaitMs");
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
            Milliseconds.ParseTicks(row.Cell(animation)),
            long.Parse(row.Cell(firstCapture), CultureInfo.InvariantCulture),
            Milliseconds.ParseTicks(row.Cell(firstSeen)),
            Milliseconds.ParseTicks(row.Cell(onScreen)),
            int.Parse(row.Cell(captures), CultureInfo.InvariantCulture),
            ulong.Parse(row.Cell(skipped), CultureInfo.InvariantCulture),
            row.Ticks(display),
            row.Ticks(animationDelta),
            row.Ticks(error),
            Milliseconds.ParseTicks(row.Cell(drift)),
            flagText.Length == 0 ? Array.Empty<string>() : flagText.Split('|'),
            row.Ticks(intended),
            row.Ticks(markerTarget),
            row.Ticks(target),
            row.Ticks(pacing),
            row.Ticks(prediction),
            row.Ticks(lateness),
            row.Ticks(lastSeen),
            row.Ticks(cpuStart),
            row.Ticks(cpuBusy),
            row.Ticks(frameTime),
            row.Ticks(cpuWait),
            row.Ticks(mainSeen),
            row.Ticks(scanoutDelay)
          )
        );
      }
      return rows;
    }

    private static string Optional(long? ticks) => ticks is { } value ? Milliseconds.Format(value) : string.Empty;
  }
}
