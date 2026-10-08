//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Wire format tests. The expected bytes are the same as the C++ tests (sdk/cpp/marker/tests/FrameMarkerTests.cpp).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class PayloadTests
  {
    // Puts the CRC of the bytes before it into the last four bytes: a payload changed on purpose that the CRC does not give away
    private static void PutCrc(Span<byte> payload)
    {
      int fieldByteCount = payload.Length - WireFormat.CrcByteCount;
      BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(fieldByteCount), Crc32.Compute(payload.Slice(0, fieldByteCount)));
    }

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
          new NanosecondTimeSpan(0x1112131415161718),
          preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(0x71727374),
          targetFrameTime: NanosecondTimeDuration.FromNanoseconds(0x41424344),
          intendedDisplayTime: new NanosecondTickCount(0x3132333435363738),
          cpuStartTime: new NanosecondTickCount(0x5152535455565758),
          cpuBusy: NanosecondTimeDuration.FromNanoseconds(0x61626364)
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
        // CRC (0x7ED16A3D: what Python's binascii.crc32 gives for the 53 bytes before it)
        0x3D,
        0x6A,
        0xD1,
        0x7E,
      };
      Assert.That(count, Is.EqualTo(57));
      Assert.That(bytes.AsSpan(0, count).ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void ANegativeAnimationTime_IsStoredAsTwosComplement()
    {
      var bytes = new byte[WireFormat.PayloadByteCount];
      FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 0, 0, MarkerFlags.NoFlags, new NanosecondTimeSpan(-1)), default, bytes);
      Assert.That(bytes.AsSpan(17, 8).ToArray(), Is.All.EqualTo((byte)0xFF));
    }

    [TestCase(0ul, 0L, 0u, MarkerKind.Frame)]
    [TestCase(1ul, 16_666_667L, 7u, MarkerKind.Frame)]
    [TestCase(ulong.MaxValue, long.MaxValue, uint.MaxValue, MarkerKind.Frame)]
    [TestCase(7ul, long.MinValue, 1u, MarkerKind.SequenceStart)]
    [TestCase(42ul, -1L, 3u, MarkerKind.SequenceEnd)]
    public void RoundTrips(ulong frame, long animationNanoseconds, uint run, MarkerKind kind)
    {
      var payload = new Payload(kind, run, frame, MarkerFlags.NoFlags, new NanosecondTimeSpan(animationNanoseconds));
      var bytes = new byte[Payload.MaxEncodedByteCount];
      int count = FrameMarker.EncodePayload(payload, default, bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(payload));
    }

    [TestCase(0L, 0u)]
    [TestCase(1_234_567_890_123L, 16_666_667u)]
    [TestCase(long.MinValue, uint.MaxValue)] // Payload.OnDemandFrameTime
    [TestCase(long.MaxValue, 33_333_333u)]
    public void PacingFields_RoundTrip(long intendedDisplayNanoseconds, uint targetFrameNanoseconds)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(
          kind,
          9,
          7,
          MarkerFlags.NoFlags,
          new NanosecondTimeSpan(8),
          targetFrameTime: NanosecondTimeDuration.FromNanoseconds(targetFrameNanoseconds),
          intendedDisplayTime: new NanosecondTickCount(intendedDisplayNanoseconds),
          cpuStartTime: new NanosecondTickCount(-intendedDisplayNanoseconds / 2),
          cpuBusy: NanosecondTimeDuration.FromNanoseconds(targetFrameNanoseconds / 3)
        );
        var bytes = new byte[Payload.MaxEncodedByteCount];
        int count = FrameMarker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That(decoded.IntendedDisplayTime.Nanoseconds, Is.EqualTo(intendedDisplayNanoseconds));
        Assert.That(decoded.TargetFrameTime.Nanoseconds, Is.EqualTo(targetFrameNanoseconds));
        Assert.That(decoded.CpuStartTime.Nanoseconds, Is.EqualTo(-intendedDisplayNanoseconds / 2));
        Assert.That(decoded.CpuBusy.Nanoseconds, Is.EqualTo(targetFrameNanoseconds / 3));
      }
    }

    [TestCase(16_666_667u, MarkerFlags.NoFlags)]
    [TestCase(uint.MaxValue, MarkerFlags.StaticAfter)] // Payload.OnDemandFrameTime
    [TestCase(1_000_000_000u, MarkerFlags.StaticAfter)]
    [TestCase(16_666_667u, MarkerFlags.StaticBefore)]
    [TestCase(16_666_667u, MarkerFlags.StaticAfter | MarkerFlags.StaticBefore)]
    [TestCase(0u, (MarkerFlags)0x81)]
    public void PreferredFrameTimeAndFlags_RoundTrip(uint preferredFrameNanoseconds, MarkerFlags flags)
    {
      foreach (var kind in new[] { MarkerKind.Frame, MarkerKind.SequenceStart, MarkerKind.SequenceEnd })
      {
        var payload = new Payload(
          kind,
          9,
          7,
          flags,
          new NanosecondTimeSpan(8),
          preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(preferredFrameNanoseconds),
          targetFrameTime: NanosecondTimeDuration.FromNanoseconds(33_333_333),
          intendedDisplayTime: new NanosecondTickCount(10),
          cpuStartTime: new NanosecondTickCount(11),
          cpuBusy: NanosecondTimeDuration.FromNanoseconds(12)
        );
        var bytes = new byte[Payload.MaxEncodedByteCount];
        int count = FrameMarker.EncodePayload(payload, new StartMetadata(1, new SequenceId(1, 2)), bytes);
        Assert.That(bytes[16], Is.EqualTo((byte)flags), "the flags byte, reserved bits included");
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
        Assert.That(decoded, Is.EqualTo(payload));
        Assert.That((decoded.PreferredFrameTime.Nanoseconds, decoded.Flags), Is.EqualTo(((long)preferredFrameNanoseconds, flags)));
      }
    }

    [Test]
    public void ADurationLongerThanItsFourBytes_IsHeldAsTheLongestAMarkerCarries()
    {
      // The marker's three durations are four unsigned bytes of nanoseconds each: 4.294967295 s at most
      Assert.That(Payload.MaxCpuBusy.Nanoseconds, Is.EqualTo(0xFFFFFFFF));
      Assert.That(Payload.OnDemandFrameTime.Nanoseconds, Is.EqualTo(0xFFFFFFFF));
      Assert.That(Payload.MaxFrameTime.Nanoseconds, Is.EqualTo(0xFFFFFFFE));
      var tenSeconds = NanosecondTimeDuration.FromNanoseconds(10_000_000_000);
      var longest = NanosecondTimeDuration.MaxValue;

      // A longer one is capped where the payload is made (never an error: this runs in a frame loop), so a payload holds what a marker can
      var capped = new Payload(
        MarkerKind.Frame,
        1,
        2,
        MarkerFlags.NoFlags,
        new NanosecondTimeSpan(3),
        preferredFrameTime: tenSeconds,
        targetFrameTime: longest,
        cpuBusy: tenSeconds
      );
      Assert.That(capped.PreferredFrameTime, Is.EqualTo(Payload.MaxFrameTime));
      Assert.That(capped.TargetFrameTime, Is.EqualTo(Payload.MaxFrameTime));
      Assert.That(capped.CpuBusy, Is.EqualTo(Payload.MaxCpuBusy));
      Assert.That(capped.CpuBusy.Nanoseconds, Is.EqualTo(4_294_967_295));
      Assert.That(
        new Payload(MarkerKind.Frame, 1, 2, MarkerFlags.NoFlags, new NanosecondTimeSpan(3), cpuBusy: longest).CpuBusy,
        Is.EqualTo(Payload.MaxCpuBusy)
      );

      // The longest that fit are kept, and so is on demand: only on demand reads as on demand
      var kept = new Payload(
        MarkerKind.Frame,
        1,
        2,
        MarkerFlags.NoFlags,
        new NanosecondTimeSpan(3),
        preferredFrameTime: Payload.MaxFrameTime,
        targetFrameTime: Payload.OnDemandFrameTime,
        cpuBusy: Payload.MaxCpuBusy
      );
      Assert.That(kept.PreferredFrameTime, Is.EqualTo(Payload.MaxFrameTime));
      Assert.That(kept.TargetFrameTime, Is.EqualTo(Payload.OnDemandFrameTime));
      Assert.That(kept.CpuBusy, Is.EqualTo(Payload.MaxCpuBusy));
      // One nanosecond past on demand is a frame time again, and too long
      var pastOnDemand = new Payload(
        MarkerKind.Frame,
        1,
        2,
        MarkerFlags.NoFlags,
        new NanosecondTimeSpan(3),
        preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(4_294_967_296)
      );
      Assert.That(pastOnDemand.PreferredFrameTime, Is.EqualTo(Payload.MaxFrameTime));
      // Every kind caps, and another kind of the same payload holds the same values
      Assert.That(capped.WithKind(MarkerKind.SequenceStart).TargetFrameTime, Is.EqualTo(Payload.MaxFrameTime));
      Assert.That(kept.WithKind(MarkerKind.SequenceEnd).TargetFrameTime, Is.EqualTo(Payload.OnDemandFrameTime));

      // On the wire: 0xFFFFFFFE for a capped frame time, 0xFFFFFFFF for on demand and for a capped CPU busy
      var oneBelow = new byte[] { 0xFE, 0xFF, 0xFF, 0xFF };
      var largest = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
      var cappedBytes = new byte[WireFormat.PayloadByteCount];
      Assert.That(FrameMarker.EncodePayload(capped, default, cappedBytes), Is.EqualTo(WireFormat.PayloadByteCount));
      Assert.That(cappedBytes.AsSpan(25, 4).ToArray(), Is.EqualTo(oneBelow));
      Assert.That(cappedBytes.AsSpan(29, 4).ToArray(), Is.EqualTo(oneBelow));
      Assert.That(cappedBytes.AsSpan(49, 4).ToArray(), Is.EqualTo(largest));
      var keptBytes = new byte[WireFormat.PayloadByteCount];
      Assert.That(FrameMarker.EncodePayload(kept, default, keptBytes), Is.EqualTo(WireFormat.PayloadByteCount));
      Assert.That(keptBytes.AsSpan(25, 4).ToArray(), Is.EqualTo(oneBelow));
      Assert.That(keptBytes.AsSpan(29, 4).ToArray(), Is.EqualTo(largest));
      Assert.That(keptBytes.AsSpan(49, 4).ToArray(), Is.EqualTo(largest));

      // So a payload decodes to exactly what was encoded, a capped one too
      Assert.That(FrameMarker.TryDecodePayload(cappedBytes, out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(capped));
      Assert.That(FrameMarker.TryDecodePayload(keptBytes, out decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(kept));
    }

    [Test]
    public void ADurationIsNeverNegative_AndAnEmptyPayloadIsAFrameMarkerOfZeros()
    {
      // A negative span is no duration: it becomes zero where the duration is made, before a payload sees it
      var payload = new Payload(
        MarkerKind.Frame,
        1,
        2,
        MarkerFlags.NoFlags,
        new NanosecondTimeSpan(3),
        preferredFrameTime: new NanosecondTimeDuration(new NanosecondTimeSpan(-1)),
        cpuBusy: NanosecondTimeDuration.FromNanoseconds(long.MinValue)
      );
      Assert.That(payload.PreferredFrameTime, Is.EqualTo(NanosecondTimeDuration.Zero));
      Assert.That(payload.CpuBusy, Is.EqualTo(NanosecondTimeDuration.Zero));

      // default(Payload) is valid: a frame marker with every value 0, which encodes and decodes as any other
      Payload empty = default;
      Assert.That(empty, Is.EqualTo(new Payload(MarkerKind.Frame, 0, 0, MarkerFlags.NoFlags, NanosecondTimeSpan.Zero)));
      var bytes = new byte[WireFormat.PayloadByteCount];
      Assert.That(FrameMarker.EncodePayload(empty, default, bytes), Is.EqualTo(WireFormat.PayloadByteCount));
      Assert.That(bytes.AsSpan(WireFormat.OffsetRunId, WireFormat.HeaderByteCount - WireFormat.OffsetRunId).ToArray(), Is.All.Zero);
      Assert.That(FrameMarker.TryDecodePayload(bytes, out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(empty));
    }

    [Test]
    public void OldHeaderLengths_AreRejected()
    {
      var bytes = new byte[Payload.MaxEncodedByteCount + 1];
      int count = FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2)), default, bytes);
      Assert.That(count, Is.EqualTo(57));
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 56), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 58), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, 53), out _, out _), Is.False, "the header alone, as it was before the CRC");
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
          new NanosecondTimeSpan(123),
          targetFrameTime: NanosecondTimeDuration.FromNanoseconds(6),
          intendedDisplayTime: new NanosecondTickCount(5)
        ),
        default,
        bytes
      );
      Assert.That(count, Is.EqualTo(WireFormat.SyncPayloadByteCount));
      // The header's first 16 bytes and their CRC (0xC0A3D4F2 by Python's binascii.crc32)
      Assert.That(count, Is.EqualTo(20));
      Assert.That(
        bytes.AsSpan(0, count).ToArray(),
        Is.EqualTo(new byte[] { (byte)'M', (byte)'F', 1, 3, 4, 0, 0, 0, 8, 7, 6, 5, 4, 3, 2, 1, 0xF2, 0xD4, 0xA3, 0xC0 })
      );
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count), out var decoded, out _), Is.True);
      Assert.That(decoded, Is.EqualTo(new Payload(MarkerKind.Sync, 4, 0x0102030405060708u, MarkerFlags.NoFlags, new NanosecondTimeSpan(0))));
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count + 1), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, count - 1), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.SyncFieldsByteCount), out _, out _), Is.False, "without its CRC");
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
        new NanosecondTimeSpan(20),
        targetFrameTime: NanosecondTimeDuration.FromNanoseconds(50),
        intendedDisplayTime: new NanosecondTickCount(40),
        cpuStartTime: new NanosecondTickCount(60),
        cpuBusy: NanosecondTimeDuration.FromNanoseconds(70)
      );
      var id = new SequenceId(0x0011_2233_4455_6677, 0x8899_AABB_CCDD_EEFF);
      int count = FrameMarker.EncodePayload(payload, new StartMetadata(638_000_000_000_000_000, id), bytes.AsSpan(5));
      Assert.That(count, Is.EqualTo(WireFormat.StartPayloadByteCount));
      Assert.That(count, Is.EqualTo(81));
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
        FrameMarker.EncodePayload(
          new Payload(MarkerKind.Frame, 3, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2)),
          new StartMetadata(5, id),
          bytes
        ),
        Is.EqualTo(WireFormat.PayloadByteCount)
      );
    }

    [Test]
    public void Encode_RejectsSmallBuffers()
    {
      var start = new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2));
      Assert.That(FrameMarker.EncodePayload(start, default, new byte[WireFormat.StartPayloadByteCount - 1]), Is.Zero);
      Assert.That(
        FrameMarker.EncodePayload(
          new Payload(MarkerKind.Frame, 0, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2)),
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
      FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 0, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2)), default, bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.PayloadByteCount - 1), out _, out _), Is.False, "short");
      // Each with the CRC put right, so it is the magic, the format version and the kind that are refused
      bytes[0] = (byte)'X';
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "magic");
      bytes[0] = (byte)'M';
      bytes[1] = (byte)'X';
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "the magic's second byte");
      bytes[1] = (byte)'F';
      bytes[2] = 2;
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "format version");
      bytes[2] = 1;
      bytes[3] = 3;
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "a sync marker is 20 bytes");
      foreach (byte kind in new byte[] { 4, 5, 127, 128, 255 })
      {
        bytes[3] = kind;
        PutCrc(bytes);
        Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False, "an unknown kind: " + kind);
        // The same in a sync marker's 20 bytes: the kind is checked before the length it implies
        Assert.That(
          FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.SyncPayloadByteCount), out _, out _),
          Is.False,
          "sync length, kind " + kind
        );
      }
      bytes[3] = 0;
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.True);

      // A start marker without its metadata block, or with a byte too many
      FrameMarker.EncodePayload(
        new Payload(MarkerKind.SequenceStart, 3, 1, MarkerFlags.NoFlags, new NanosecondTimeSpan(2)),
        default,
        bytes = new byte[WireFormat.StartPayloadByteCount + 1]
      );
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.PayloadByteCount), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, bytes.Length), out _, out _), Is.False);
      Assert.That(FrameMarker.TryDecodePayload(bytes.AsSpan(0, WireFormat.StartPayloadByteCount), out _, out _), Is.True);
    }

    [Test]
    public void TheCrc_IsTheStandardOne()
    {
      // The check value every description of the CRC-32 of zlib, PNG and Ethernet gives
      Assert.That(Crc32.Compute(Encoding.ASCII.GetBytes("123456789")), Is.EqualTo(0xCBF43926u));
      Assert.That(Crc32.Compute(ReadOnlySpan<byte>.Empty), Is.Zero);
      Assert.That(Crc32.Compute(new byte[32]), Is.EqualTo(0x190A55ADu));
      var ones = new byte[32];
      ones.AsSpan().Fill(0xFF);
      Assert.That(Crc32.Compute(ones), Is.EqualTo(0xFF6CAB0Bu));
    }

    [Test]
    public void AChangedBit_IsRefused([Values] MarkerKind kind)
    {
      // Every single bit of the payload, the CRC's own bits too: none decodes, and the payload put back decodes again
      var payload = new Payload(
        kind,
        0x21222324u,
        0x0102030405060708u,
        MarkerFlags.StaticAfter,
        new NanosecondTimeSpan(0x1112131415161718),
        preferredFrameTime: NanosecondTimeDuration.FromNanoseconds(0x71727374),
        targetFrameTime: NanosecondTimeDuration.FromNanoseconds(0x41424344),
        intendedDisplayTime: new NanosecondTickCount(0x3132333435363738),
        cpuStartTime: new NanosecondTickCount(0x5152535455565758),
        cpuBusy: NanosecondTimeDuration.FromNanoseconds(0x61626364)
      );
      Assert.That(SequenceId.TryFromText("a changed bit", out var id), Is.True);
      var buffer = new byte[Payload.MaxEncodedByteCount];
      int count = FrameMarker.EncodePayload(payload, new StartMetadata(638_000_000_000_000_000, id), buffer);
      var bytes = buffer.AsSpan(0, count);
      Assert.That(FrameMarker.TryDecodePayload(bytes, out _, out _), Is.True);
      for (int bit = 0; bit < count * 8; ++bit)
      {
        bytes[bit / 8] ^= (byte)(1 << (bit % 8));
        Assert.That(FrameMarker.TryDecodePayload(bytes, out _, out _), Is.False, "bit " + bit);
        bytes[bit / 8] ^= (byte)(1 << (bit % 8));
      }
      Assert.That(FrameMarker.TryDecodePayload(bytes, out _, out _), Is.True);
    }

    [Test]
    public void AFieldChangedWithoutItsCrc_IsRefused()
    {
      // What a QR decoder's error correction can hand back for a symbol that mixes two frames: a well-formed payload of bytes that were
      // never drawn. Only the CRC tells
      var bytes = new byte[WireFormat.PayloadByteCount];
      FrameMarker.EncodePayload(new Payload(MarkerKind.Frame, 7, 1000, MarkerFlags.NoFlags, new NanosecondTimeSpan(16_666_667)), default, bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes, out _, out _), Is.True);
      bytes[WireFormat.OffsetFrameIndex] = 0xE9;
      Assert.That(FrameMarker.TryDecodePayload(bytes, out _, out _), Is.False);
      PutCrc(bytes);
      Assert.That(FrameMarker.TryDecodePayload(bytes, out var decoded, out _), Is.True);
      Assert.That(decoded.FrameIndex, Is.EqualTo(1001ul));
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
