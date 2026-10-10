//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame record against the golden data both languages' tests read (sdk/test-data/trace/frame-records.csv): for each payload, the
//* inputs of its constructor in the order of the wire format, whether it gets a record, and the record's name, start, length and
//* annotations.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class GoldenTests
  {
    private const string Header =
      "kind,runId,frameIndex,animationNs,preferredNs,targetNs,intendedDisplayNs,cpuStartNs,cpuBusyNs,name,startNs,durationNs,annotations";

    private static string GoldenFile()
    {
      var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "test-data", "trace", "frame-records.csv")))
        directory = directory.Parent;
      return Path.Combine(
        directory?.FullName ?? throw new DirectoryNotFoundException("test-data/trace not found above the test directory"),
        "test-data",
        "trace",
        "frame-records.csv"
      );
    }

    private static IEnumerable<string[]> Rows()
    {
      var lines = File.ReadAllLines(GoldenFile());
      Assert.That(lines[0], Is.EqualTo(Header));
      return lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(','));
    }

    private static long Whole(string text) => long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Test]
    public void EveryPayload_GivesItsRecord()
    {
      int rows = 0;
      int records = 0;
      foreach (var cells in Rows())
      {
        ++rows;
        string row = string.Join(',', cells);
        Assert.That(cells.Length, Is.EqualTo(13), row);
        var payload = new Payload(
          Enum.Parse<MarkerKind>(cells[0]),
          uint.Parse(cells[1], CultureInfo.InvariantCulture),
          ulong.Parse(cells[2], CultureInfo.InvariantCulture),
          MarkerFlags.NoFlags,
          NanosecondTimeSpan.FromNanoseconds(Whole(cells[3])),
          NanosecondTimeDuration.FromNanoseconds(Whole(cells[4])),
          NanosecondTimeDuration.FromNanoseconds(Whole(cells[5])),
          NanosecondTickCount.FromNanoseconds(Whole(cells[6])),
          NanosecondTickCount.FromNanoseconds(Whole(cells[7])),
          NanosecondTimeDuration.FromNanoseconds(Whole(cells[8]))
        );

        bool has = FrameRecord.TryCreate(payload, out var record);

        Assert.That(has, Is.EqualTo(cells[9].Length > 0), row);
        if (!has)
        {
          Assert.That(record, Is.EqualTo(default(FrameRecord)), row);
          continue;
        }
        ++records;
        Assert.That(record.Name, Is.EqualTo(cells[9]), row);
        Assert.That(record.StartTime.Nanoseconds, Is.EqualTo(Whole(cells[10])), row);
        Assert.That(record.Duration.Nanoseconds, Is.EqualTo(Whole(cells[11])), row);
        var annotations = Enumerable.Range(0, record.AnnotationCount).Select(index => record.GetAnnotation(index).ToString());
        Assert.That(string.Join(';', annotations), Is.EqualTo(cells[12]), row);
        // A reader gets the marker's ids back from the name
        Assert.That(FrameRecordName.TryParse(cells[9], out uint runId, out ulong frameIndex), Is.True, row);
        Assert.That((runId, frameIndex), Is.EqualTo((payload.RunId, payload.FrameIndex)), row);
      }
      Assert.That(rows, Is.GreaterThanOrEqualTo(9));
      Assert.That(records, Is.GreaterThanOrEqualTo(6));
    }
  }
}
