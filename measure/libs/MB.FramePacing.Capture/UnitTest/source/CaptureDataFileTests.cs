//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The capture data file (captures.mbcd): header and records round trip, the largest markers fit, the header is completed at the end, and
//* foreign or newer files are refused.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class CaptureDataFileTests
  {
    private static readonly CaptureFileHeader g_frames = new CaptureFileHeader(
      960,
      540,
      new FrameRate(60000, 1001),
      1920,
      1080,
      new PixelRect(10, 20, 300, 400)
    );

    private static readonly MarkerLock[] g_locks = { MarkerLock.At(40, 32, 3), MarkerLock.At(40, 400, 3, MarkerKind.Sync) };

    [Test]
    public void Header_RoundTrips()
    {
      var header = g_frames.ToDataHeader(g_locks, framesStored: true, camera: false);
      var bytes = new byte[CaptureDataHeader.HeaderSize];
      header.Write(bytes);
      var read = CaptureDataHeader.Read(bytes);
      Assert.That(read.ToFileHeader(), Is.EqualTo(g_frames));
      Assert.That(read.ToLocks(), Is.EqualTo(g_locks));
      Assert.That((read.FramesStored, read.Camera), Is.EqualTo((true, false)));
    }

    [Test]
    public void Header_RefusesForeignAndNewerFiles()
    {
      Assert.Throws<InvalidDataException>(() => CaptureDataHeader.Read(new byte[CaptureDataHeader.HeaderSize]));
      var bytes = new byte[CaptureDataHeader.HeaderSize];
      g_frames.ToDataHeader(g_locks, false, false).Write(bytes);
      bytes[4] = CaptureDataHeader.FormatVersion + 1;
      Assert.That(() => CaptureDataHeader.Read(bytes), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("update the tools"));
    }

    /// <summary>The largest marker (a start marker) and a sync marker fit one record; a record without markers keeps none.</summary>
    [Test]
    public void Records_RoundTrip_WithTheLargestMarkers()
    {
      var start = new MarkerPayload(1, 2, 3, MarkerKind.SequenceStart).Encode(
        new StartMetadata(123, new MB.FrameMarker.SequenceId(ulong.MaxValue, ulong.MaxValue))
      );
      Assert.That(start, Has.Length.EqualTo(MarkerPayload.MaxEncodedByteCount));
      var sync = new byte[12];
      sync.AsSpan().Fill(7);
      var records = new[]
      {
        new CaptureDataRecord(0, 100, 200, 0, CaptureDataStatus.Decoded, start, sync),
        new CaptureDataRecord(1, 300, CaptureRecordHeader.UnknownTicks, 2, CaptureDataStatus.Undecodable, null, null),
        new CaptureDataRecord(5, 500, 600, 0, CaptureDataStatus.Torn, null, sync),
      };

      using var temp = new TempDirectory();
      var path = temp.File("captures.mbcd");
      using (var writer = new CaptureDataWriter(path, g_frames.ToDataHeader(Array.Empty<MarkerLock>(), false, false)))
      {
        var buffer = new byte[records.Length * CaptureDataRecord.Size];
        for (int i = 0; i < records.Length; ++i)
          records[i].Write(buffer.AsSpan(i * CaptureDataRecord.Size));
        writer.WriteRecords(buffer);
        // The locks are only known at the end of a capture
        writer.Complete(writer.Header with { Markers = g_locks.ToLocations() });
        Assert.That(writer.RecordsWritten, Is.EqualTo(3));
      }

      using var reader = new CaptureDataReader(path);
      Assert.That(reader.Header.ToLocks(), Is.EqualTo(g_locks));
      Assert.That(reader.RecordCount, Is.EqualTo(3));
      var read = reader.ReadAll();
      for (int i = 0; i < records.Length; ++i)
      {
        var (expected, actual) = (records[i], read[i]);
        Assert.That(
          (actual.CaptureIndex, actual.HostTicks, actual.DeviceTicks, actual.SourceDrops, actual.Status),
          Is.EqualTo((expected.CaptureIndex, expected.HostTicks, expected.DeviceTicks, expected.SourceDrops, expected.Status))
        );
        Assert.That(actual.MainBytes, Is.EqualTo(expected.MainBytes));
        Assert.That(actual.SecondBytes, Is.EqualTo(expected.SecondBytes));
        Assert.That(reader.ReadRecord(i).MainBytes, Is.EqualTo(expected.MainBytes));
      }
      Assert.That(read[1].HasDeviceTicks, Is.False);
      Assert.That(MarkerPayload.TryDecode(read[0].MainBytes, out var payload, out var metadata), Is.True);
      Assert.That(
        (payload.Kind, metadata!.SequenceId),
        Is.EqualTo((MarkerKind.SequenceStart, new MB.FrameMarker.SequenceId(ulong.MaxValue, ulong.MaxValue)))
      );
    }

    [Test]
    public void Record_RefusesMarkersThatDoNotFit()
    {
      var record = new CaptureDataRecord(0, 0, 0, 0, CaptureDataStatus.Decoded, new byte[CaptureDataRecord.MainCapacity + 1], null);
      Assert.Throws<ArgumentException>(() => record.Write(new byte[CaptureDataRecord.Size]));
    }

    /// <summary>A capture killed mid-write ends with a partial record: it is ignored.</summary>
    [Test]
    public void Reader_IgnoresAPartialLastRecord()
    {
      using var temp = new TempDirectory();
      var path = temp.File("captures.mbcd");
      using (var writer = new CaptureDataWriter(path, g_frames.ToDataHeader(g_locks, false, false)))
      {
        var buffer = new byte[2 * CaptureDataRecord.Size];
        for (int i = 0; i < 2; ++i)
          new CaptureDataRecord(i, i, i, 0, CaptureDataStatus.Undecodable, null, null).Write(buffer.AsSpan(i * CaptureDataRecord.Size));
        writer.WriteRecords(buffer);
      }
      using (var stream = new FileStream(path, FileMode.Append))
        stream.Write(new byte[CaptureDataRecord.Size / 2]);

      using var reader = new CaptureDataReader(path);
      Assert.That(reader.RecordCount, Is.EqualTo(2));
      Assert.That(reader.ReadAll().Select(r => r.CaptureIndex), Is.EqualTo(new long[] { 0, 1 }));
    }
  }
}
