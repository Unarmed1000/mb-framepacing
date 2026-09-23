//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* .mbfc header and record round trips.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.IO;
using MB.FramePacing.Marker;
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
          1000 * i,
          i == 1 ? CaptureRecordHeader.UnknownTicks : 5000 * i,
          CaptureRecordFlags.None,
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
      Assert.That(second.HostTicks, Is.EqualTo(1000));
      Assert.That(second.HasDeviceTicks, Is.False);
      Assert.That(image[16, 4], Is.EqualTo(2));
      Assert.That(reader.ReadRecordHeader(2).DeviceTicks, Is.EqualTo(10000));
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
