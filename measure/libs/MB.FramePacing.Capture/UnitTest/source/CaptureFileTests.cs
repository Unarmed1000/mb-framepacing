//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* .mbfc header and record round trips.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using MB.FramePacing.Data;
using MB.FramePacing.MarkerDecoding;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class CaptureFileTests
  {
    [Test]
    public void Header_RoundTrips()
    {
      var header = new CaptureFileHeader(960, 540, new FrameRate(60000, 1001), 1920, 1080, new PixelRect(10, 20, 300, 400));
      var bytes = new byte[CaptureFileHeader.HeaderSize];
      header.Write(bytes);
      Assert.That(CaptureFileHeader.Read(bytes), Is.EqualTo(header));
      Assert.That(header.RecordSize % CaptureFileHeader.RecordAlignment, Is.Zero);
      Assert.That(header.RecordSize, Is.GreaterThanOrEqualTo(CaptureFileHeader.RecordHeaderSize + (960 * 540)));
    }

    /// <summary>Both markers' regions, stacked: the header names the second one too, and the records still start on a 64 byte boundary.</summary>
    [Test]
    public void Header_RoundTrips_WithTheSyncMarkersRegion()
    {
      var header = new CaptureFileHeader(
        165,
        282,
        FrameRate.FromFps(60),
        1920,
        1080,
        new PixelRect(14, 14, 330, 330),
        new PixelRect(14, 832, 234, 234)
      );
      var bytes = new byte[CaptureFileHeader.HeaderSize];
      bytes.AsSpan().Fill(0xEE);
      header.Write(bytes);

      var read = CaptureFileHeader.Read(bytes);
      Assert.That(read, Is.EqualTo(header));
      Assert.That(read.SyncRoi, Is.EqualTo(new PixelRect(14, 832, 234, 234)));
      Assert.That(CaptureFileHeader.HeaderSize % CaptureFileHeader.RecordAlignment, Is.Zero);
      // The second region follows the first; what is left of the header is reserved, written as 0
      Assert.That(BitConverter.ToInt32(bytes, 64), Is.EqualTo(14));
      Assert.That(BitConverter.ToInt32(bytes, 68), Is.EqualTo(832));
      Assert.That(bytes.AsSpan(80).ToArray(), Is.All.Zero);
      Assert.That(bytes.AsSpan(28, 4).ToArray(), Is.All.Zero);
    }

    /// <summary>
    /// The second region is there when the header is long enough to hold it: a file with the shortest header (64 bytes) has none, and
    /// what follows those 64 bytes is its first record, not a region.
    /// </summary>
    [Test]
    public void Header_OfTheShortestSize_HasNoSecondRegion()
    {
      var header = new CaptureFileHeader(
        165,
        165,
        FrameRate.FromFps(60),
        1920,
        1080,
        new PixelRect(14, 14, 330, 330),
        new PixelRect(14, 832, 234, 234)
      );
      var bytes = new byte[CaptureFileHeader.HeaderSize];
      header.Write(bytes);
      bytes[6] = CaptureFileHeader.MinHeaderSize;

      var read = CaptureFileHeader.Read(bytes, out int headerSize);
      Assert.That(headerSize, Is.EqualTo(64));
      Assert.That(read, Is.EqualTo(header with { SyncRoi = default }));
      Assert.That(CaptureFileHeader.Read(bytes.AsSpan(0, 64)), Is.EqualTo(read), "the 64 bytes alone are a whole header");
    }

    [Test]
    public void Header_RefusesAHeaderSizeBelowTheShortest_AndAFileThatEndsInsideItsHeader()
    {
      var bytes = new byte[CaptureFileHeader.HeaderSize];
      new CaptureFileHeader(8, 4, FrameRate.FromFps(60)).Write(bytes);
      Assert.Throws<InvalidDataException>(() => CaptureFileHeader.Read(bytes.AsSpan(0, 100)), "128 bytes of header, 100 of file");
      bytes[6] = 48;
      Assert.Throws<InvalidDataException>(() => CaptureFileHeader.Read(bytes));
    }

    /// <summary>A longer header than this reader knows: the fields it knows are read, and the records start after the whole header.</summary>
    [Test]
    public void Reader_FindsTheRecordsAfterTheFilesOwnHeader([Values(64, 128, 192)] int headerSize)
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var header = new CaptureFileHeader(6, 3, FrameRate.FromFps(60), 1920, 1080, new PixelRect(1, 2, 6, 3), new PixelRect(3, 4, 6, 3));
      var bytes = new byte[headerSize + (2 * header.RecordSize)];
      var written = new byte[CaptureFileHeader.HeaderSize];
      header.Write(written);
      written.AsSpan(0, Math.Min(headerSize, written.Length)).CopyTo(bytes);
      bytes[6] = (byte)headerSize;
      for (int i = 0; i < 2; ++i)
      {
        var slot = bytes.AsSpan(headerSize + (i * header.RecordSize), header.RecordSize);
        new CaptureRecordHeader(70 + i, new TickCount64(i), DeviceTimestamp.Unknown, 0, header.PixelByteCount).Write(slot);
        slot.Slice(CaptureFileHeader.RecordHeaderSize, header.PixelByteCount).Fill((byte)(200 + i));
      }
      File.WriteAllBytes(path, bytes);

      using var reader = new CaptureFileReader(path);
      Assert.That(reader.Header, Is.EqualTo(headerSize >= 80 ? header : header with { SyncRoi = default }));
      Assert.That(reader.RecordCount, Is.EqualTo(2));
      var image = reader.CreateFrameImage();
      Assert.That(reader.ReadRecord(1, image).CaptureIndex, Is.EqualTo(71));
      Assert.That(image[5, 2], Is.EqualTo(201));
    }

    [Test]
    public void Header_SizeText_NamesTheSourceWhenLessOfItWasStored()
    {
      var rate = FrameRate.FromFps(60);
      // Only the markers' regions of a 1080p recording, a downscale, and frames stored as they are or of an unknown source
      Assert.That(
        new CaptureFileHeader(165, 282, rate, 1920, 1080, new PixelRect(14, 14, 330, 330)).SizeText,
        Is.EqualTo("1920x1080 source, 165x282 stored")
      );
      Assert.That(new CaptureFileHeader(960, 540, rate, 1920, 1080).SizeText, Is.EqualTo("1920x1080 source, 960x540 stored"));
      Assert.That(new CaptureFileHeader(1920, 1080, rate, 1920, 1080).SizeText, Is.EqualTo("1920x1080"));
      Assert.That(new CaptureFileHeader(640, 360, rate).SizeText, Is.EqualTo("640x360"));
    }

    [Test]
    public void Header_RejectsForeignFiles()
    {
      Assert.Throws<InvalidDataException>(() => CaptureFileHeader.Read(new byte[CaptureFileHeader.HeaderSize]));
    }

    [Test]
    public void Records_RoundTrip_AndPreallocationIsTrimmed()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var header = new CaptureFileHeader(17, 5, FrameRate.FromFps(240));
      var record = new byte[header.RecordSize * 3];
      for (int i = 0; i < 3; ++i)
      {
        var slot = record.AsSpan(i * header.RecordSize, header.RecordSize);
        new CaptureRecordHeader(
          100 + i,
          new TickCount64(1000 * i),
          i == 1 ? DeviceTimestamp.Unknown : new DeviceTimestamp(new TickCount64(5000 * i)),
          0,
          header.PixelByteCount
        ).Write(slot);
        slot.Slice(CaptureFileHeader.RecordHeaderSize, header.PixelByteCount).Fill((byte)(i + 1));
      }

      using (var writer = new CaptureFileWriter(path, header, expectedRecordCount: 100))
        writer.WriteRecords(record);

      Assert.That(new FileInfo(path).Length, Is.EqualTo(CaptureFileHeader.HeaderSize + (3 * header.RecordSize)));
      using var reader = new CaptureFileReader(path);
      Assert.That(reader.Header, Is.EqualTo(header));
      Assert.That(reader.RecordCount, Is.EqualTo(3));

      var image = reader.CreateFrameImage();
      var second = reader.ReadRecord(1, image);
      Assert.That(second.CaptureIndex, Is.EqualTo(101));
      Assert.That(second.HostTime.Ticks, Is.EqualTo(1000));
      Assert.That(second.DeviceTime, Is.EqualTo(DeviceTimestamp.Unknown));
      Assert.That(image[16, 4], Is.EqualTo(2));
      Assert.That(reader.ReadRecordHeader(2).DeviceTime.Time.Ticks, Is.EqualTo(10000));
    }

    [Test]
    public void Writer_RefusesToOverwrite()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      File.WriteAllText(path, "existing");
      Assert.Throws<IOException>(() => new CaptureFileWriter(path, new CaptureFileHeader(4, 4, FrameRate.Unknown)).Dispose());
    }

    [TestCase(59.94, 60000u, 1001u)]
    [TestCase(60.0, 60u, 1u)]
    [TestCase(239.76, 240000u, 1001u)]
    [TestCase(143.5, 143500u, 1000u)]
    public void FrameRate_FromFps(double fps, uint numerator, uint denominator)
    {
      Assert.That(FrameRate.FromFps(fps), Is.EqualTo(new FrameRate(numerator, denominator)));
    }
  }
}
