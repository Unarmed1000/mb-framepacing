//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The CSV files are read and written a line at a time without a string per line or per cell (CsvLineReader, CsvLineWriter, CsvRow): the
//* lines are TextReader.ReadLine's whatever the buffer's size, a line with more or fewer cells than the header reads by column as before,
//* and a file of many lines costs a row object per line, not a string per cell.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MB.FramePacing.Data.UnitTest
{
  [TestFixture]
  public class CsvLineTests
  {
    private const string FrameHeader =
      "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,driftTicks,flags,olderFrames";

    [TestCase("")]
    [TestCase("a")]
    [TestCase("a\n")]
    [TestCase("a\r\n")]
    [TestCase("a\r")]
    [TestCase("\n")]
    [TestCase("\r\n\r\n")]
    [TestCase("a\n\nb")]
    [TestCase("a\r\rb\n")]
    [TestCase("a\n\rb")]
    [TestCase("one,two\r\nthree,four\r\n\r\nlast")]
    [TestCase("a line that is longer than every buffer tried here\nshort\r\n")]
    public void LineReader_GivesReadLinesLines_WhateverTheBuffer(string text)
    {
      var expected = new List<string>();
      using (var reference = new StringReader(text))
      {
        while (reference.ReadLine() is { } line)
          expected.Add(line);
      }

      // Every split of the text between two fills, a carriage return and its line feed apart too
      foreach (int bufferLength in new[] { 1, 2, 3, 4, 5, 7, 16, 1 << 15 })
      {
        var lines = new List<string>();
        using (var reader = new CsvLineReader(new StringReader(text), bufferLength))
        {
          while (reader.TryReadLine(out var line))
            lines.Add(line.ToString());
          Assert.That(reader.TryReadLine(out _), Is.False, "the end stays the end");
        }
        Assert.That(lines, Is.EqualTo(expected), $"a buffer of {bufferLength}");
      }
    }

    [Test]
    public void LineReader_RandomTexts_AreReadLinesLines()
    {
      var random = new Random(11);
      const string alphabet = "ab,\r\n";
      for (int round = 0; round < 500; ++round)
      {
        var text = new StringBuilder();
        for (int i = random.Next(0, 40); i > 0; --i)
          text.Append(alphabet[random.Next(alphabet.Length)]);
        var expected = new List<string>();
        using (var reference = new StringReader(text.ToString()))
        {
          while (reference.ReadLine() is { } line)
            expected.Add(line);
        }
        var lines = new List<string>();
        using (var reader = new CsvLineReader(new StringReader(text.ToString()), random.Next(1, 6)))
        {
          while (reader.TryReadLine(out var line))
            lines.Add(line.ToString());
        }
        Assert.That(
          lines,
          Is.EqualTo(expected),
          text.ToString().Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
        );
      }
    }

    [Test]
    public void LineWriter_WritesCellsAsTheirText()
    {
      var text = new StringWriter { NewLine = "\n" };
      var line = new CsvLineWriter();
      line.Add(long.MinValue);
      line.Add(ulong.MaxValue);
      line.Add((long?)null);
      line.Add((ulong?)4294967295u);
      line.Add("Late");
      line.Add((string?)null);
      line.AddHex(new byte[] { 0x4D, 0x46, 0x00, 0xFF });
      line.AddHex(null);
      line.Cell();
      line.Append(41UL);
      line.Append('@');
      line.Append(-5L);
      line.End(text);
      // The next line starts empty, and a long one grows the buffer
      line.Add(new string('x', 2000));
      line.Add(7L);
      line.End(text);

      Assert.That(
        text.ToString(),
        Is.EqualTo("-9223372036854775808,18446744073709551615,,4294967295,Late,,4D4600FF,,41@-5\n" + new string('x', 2000) + ",7\n")
      );
    }

    [Test]
    public void FramesCsv_ALineWithMoreOrFewerCellsThanTheHeader_ReadsByColumn()
    {
      // The cells beyond the header's columns belong to no column; a short line's missing cells are empty
      string csv = FrameHeader + "\n" + "0,7,100,3,500,333,2,1,-5,Late,41@5|40@-5,extra,more,and,more\n" + "0,8,200,4,600,333,2,0,0\n";
      var rows = FramesCsv.Read(new StringReader(csv));

      Assert.That(rows.Select(r => r.FrameIndex), Is.EqualTo(new[] { 7UL, 8UL }));
      Assert.That(rows[0].Flags, Is.EqualTo(new[] { "Late" }));
      Assert.That(rows[0].OlderFrames, Is.EqualTo(new[] { new OlderFrame(41, new TickCount64(5)), new OlderFrame(40, new TickCount64(-5)) }));
      Assert.That(rows[1].Flags, Is.Empty);
      Assert.That(rows[1].OlderFrames, Is.Empty);
    }

    [Test]
    public void FramesCsv_AFileWithManyColumns_ReadsItsOwnAmongThem()
    {
      // More columns than the reader keeps room for on the stack, the known ones at the end
      string padding = string.Join(',', Enumerable.Range(0, 100).Select(i => $"other{i}"));
      string cells = string.Join(',', Enumerable.Repeat("x", 100));
      string csv = padding + "," + FrameHeader + "\n" + cells + ",0,7,100,3,500,333,2,1,-5,Late|StaticAfter,\n";
      var rows = FramesCsv.Read(new StringReader(csv));

      Assert.That(rows, Has.Count.EqualTo(1));
      Assert.That((rows[0].FrameIndex, rows[0].Drift), Is.EqualTo((7UL, new TimeSpan(-5))));
      Assert.That(rows[0].Flags, Is.EqualTo(new[] { "Late", "StaticAfter" }));
    }

    [Test]
    public void ReadingAndWriting_CostARowPerLine_NotAStringPerCell()
    {
      const int count = 2000;
      var frames = Enumerable
        .Range(0, count)
        .Select(i => new FrameRow(
          0,
          (ulong)i,
          new TimeSpan(i * 166_667L),
          i,
          new TickCount64(i * 166_667L),
          new TimeSpan(166_667),
          1,
          0,
          new TimeSpan(166_667),
          new TimeSpan(166_667),
          TimeSpan.Zero,
          TimeSpan.Zero,
          i % 97 == 0 ? new[] { "Late" } : Array.Empty<string>(),
          new TickCount64(i * 166_667L),
          new TimeSpan32(166_667),
          new TimeSpan(166_667),
          new TimeSpan32(166_667),
          new TimeSpan(166_667),
          TimeSpan.Zero,
          TimeSpan.Zero,
          TimeSpan.Zero,
          new TickCount64(i * 166_667L),
          new TickCount64(i * 166_667L),
          new TimeSpan32(50_000),
          new TimeSpan(166_667),
          new TimeSpan(100_000),
          Array.Empty<OlderFrame>()
        ))
        .ToList();
      var text = new StringWriter { NewLine = "\n" };
      FramesCsv.Write(text, frames, camera: false);
      string csv = text.ToString();

      // Warm up, then measure: writing into nothing, reading from the text
      FramesCsv.Write(TextWriter.Null, frames, camera: false);
      FramesCsv.Read(new StringReader(csv));
      long before = GC.GetAllocatedBytesForCurrentThread();
      FramesCsv.Write(TextWriter.Null, frames, camera: false);
      long written = GC.GetAllocatedBytesForCurrentThread() - before;
      before = GC.GetAllocatedBytesForCurrentThread();
      var rows = FramesCsv.Read(new StringReader(csv));
      long read = GC.GetAllocatedBytesForCurrentThread() - before;

      Assert.That(rows, Has.Count.EqualTo(count));
      // 27 cells a line: a string each would be far over a kilobyte a line. A row object is about 360 bytes
      Assert.That(written / count, Is.LessThan(16), "writing allocates per file, not per line");
      Assert.That(read / count, Is.LessThan(600), "reading allocates the row, and little more");
    }
  }
}
