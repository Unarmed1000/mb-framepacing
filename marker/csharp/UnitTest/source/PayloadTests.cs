//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ tests (marker/cpp/tests/FrameMarkerTests.cpp).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Text;
using NUnit.Framework;

namespace MB.FrameMarker.UnitTest
{
  [TestFixture]
  public class PayloadTests
  {
    [Test]
    public void Encode_ProducesTheDocumentedLittleEndianLayout()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      int count = Marker.EncodePayload(new Payload(0x0102030405060708u, 0x1112131415161718, 0x21222324u, MarkerKind.SequenceEnd), bytes);
      Assert.That(count, Is.EqualTo(Marker.PayloadByteCount));
      var expected = new byte[]
      {
        (byte)'M',
        (byte)'F',
        1,
        2,
        0x08,
        0x07,
        0x06,
        0x05,
        0x04,
        0x03,
        0x02,
        0x01,
        0x18,
        0x17,
        0x16,
        0x15,
        0x14,
        0x13,
        0x12,
        0x11,
        0x24,
        0x23,
        0x22,
        0x21,
      };
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void NegativeTicks_AreStoredAsTwosComplement()
    {
      var bytes = new byte[Marker.PayloadByteCount];
      Marker.EncodePayload(new Payload(0, -1), bytes);
      Assert.That(bytes.AsSpan(12, 8).ToArray(), Is.All.EqualTo((byte)0xFF));
    }

    [TestCase(0ul, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1ul, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7ul, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42ul, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrips(ulong frame, long ticks, uint run, MarkerKind kind)
    {
      var payload = new Payload(frame, ticks, run, kind);
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      int count = Marker.EncodePayload(payload, default, bytes);
      Assert.That(Marker.TryDecodePayload(bytes, 0, count, out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount + 5];
      var payload = new Payload(10, 20, 30, MarkerKind.SequenceStart);
      const string Name = "Benchmark æøå run";
      int count = Marker.EncodePayload(payload, new StartMetadata(638_000_000_000_000_000, Name), bytes, 5);
      Assert.That(count, Is.EqualTo(Marker.StartPayloadFixedByteCount + Encoding.UTF8.GetByteCount(Name)));

      Assert.That(Marker.TryDecodePayload(bytes, 5, count, out var decoded, out var metadata), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(metadata.UtcTicks, Is.EqualTo(638_000_000_000_000_000));
      Assert.That(metadata.Name, Is.EqualTo(Name));

      // A truncated name is rejected; frame payloads ignore the metadata
      Assert.That(Marker.TryDecodePayload(bytes, 5, count - 1, out _, out _), Is.False);
      Assert.That(Marker.EncodePayload(new Payload(1, 2, 3), new StartMetadata(5, Name), bytes), Is.EqualTo(Marker.PayloadByteCount));
    }

    [Test]
    public void Encode_RejectsLongNamesAndSmallBuffers()
    {
      var bytes = new byte[Marker.MaxEncodedPayloadByteCount];
      var start = new Payload(1, 2, 3, MarkerKind.SequenceStart);
      Assert.That(Marker.EncodePayload(start, new StartMetadata(0, new string('x', Marker.MaxStartNameBytes)), bytes), Is.EqualTo(97));
      Assert.That(Marker.EncodePayload(start, new StartMetadata(0, new string('x', Marker.MaxStartNameBytes + 1)), bytes), Is.Zero);
      Assert.That(Marker.EncodePayload(start, new StartMetadata(0, new string('æ', 33)), bytes), Is.Zero, "66 UTF-8 bytes");
      Assert.That(Marker.EncodePayload(new Payload(1, 2), new byte[Marker.PayloadByteCount - 1]), Is.Zero);
      Assert.That(Marker.EncodePayload(start, new StartMetadata(0, null), bytes), Is.EqualTo(Marker.StartPayloadFixedByteCount));
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new byte[Marker.PayloadByteCount];
      Marker.EncodePayload(new Payload(1, 2), bytes);
      Assert.That(Marker.TryDecodePayload(bytes, 0, Marker.PayloadByteCount - 1, out _, out _), Is.False, "short");
      bytes[0] = (byte)'X';
      Assert.That(Marker.TryDecodePayload(bytes, 0, bytes.Length, out _, out _), Is.False, "magic");
      bytes[0] = (byte)'M';
      bytes[2] = 2;
      Assert.That(Marker.TryDecodePayload(bytes, 0, bytes.Length, out _, out _), Is.False, "format version");
      bytes[2] = 1;
      bytes[3] = 3;
      Assert.That(Marker.TryDecodePayload(bytes, 0, bytes.Length, out _, out _), Is.False, "kind");
      bytes[3] = 0;
      Assert.That(Marker.TryDecodePayload(bytes, 0, bytes.Length, out _, out _), Is.True);

      // A start marker without its metadata block, and one whose name is not valid UTF-8
      Marker.EncodePayload(new Payload(1, 2, 3, MarkerKind.SequenceStart), default, bytes = new byte[Marker.StartPayloadFixedByteCount + 1]);
      Assert.That(Marker.TryDecodePayload(bytes, 0, Marker.PayloadByteCount, out _, out _), Is.False);
      bytes[Marker.StartPayloadFixedByteCount - 1] = 1;
      bytes[Marker.StartPayloadFixedByteCount] = 0xC3;
      Assert.That(Marker.TryDecodePayload(bytes, 0, bytes.Length, out _, out _), Is.False);
    }

    [Test]
    public void Ticks_MatchDateTimeAndTimeSpan()
    {
      var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      Assert.That(Marker.ToDateTimeTicks(time), Is.EqualTo(639_028_224_000_000_000));
      Assert.That(StartMetadata.Create(time, "x").UtcTicks, Is.EqualTo(time.Ticks));
      Assert.That(Marker.SecondsToTicks(1.5), Is.EqualTo(TimeSpan.FromSeconds(1.5).Ticks));
      Assert.That(Marker.SecondsToTicks(1.0 / 60), Is.EqualTo(166_667));
    }
  }
}
