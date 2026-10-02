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
using MB.FramePacing.Marker;
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
      new Rectangle(8, 16, 960, 540),
      new[] { new MarkerLocation(new Rectangle(40, 32, 147, 147), 3), new MarkerLocation(new Rectangle(40, 400, 99, 99), 3) },
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
        new CaptureDataRecord(0, new TickCount64(100), new TickCount64(200), 0, CaptureDataStatus.Decoded, main, second),
        new CaptureDataRecord(2, new TickCount64(300), null, 2u, CaptureDataStatus.Undecodable, null, null),
        new CaptureDataRecord(3, new TickCount64(400), new TickCount64(500), 0, CaptureDataStatus.Torn, null, second),
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
        Assert.That(read[1].DeviceTime, Is.Null, "a device that gave no timestamp");
      }
      finally
      {
        File.Delete(path);
      }
    }

    [Test]
    public void Header_RefusesWhatIsNotTheFormat()
    {
      byte[] Written()
      {
        var bytes = new byte[CaptureDataHeader.HeaderSize];
        g_header.Write(bytes);
        return bytes;
      }
      byte[] With(int offset, byte value)
      {
        byte[] bytes = Written();
        bytes[offset] = value;
        return bytes;
      }
      Assert.That(CaptureDataHeader.Read(Written()).Width, Is.EqualTo(960), "the bytes these cases change one of");
      Assert.That(
        () => CaptureDataHeader.Read(Written().AsSpan(0, CaptureDataHeader.HeaderSize - 1)),
        Throws.InstanceOf<InvalidDataException>(),
        "a byte short"
      );
      Assert.That(() => CaptureDataHeader.Read(ReadOnlySpan<byte>.Empty), Throws.InstanceOf<InvalidDataException>(), "nothing");
      Assert.That(() => CaptureDataHeader.Read(With(0, (byte)'X')), Throws.InstanceOf<InvalidDataException>(), "another magic");
      Assert.That(
        () => CaptureDataHeader.Read(With(4, 0)),
        Throws.InstanceOf<InvalidDataException>().With.Message.Contains("version 0"),
        "version 0"
      );
      Assert.That(() => CaptureDataHeader.Read(With(6, 255)), Throws.InstanceOf<InvalidDataException>(), "another header size");
      Assert.That(() => CaptureDataHeader.Read(With(8, 191)), Throws.InstanceOf<InvalidDataException>(), "another record size");
      Assert.That(
        () => CaptureDataHeader.Read(With(64, CaptureDataHeader.MaxMarkers + 1)),
        Throws.InstanceOf<InvalidDataException>(),
        "five markers"
      );
      Assert.That(CaptureDataHeader.Read(With(64, 0)).Markers, Is.Empty);
      Assert.That(() => g_header.Write(new byte[CaptureDataHeader.HeaderSize - 1]), Throws.ArgumentException, "a buffer too small to write into");
    }

    [Test]
    public void Header_FlagsAndTheSourceSizeRoundTrip()
    {
      var bytes = new byte[CaptureDataHeader.HeaderSize + 8];
      Array.Fill(bytes, (byte)0xEE);
      var camera = g_header with { FramesStored = false, Camera = true, SourceWidth = 0, SourceHeight = 0, Markers = Array.Empty<MarkerLocation>() };
      camera.Write(bytes);
      Assert.That(bytes.AsSpan(CaptureDataHeader.HeaderSize).ToArray(), Is.All.EqualTo(0xEE), "nothing is written past the header");
      var read = CaptureDataHeader.Read(bytes);
      Assert.That((read.FramesStored, read.Camera, read.SourceWidth, read.SourceHeight, read.Markers.Count), Is.EqualTo((false, true, 0, 0, 0)));
      Assert.That((read.FrameRateNumerator, read.FrameRateDenominator, read.Region), Is.EqualTo((60000u, 1001u, new Rectangle(8, 16, 960, 540))));
    }

    [Test]
    public void Record_RefusesWhatIsNotARecord()
    {
      byte[] With(int offset, byte value)
      {
        var bytes = new byte[CaptureDataRecord.Size];
        new CaptureDataRecord(7, new TickCount64(100), null, 0, CaptureDataStatus.Decoded, null, null).Write(bytes);
        bytes[offset] = value;
        return bytes;
      }
      Assert.That(
        CaptureDataRecord.Read(With(CaptureDataRecord.StatusOffset, (byte)CaptureDataStatus.Torn)).CaptureStatus,
        Is.EqualTo(CaptureDataStatus.Torn)
      );
      Assert.That(
        () => CaptureDataRecord.Read(With(CaptureDataRecord.StatusOffset, 3)),
        Throws.InstanceOf<InvalidDataException>(),
        "an unknown status"
      );
      Assert.That(
        () => CaptureDataRecord.Read(With(CaptureDataRecord.MainLengthOffset, CaptureDataRecord.MainCapacity + 1)),
        Throws.InstanceOf<InvalidDataException>(),
        "a main marker longer than its slot"
      );
      Assert.That(
        () => CaptureDataRecord.Read(With(CaptureDataRecord.SecondLengthOffset, CaptureDataRecord.SecondCapacity + 1)),
        Throws.InstanceOf<InvalidDataException>(),
        "a second marker longer than its slot"
      );
      Assert.That(CaptureDataRecord.Read(With(CaptureDataRecord.MainLengthOffset, CaptureDataRecord.MainCapacity)).MainBytes, Has.Length.EqualTo(80));
      // Too few bytes are content that is not a record, not a caller's mistake: the bytes come from a file
      Assert.That(() => CaptureDataRecord.Read(new byte[CaptureDataRecord.Size - 1]), Throws.InstanceOf<InvalidDataException>(), "a byte short");
      Assert.That(
        () => new CaptureDataRecord(0, default, null, 0, CaptureDataStatus.Decoded, null, null).Write(new byte[CaptureDataRecord.Size - 1]),
        Throws.ArgumentException,
        "a buffer too small to write into"
      );
      var second = new CaptureDataRecord(0, default, null, 0, CaptureDataStatus.Decoded, null, new byte[CaptureDataRecord.SecondCapacity + 1]);
      Assert.That(() => second.Write(new byte[CaptureDataRecord.Size]), Throws.ArgumentException, "a second marker too long for its slot");
    }

    [Test]
    public void Record_ItsPartsAreWrittenApart_AndItsMarkersDecode()
    {
      var main = new byte[Payload.MaxEncodedByteCount];
      int mainLength = FrameMarker.EncodePayload(
        new Payload(MarkerKind.SequenceStart, 7, 12, MarkerFlags.StaticAfter, new TimeSpan(34)),
        default,
        main
      );
      var sync = new byte[Payload.MaxEncodedByteCount];
      int syncLength = FrameMarker.EncodePayload(new Payload(MarkerKind.Sync, 7, 11, MarkerFlags.NoFlags, TimeSpan.Zero), default, sync);
      Assert.That((mainLength, syncLength), Is.EqualTo((77, 16)), "the longest and the shortest marker");
      Assert.That(mainLength, Is.LessThanOrEqualTo(CaptureDataRecord.MainCapacity), "any marker fits a slot");

      // The recorder writes the capture part when the frame arrives and the decoded part when the decoder is done
      var parts = new byte[CaptureDataRecord.Size];
      Array.Fill(parts, (byte)0xEE);
      CaptureDataRecord.WriteCapture(parts, 5, new TickCount64(100), new TickCount64(-200), 3);
      CaptureDataRecord.WriteDecoded(parts, CaptureDataStatus.Torn, main.AsSpan(0, mainLength), sync.AsSpan(0, syncLength));
      var record = CaptureDataRecord.Read(parts);
      var whole = new byte[CaptureDataRecord.Size];
      record.Write(whole);
      Assert.That(parts, Is.EqualTo(whole), "the two parts are the whole record, the rest zero");
      // Each part leaves the other as it is, in either order
      var reversed = new byte[CaptureDataRecord.Size];
      CaptureDataRecord.WriteDecoded(reversed, CaptureDataStatus.Torn, main.AsSpan(0, mainLength), sync.AsSpan(0, syncLength));
      CaptureDataRecord.WriteCapture(reversed, 5, new TickCount64(100), new TickCount64(-200), 3);
      Assert.That(reversed, Is.EqualTo(whole), "the capture part written last");
      Assert.That(
        (record.CaptureIndex, record.HostTime, record.DeviceTime, record.SourceDrops, record.CaptureStatus),
        Is.EqualTo((5L, new TickCount64(100), (TickCount64?)new TickCount64(-200), 3u, CaptureDataStatus.Torn))
      );

      Assert.That(record.TryDecodeMain(out var payload, out _), Is.True);
      Assert.That(
        (payload.Kind, payload.RunId, payload.FrameIndex, payload.AnimationTime),
        Is.EqualTo((MarkerKind.SequenceStart, 7u, 12UL, new TimeSpan(34)))
      );
      Assert.That(record.TryDecodeSecond(out var second), Is.True);
      Assert.That((second.Kind, second.RunId, second.FrameIndex), Is.EqualTo((MarkerKind.Sync, 7u, 11UL)));

      // A record without markers, and bytes that are no marker
      var none = new CaptureDataRecord(0, default, null, 0, CaptureDataStatus.Undecodable, null, null);
      Assert.That(none.TryDecodeMain(out var noPayload, out _), Is.False);
      Assert.That(noPayload, Is.EqualTo(default(Payload)));
      Assert.That(none.TryDecodeSecond(out _), Is.False);
      var garbage = none with { MainBytes = new byte[53], SecondBytes = new byte[] { 1, 2, 3 } };
      Assert.That(garbage.TryDecodeMain(out _, out _), Is.False);
      Assert.That(garbage.TryDecodeSecond(out _), Is.False);
    }

    [Test]
    public void Reader_ReadsARecordByItsIndex_AndRefusesWhatIsNoFile()
    {
      string path = Path.Combine(Path.GetTempPath(), $"mb-framepacing-data-{Guid.NewGuid():N}.mbcd");
      try
      {
        var records = new CaptureDataRecord[5000];
        for (int i = 0; i < records.Length; ++i)
          records[i] = new CaptureDataRecord(
            i * 2,
            new TickCount64(i * 100L),
            i % 3 == 0 ? null : new TickCount64(i),
            (uint)(i % 7),
            CaptureDataStatus.Undecodable,
            null,
            null
          );
        using (var writer = new CaptureDataWriter(path, g_header))
        {
          Assert.That((writer.RecordsWritten, writer.BytesWritten), Is.EqualTo((0L, (long)CaptureDataHeader.HeaderSize)));
          writer.WriteRecords(records);
          Assert.That(
            (writer.RecordsWritten, writer.BytesWritten),
            Is.EqualTo((5000L, CaptureDataHeader.HeaderSize + (5000L * CaptureDataRecord.Size))),
            "more than one batch"
          );
          Assert.That(() => writer.WriteRecords(new byte[CaptureDataRecord.Size + 1]), Throws.ArgumentException, "not whole records");
          writer.WriteRecords(ReadOnlySpan<byte>.Empty);
          writer.WriteRecords(Array.Empty<CaptureDataRecord>());
          Assert.That(writer.RecordsWritten, Is.EqualTo(5000), "nothing more was written");
          Assert.That(() => writer.Complete(null!), Throws.ArgumentNullException);
          Assert.That(writer.Header, Is.SameAs(g_header));
        }

        using (var reader = new CaptureDataReader(path))
        {
          Assert.That(reader.RecordCount, Is.EqualTo(5000));
          var all = reader.ReadAll();
          foreach (int index in new[] { 0, 1, 4095, 4096, 4999 })
            Assert.That(reader.ReadRecord(index), Is.EqualTo(all[index]), "record " + index);
          Assert.That(all[4999], Is.EqualTo(records[4999]));
          Assert.That(() => reader.ReadRecord(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
          Assert.That(() => reader.ReadRecord(5000), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        // A file that ends inside its header, and one that is something else
        File.WriteAllBytes(path, new byte[CaptureDataHeader.HeaderSize - 1]);
        Assert.That(() => new CaptureDataReader(path), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("too short"));
        File.WriteAllBytes(path, new byte[CaptureDataHeader.HeaderSize]);
        Assert.That(() => new CaptureDataReader(path), Throws.InstanceOf<InvalidDataException>());
        // Refused files are closed again: the file can be replaced
        File.WriteAllBytes(path, Array.Empty<byte>());
        Assert.That(() => new CaptureDataReader(path), Throws.InstanceOf<InvalidDataException>());
        File.Delete(path);
        Assert.That(() => new CaptureDataReader(path), Throws.InstanceOf<FileNotFoundException>(), "a file that is not there is not a format error");
      }
      finally
      {
        File.Delete(path);
      }
    }

    [Test]
    public void Writer_ThatCannotWriteItsHeader_LeavesNoFile()
    {
      string path = Path.Combine(Path.GetTempPath(), $"mb-framepacing-data-{Guid.NewGuid():N}.mbcd");
      try
      {
        var tooMany = g_header with { Markers = new MarkerLocation[CaptureDataHeader.MaxMarkers + 1] };
        Assert.That(() => new CaptureDataWriter(path, tooMany), Throws.ArgumentException);
        Assert.That(File.Exists(path), Is.False, "no empty file is left behind");
        Assert.That(() => new CaptureDataWriter(path, null!), Throws.ArgumentNullException);
        Assert.That(File.Exists(path), Is.False);

        // So the same path can be written right after, and never over an existing file
        using (var writer = new CaptureDataWriter(path, g_header))
        {
          Assert.That(() => writer.Complete(tooMany), Throws.ArgumentException);
          Assert.That(writer.Header, Is.SameAs(g_header), "a header that cannot be written is not taken");
        }
        using (var reader = new CaptureDataReader(path))
          Assert.That(reader.Header.Markers, Is.EqualTo(g_header.Markers), "the file keeps the header it had");
        Assert.That(() => new CaptureDataWriter(path, g_header), Throws.InstanceOf<IOException>(), "it never replaces a file");

        var disposed = new CaptureDataWriter(path + ".2", g_header);
        disposed.Dispose();
        disposed.Dispose();
        Assert.That(() => disposed.WriteRecords(new byte[CaptureDataRecord.Size]), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(() => disposed.Complete(g_header), Throws.InstanceOf<ObjectDisposedException>());
      }
      finally
      {
        File.Delete(path);
        File.Delete(path + ".2");
      }
    }

    [Test]
    public void Records_RefuseMarkersTooLongForTheirSlot()
    {
      var record = new CaptureDataRecord(0, default, null, 0, CaptureDataStatus.Decoded, new byte[CaptureDataRecord.MainCapacity + 1], null);
      Assert.Throws<ArgumentException>(() => record.Write(new byte[CaptureDataRecord.Size]));
    }
  }
}
