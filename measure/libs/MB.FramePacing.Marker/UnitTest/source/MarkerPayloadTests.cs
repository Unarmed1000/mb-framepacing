//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ test (marker/cpp/tests/FrameMarkerTests.cpp) so both sides agree byte for byte.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class MarkerPayloadTests
  {
    [Test]
    public void Encode_MatchesDocumentedLayout()
    {
      var payload = new MarkerPayload(0x0102030405060708UL, 0x1112131415161718L, 0x21222324u, MarkerKind.SequenceEnd);
      byte[] expected =
      [
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
      ];
      Assert.That(payload.Encode(), Is.EqualTo(expected));
    }

    [TestCase(0UL, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1UL, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7UL, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42UL, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrip(ulong frameIndex, long ticks, uint runId, MarkerKind kind)
    {
      var payload = new MarkerPayload(frameIndex, ticks, runId, kind);
      Assert.That(MarkerPayload.TryDecode(payload.Encode(), out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, kind == MarkerKind.SequenceStart ? Is.EqualTo(StartMetadata.Empty) : Is.Null);
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var metadata = StartMetadata.Create(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc), "Benchmark æøå run");
      var payload = new MarkerPayload(10, 20, 30, MarkerKind.SequenceStart);
      var bytes = payload.Encode(metadata);
      Assert.That(bytes, Has.Length.EqualTo(MarkerPayload.StartFixedByteCount + System.Text.Encoding.UTF8.GetByteCount(metadata.Name)));
      Assert.That(MarkerPayload.TryDecode(bytes, out var decoded, out var start), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(start, Is.EqualTo(metadata));
      Assert.That(start!.StartTimeUtc, Is.EqualTo(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void StartMetadata_NameTooLong_Throws()
    {
      var payload = new MarkerPayload(1, 2, 3, MarkerKind.SequenceStart);
      Assert.Throws<ArgumentException>(() => payload.Encode(new StartMetadata(0, new string('x', MarkerPayload.MaxStartNameBytes + 1))));
      Assert.DoesNotThrow(() => payload.Encode(new StartMetadata(0, new string('x', MarkerPayload.MaxStartNameBytes))));
    }

    [Test]
    public void FrameMarker_IgnoresMetadata()
    {
      var bytes = new MarkerPayload(1, 2, 3, MarkerKind.Frame).Encode(new StartMetadata(5, "ignored"));
      Assert.That(bytes, Has.Length.EqualTo(MarkerPayload.ByteCount));
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new MarkerPayload(1, 2, 3).Encode();
      Assert.That(MarkerPayload.TryDecode(bytes.AsSpan(0, MarkerPayload.ByteCount - 1), out _), Is.False);

      bytes[0] = (byte)'X';
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
      bytes[0] = (byte)'M';

      bytes[2] = 2;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
      bytes[2] = 1;

      bytes[3] = 3;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);

      // A start marker must carry its metadata block
      bytes[3] = (byte)MarkerKind.SequenceStart;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsInvalidUtf8Name()
    {
      var bytes = new MarkerPayload(1, 2, 3, MarkerKind.SequenceStart).Encode(new StartMetadata(0, "ab"));
      bytes[^1] = 0xFF;
      Assert.That(MarkerPayload.TryDecode(bytes, out _), Is.False);
    }
  }
}
