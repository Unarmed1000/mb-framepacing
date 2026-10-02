//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ tests (sdk/cpp/marker/tests/FrameMarkerTests.cpp).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Text;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class PayloadTests
  {
    [Test]
    public void Encode_ProducesTheDocumentedLittleEndianLayout()
    {
      var bytes = new byte[Payload.MaxEncodedByteCount];
      int count = FrameMarker.EncodePayload(
        new Payload(
          MarkerKind.SequenceEnd,
          0x21222324u,
          0x0102030405060708u,
          MarkerFlags.StaticAfter,
          new TimeSpan(0x1112131415161718),
          preferredFrameTime: new TimeSpan32(0x71727374u),
          targetFrameTime: new TimeSpan32(0x41424344u),
          intendedDisplayTime: new TickCount64(0x3132333435363738),
          cpuStartTime: new TickCount64(0x5152535455565758),
          cpuBusy: new TimeSpan32(0x61626364u)
        ),
        default,
        bytes
      );
      Assert.That(count, Is.EqualTo(WireFormat.PayloadByteCount));
      var expected = new byte[]
      {
        (byte)'M',
        (byte)'F',
        1,
        2,
        // run id
        0x24,
        0x23,
        0x22,
        0x21,
        // frame index
        0x08,
        0x07,
        0x06,
        0x05,
        0x04,
        0x03,
        0x02,
        0x01,
        // flags
        0x01,
        // animation time
        0x18,
        0x17,
        0x16,
        0x15,
        0x14,
        0x13,
        0x12,
        0x11,
        // preferred frame time
        0x74,
        0x73,
        0x72,
        0x71,
        // target frame time
        0x44,
        0x43,
        0x42,
        0x41,
        // intended display time
        0x38,
        0x37,
        0x36,
        0x35,
        0x34,
        0x33,
        0x32,
        0x31,
        // CPU start time
        0x58,
        0x57,
        0x56,
        0x55,
        0x54,
        0x53,
        0x52,
        0x51,
        // CPU busy
        0x64,
        0x63,
        0x62,
        0x61,
      };
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void NegativeTicks_AreStoredAsTwosComplement()
    {
      var bytes = new byte[WireFormat.PayloadByteCount];
      FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 0, 0, MarkerFlags.NoFlags, new TimeSpan(-1)), default, bytes);
      Assert.That(bytes.AsSpan(17, 8).ToArray(), Is.All.EqualTo((byte)0xFF));
    }

    [TestCase(0ul, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1ul, 166_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7ul, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42ul, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrips(ulong frame, long ticks, uint run, MarkerKind kind)
    {
      var payload = new Payload(kind, run, frame, MarkerFlags.NoFlags, new TimeSpan(ticks));
      var bytes = new byte[Payload.MaxEncodedByteCount];
      int count = FrameMarker.EncodePayload(payload, default, bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    [TestCase(0L, 0u)]
    [TestCase(1_234_567_890_123L, 166_667u)]
    [TestCase(long.MinValue, uint.MaxValue)]
    [TestCase(long.MaxValue, 333_333u)]
    public void PacingFields_RoundTrip(long intendedDisplayTicks, uint targetFrameTicks)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(
          kind,
          9,
          7,
          MarkerFlags.NoFlags,
          new TimeSpan(8),
          targetFrameTime: new TimeSpan32(targetFrameTicks),
          intendedDisplayTime: new TickCount64(intendedDisplayTicks),
          cpuStartTime: new TickCount64(-intendedDisplayTicks / 2),
          cpuBusy: new TimeSpan32(targetFrameTicks / 3)
        );
        var bytes = new byte[Payload.MaxEncodedByteCount];
        int count = FrameMarker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That(decoded.IntendedDisplayTime.Ticks, Is.EqualTo(intendedDisplayTicks));
        Assert.That(decoded.TargetFrameTime.Ticks, Is.EqualTo(targetFrameTicks));
        Assert.That(decoded.CpuStartTime.Ticks, Is.EqualTo(-intendedDisplayTicks / 2));
        Assert.That(decoded.CpuBusy.Ticks, Is.EqualTo(targetFrameTicks / 3));
      }
    }

    [TestCase(166_667u, MarkerFlags.NoFlags)]
    [TestCase(uint.MaxValue, MarkerFlags.StaticAfter)] // Payload.OnDemandFrameTime
    [TestCase(10_000_000u, MarkerFlags.StaticAfter)]
    [TestCase(166_667u, MarkerFlags.StaticBefore)]
    [TestCase(166_667u, MarkerFlags.StaticAfter | MarkerFlags.StaticBefore)]
    [TestCase(0u, (MarkerFlags)0x81)]
    public void PreferredFrameTimeAndFlags_RoundTrip(uint preferredFrameTicks, MarkerFlags flags)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(
          kind,
          9,
          7,
          flags,
          new TimeSpan(8),
          preferredFrameTime: new TimeSpan32(preferredFrameTicks),
          targetFrameTime: new TimeSpan32(333_333),
          intendedDisplayTime: new TickCount64(10),
          cpuStartTime: new TickCount64(11),
          cpuBusy: new TimeSpan32(12)
        );
        var bytes = new byte[Payload.MaxEncodedByteCount];
        int count = FrameMarker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(bytes[16], Is.EqualTo((byte)flags), "the flags byte, reserved bits included");
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That((decoded.PreferredFrameTime.Ticks, decoded.Flags), Is.EqualTo((preferredFrameTicks, flags)));
      }
    }

    [Test]
    public void OldHeaderLengths_AreRejected()
    {
      var bytes = new byte[Payload.MaxEncodedByteCount + 1];
      int count = FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.NoFlags, new TimeSpan(2)), default, bytes);
      Assert.That(count, Is.EqualTo(53));
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 52), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 54), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 48), out _, out _), Is.False, "the older 48 byte header");
    }

    [Test]
    public void SyncMarker_CarriesOnlyTheRunIdAndTheFrameIndex()
    {
      var bytes = new byte[Payload.MaxEncodedByteCount];
      int count = FrameMarker.EncodePayload(
        new Payload(
          MarkerKind.Sync,
          4,
          0x0102030405060708u,
          MarkerFlags.NoFlags,
          new TimeSpan(123),
          targetFrameTime: new TimeSpan32(6),
          intendedDisplayTime: new TickCount64(5)
        ),
        default,
        bytes
      );
      Assert.That(count, Is.EqualTo(WireFormat.SyncPayloadByteCount));
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(new byte[] { (byte)'M', (byte)'F', 1, 3, 4, 0, 0, 0, 8, 7, 6, 5, 4, 3, 2, 1 }));
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(new Payload(MarkerKind.Sync, 4, 0x0102030405060708u, MarkerFlags.NoFlags, new TimeSpan(0))));
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count + 1), out _, out _), Is.False);
    }

    [Test]
    public void StartMetadata_RoundTrips()
    {
      var bytes = new byte[Payload.MaxEncodedByteCount + 6];
      var payload = new Payload(
        MarkerKind.SequenceStart,
        30,
        10,
        MarkerFlags.NoFlags,
        new TimeSpan(20),
        targetFrameTime: new TimeSpan32(50),
        intendedDisplayTime: new TickCount64(40),
        cpuStartTime: new TickCount64(60),
        cpuBusy: new TimeSpan32(70)
      );
      var id = new SequenceId(0x0011_2233_4455_6677, 0x8899_AABB_CCDD_EEFF);
      int count = FrameMarker.EncodePayload(payload, new StartMetadata(638_000_000_000_000_000, id), bytes.AsSpan(5));
      Assert.That(count, Is.EqualTo(WireFormat.StartPayloadByteCount));
      Assert.That(count, Is.EqualTo(77));
      // The sequence id's 16 bytes as they are, at offset 61
      Assert.That(
        bytes.AsSpan(5 + 61, 16).ToArray(),
        Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF })
      );

      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(5, count), out var decoded, out var metadata), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
      Assert.That(metadata.UtcTicks, Is.EqualTo(638_000_000_000_000_000));
      Assert.That(metadata.SequenceId, Is.EqualTo(id));

      // A start marker of another length is rejected; frame payloads ignore the metadata
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(5, count - 1), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(5, count + 1), out _, out _), Is.False);
      Assert.That(
        FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.NoFlags, new TimeSpan(2)), new StartMetadata(5, id), bytes),
        Is.EqualTo(WireFormat.PayloadByteCount)
      );
    }

    [Test]
    public void Encode_RejectsSmallBuffers()
    {
      var start = new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.NoFlags, new TimeSpan(2));
      Assert.That(FrameMarker.EncodePayload(start, default, new byte[WireFormat.StartPayloadByteCount - 1]), Is.Zero);
      Assert.That(
        FrameMarker.EncodePayload(
          new Payload(MarkerKind.Frame, 0, 1, MarkerFlags.NoFlags, new TimeSpan(2)),
          default,
          new byte[WireFormat.PayloadByteCount - 1]
        ),
        Is.Zero
      );
      Assert.That(
        FrameMarker.EncodePayload(start, default, new byte[WireFormat.StartPayloadByteCount]),
        Is.EqualTo(WireFormat.StartPayloadByteCount)
      );
    }

    [Test]
    public void SequenceId_TextGuidAndDisplay()
    {
      Assert.That(SequenceId.TryFromText("bench-2026-09-28", out var text), Is.True, "16 characters fit");
      Assert.That(text.ToString(), Is.EqualTo("bench-2026-09-28"));
      Assert.That(SequenceId.TryFromText("run 7", out var shortText), Is.True);
      Assert.That(shortText.ToString(), Is.EqualTo("run 7"), "padded with zero bytes, shown without them");
      Assert.That(SequenceId.TryFromText("seventeen chars!!", out _), Is.False, "too long");
      Assert.That(SequenceId.TryFromText("tab\there", out _), Is.False, "not printable");
      Assert.That(SequenceId.TryFromText("æøå", out _), Is.False, "not ASCII");
      Assert.That(SequenceId.TryFromText(string.Empty, out var empty), Is.False, "no text");
      Assert.That(empty.IsEmpty, Is.True);
      Assert.That(SequenceId.TryFromText(null!, out _), Is.False, "no text at all");
      Assert.That(SequenceId.TryFromText("~ ", out var edges), Is.True, "the first and the last printable character");
      Assert.That(edges.ToString(), Is.EqualTo("~ "));
      Assert.That(SequenceId.TryFromText("a\u007Fb", out _), Is.False, "DEL is not printable");
      Assert.That(SequenceId.TryFromText("a\0b", out _), Is.False, "a zero inside the text is not padding");

      var guid = Guid.Parse("3f2a1b4c-5d6e-7f80-9102-a3b4c5d6e7f8");
      var fromGuid = SequenceId.FromGuid(guid);
      Assert.That(fromGuid.ToString(), Is.EqualTo("3f2a1b4c-5d6e-7f80-9102-a3b4c5d6e7f8"), "the bytes in the order the UUID text shows them");
      var bytes = new byte[SequenceId.ByteCount];
      Assert.That(fromGuid.TryCopyTo(bytes), Is.True);
      Assert.That(bytes[0], Is.EqualTo(0x3f));
      Assert.That(SequenceId.FromBytes(bytes), Is.EqualTo(fromGuid));
      Assert.That(new SequenceId(0, 1).ToString(), Is.EqualTo("00000000-0000-0000-0000-000000000001"), "not text: hex");
      Assert.That(default(SequenceId).IsEmpty, Is.True);
    }

    [Test]
    public void SequenceId_FromGuid_MatchesTheUuidText()
    {
      var random = new Random(1234);
      var raw = new byte[16];
      for (int i = 0; i < 200; ++i)
      {
        random.NextBytes(raw);
        var guid = new Guid(raw);
        Assert.That(SequenceId.FromGuid(guid).ToString(), Is.EqualTo(guid.ToString("D")), guid.ToString());
      }
      Assert.That(SequenceId.FromGuid(Guid.Empty).IsEmpty, Is.True);
    }

    [Test]
    public void TryDecode_RejectsBadInput()
    {
      var bytes = new byte[WireFormat.PayloadByteCount];
      FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 0, 1, MarkerFlags.NoFlags, new TimeSpan(2)), default, bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.PayloadByteCount - 1), out _, out _), Is.False, "short");
      bytes[0] = (byte)'X';
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "magic");
      bytes[0] = (byte)'M';
      bytes[2] = 2;
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "format version");
      bytes[2] = 1;
      bytes[3] = 3;
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "a sync marker is 16 bytes");
      foreach (byte kind in new byte[] { 4, 5, 127, 128, 255 })
      {
        bytes[3] = kind;
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "an unknown kind: " + kind);
        // The same in a sync marker's 16 bytes: the kind is checked before the length it implies
        Assert.That(
          FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.SyncPayloadByteCount), out _, out _),
          Is.False,
          "sync length, kind " + kind
        );
      }
      bytes[3] = 0;
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.True);

      // A start marker without its metadata block, or with a byte too many
      FrameMarker.EncodePayload(
        new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.NoFlags, new TimeSpan(2)),
        default,
        bytes = new byte[WireFormat.StartPayloadByteCount + 1]
      );
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.PayloadByteCount), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.StartPayloadByteCount), out _, out _), Is.True);
    }

    [Test]
    public void StartMetadata_TakesTheStartTimeAsUtcDateTimeTicks()
    {
      var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      Assert.That(StartMetadata.Create(time, new SequenceId(1, 2)).UtcTicks, Is.EqualTo(639_028_224_000_000_000));
      Assert.That(StartMetadata.Create(time.ToLocalTime(), new SequenceId(1, 2)).UtcTicks, Is.EqualTo(time.Ticks), "a local time is converted");
      Assert.That(StartMetadata.Create(time, new SequenceId(1, 2)).SequenceId, Is.EqualTo(new SequenceId(1, 2)));
    }
  }
}
