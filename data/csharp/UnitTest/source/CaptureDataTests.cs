//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* captures.mbcd: the header and records round trip, newer and foreign files are refused, and a partial last record is ignored.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.IO;
using NUnit.Framework;

namespace MB.FramePacing.Data.UnitTest
{
  [TestFixture]
  public class CaptureDataTests
  {
    private static readonly CaptureDataHeader g_header = new CaptureDataHeader(
      960,
      540,
      60000,
      1001,
      1920,
      1080,
      new DataRect(8, 16, 960, 540),
      new[] { new MarkerLocation(new DataRect(40, 32, 147, 147), 3), new MarkerLocation(new DataRect(40, 400, 99, 99), 3) },
      FramesStored: true,
      Camera: false
    );

    [Test]
    public void Header_RoundTrips()
    {
      var bytes = new byte[CaptureDataHeader.HeaderSize];
      g_header.Write(bytes);
      var read = CaptureDataHeader.Read(bytes);
      Assert.That(read with { Markers = g_header.Markers }, Is.EqualTo(g_header));
      Assert.That(read.Markers, Is.EqualTo(g_header.Markers));
    }

    [Test]
    public void Header_RefusesForeignAndNewerFiles()
    {
      Assert.Throws<InvalidDataException>(() => CaptureDataHeader.Read(new byte[CaptureDataHeader.HeaderSize]));
      var bytes = new byte[CaptureDataHeader.HeaderSize];
      g_header.Write(bytes);
      bytes[4] = CaptureDataHeader.FormatVersion + 1;
      Assert.That(() => CaptureDataHeader.Read(bytes), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("update"));
    }

    [Test]
    public void Header_HoldsAtMostFourMarkers()
    {
      var header = g_header with { Markers = new MarkerLocation[CaptureDataHeader.MaxMarkers + 1] };
      Assert.Throws<ArgumentException>(() => header.Write(new byte[CaptureDataHeader.HeaderSize]));
    }

    [Test]
    public void Records_RoundTrip_AndAPartialLastRecordIsIgnored()
    {
      var main = new byte[CaptureDataRecord.MainCapacity];
      var second = new byte[CaptureDataRecord.SecondCapacity];
      Array.Fill(main, (byte)0xA5);
      Array.Fill(second, (byte)0x5A);
      var records = new[]
      {
        new CaptureDataRecord(0, 100, 200, 0, CaptureDataStatus.Decoded, main, second),
        new CaptureDataRecord(2, 300, CaptureDataRecord.UnknownTicks, 2u, CaptureDataStatus.Undecodable, null, null),
        new CaptureDataRecord(3, 400, 500, 0, CaptureDataStatus.Torn, null, second),
      };
      string path = Path.Combine(Path.GetTempPath(), $"mb-framepacing-data-{Guid.NewGuid():N}.mbcd");
      try
      {
        using (var writer = new CaptureDataWriter(path, g_header with { Markers = Array.Empty<MarkerLocation>() }))
        {
          writer.WriteRecords(records);
          writer.Complete(g_header);
        }
        using (var stream = new FileStream(path, FileMode.Append))
          stream.Write(new byte[CaptureDataRecord.Size / 2]);

        using var reader = new CaptureDataReader(path);
        Assert.That(reader.Header.Markers, Is.EqualTo(g_header.Markers), "Complete rewrote the header");
        Assert.That(reader.RecordCount, Is.EqualTo(records.Length));
        var read = reader.ReadAll();
        for (int i = 0; i < records.Length; ++i)
        {
          Assert.That(read[i] with { MainBytes = null, SecondBytes = null }, Is.EqualTo(records[i] with { MainBytes = null, SecondBytes = null }));
          Assert.That(read[i].MainBytes, Is.EqualTo(records[i].MainBytes));
          Assert.That(read[i].SecondBytes, Is.EqualTo(records[i].SecondBytes));
        }
        Assert.That(read[1].HasDeviceTicks, Is.False);
      }
      finally
      {
        File.Delete(path);
      }
    }

    [Test]
    public void Records_RefuseMarkersTooLongForTheirSlot()
    {
      var record = new CaptureDataRecord(0, 0, 0, 0, CaptureDataStatus.Decoded, new byte[CaptureDataRecord.MainCapacity + 1], null);
      Assert.Throws<ArgumentException>(() => record.Write(new byte[CaptureDataRecord.Size]));
    }
  }
}
