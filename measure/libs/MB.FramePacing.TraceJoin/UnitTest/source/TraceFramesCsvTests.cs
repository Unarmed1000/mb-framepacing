//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* run-<id>-trace.csv: the file tools/trace_augment.py --frames wrote for the golden capture (measure/test-data/trace/frames.csv) reads
//* and writes back byte for byte, cells that need quotes get them as the script's CSV writer gives them, and content that is not the
//* format's is refused with the file and the line.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class TraceFramesCsvTests
  {
    private static readonly string g_header = string.Join(',', TraceFramesCsv.Columns);

    [Test]
    public void TheScriptsFile_ReadsAndWritesBackByteForByte()
    {
      string stored = File.ReadAllText(TraceTestData.File("frames.csv"));

      var rows = TraceFramesCsv.Parse(stored, "frames.csv");

      Assert.That(rows.Count, Is.EqualTo(388));
      Assert.That(TraceFramesCsv.ToText(rows), Is.EqualTo(stored));
      Assert.That(rows[0].FrameIndex, Is.EqualTo(388));
      Assert.That(rows.Count(row => row.SurfaceFrameToken != null), Is.GreaterThan(380));
      Assert.That(rows.Count(row => row.PresentType == "Late Present"), Is.EqualTo(33));
      Assert.That(rows.All(row => row.DisplayMinusVBlankNs == 0), Is.True);
      Assert.That(rows.Count(row => row.CpuRunnableNs > 0), Is.GreaterThan(30));
    }

    [Test]
    public void AFile_IsWrittenAndReadBack()
    {
      using var temp = new TempFolder();
      string path = temp.File(TraceFramesCsv.FileName("run-7"));
      var rows = new[]
      {
        new TraceFrame(1, -5),
        new TraceFrame(2, 20, 3, 4, 5, -6, 7, 8, "a,\"layer\"", "On-time Present", "None", "line\nbreak", true, false, 9, -10, 11, 12, 13),
      };

      TraceFramesCsv.Write(path, rows);

      Assert.That(Path.GetFileName(path), Is.EqualTo("run-7-trace.csv"));
      Assert.That(
        File.ReadAllText(path),
        Is.EqualTo(
          g_header + "\n1,-5,,,,,,,,,,,,,,,,,\n2,20,3,4,5,-6,7,8,\"a,\"\"layer\"\"\",On-time Present,None,\"line\nbreak\",true,false,9,-10,11,12,13\n"
        )
      );
      Assert.That(File.ReadAllBytes(path)[0], Is.EqualTo((byte)'f'));
      Assert.That(TraceFramesCsv.Read(path), Is.EqualTo(rows));
    }

    [Test]
    public void ColumnsAreFoundByName_AndLinesMayEndInCarriageReturnLineFeed()
    {
      // The columns in another order, with one the reader does not know
      var names = TraceFramesCsv.Columns.Reverse().Append("more").ToArray();
      string line = string.Join(
        ',',
        names.Select(name =>
          name == "frameIndex" ? "4"
          : name == "displayTraceNs" ? "77"
          : string.Empty
        )
      );

      var rows = TraceFramesCsv.Parse(string.Join(',', names) + "\r\n" + line + "\r\n", "file");

      Assert.That(rows, Is.EqualTo(new[] { new TraceFrame(4, 77) }));
      Assert.That(TraceFramesCsv.Parse(g_header + "\n", "file"), Is.Empty);
    }

    [TestCase("", "no header line")]
    [TestCase("frameIndex,displayTraceNs\n1,2\n", "no column 'cpuStartTraceNs'")]
    [TestCase("{header}\n1,2\n", "file:2: 2 cells")]
    [TestCase("{header}\n-1,2,,,,,,,,,,,,,,,,,\n", "file:2: 'frameIndex'")]
    [TestCase("{header}\n1,,,,,,,,,,,,,,,,,,\n", "'displayTraceNs' is empty")]
    [TestCase("{header}\n1,2.5,,,,,,,,,,,,,,,,,\n", "'displayTraceNs' is not a whole number")]
    [TestCase("{header}\n1,+2,,,,,,,,,,,,,,,,,\n", "'displayTraceNs' is not a whole number")]
    [TestCase("{header}\n1,2,,,,,,,,,,,,,,,,,\n1,2,,,,,,,,,,,True,,,,,,\n", "file:3: 'onTimeFinish'")]
    [TestCase("{header}\n1,2,,,,,,,\"open,,,,,,,,,,\n", "does not end")]
    public void ContentThatIsNotTheFormats_IsRefusedWithTheFileAndTheLine(string text, string expected)
    {
      var error = Assert.Throws<InvalidDataException>(() => TraceFramesCsv.Parse(text.Replace("{header}", g_header), "file"));

      Assert.That(error!.Message, Does.Contain(expected));
    }
  }
}
