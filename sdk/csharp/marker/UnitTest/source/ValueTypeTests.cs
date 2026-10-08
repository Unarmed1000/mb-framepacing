//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker's value types compare by value, hash alike when equal and write themselves: Payload, MarkerQuad, Vertex, SequenceId and
//* Options. Payload also keeps itself valid: a kind that is not a MarkerKind throws (PayloadTests has the durations it caps).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class ValueTypeTests
  {
    private static readonly Payload g_payload = new Payload(
      MarkerKind.Frame,
      1,
      2,
      MarkerFlags.StaticAfter,
      new NanosecondTimeSpan(3),
      NanosecondTimeDuration.FromNanoseconds(4),
      NanosecondTimeDuration.FromNanoseconds(5),
      new NanosecondTickCount(6),
      new NanosecondTickCount(7),
      NanosecondTimeDuration.FromNanoseconds(8)
    );

    [Test]
    public void Payload_AKindThatIsNotAMarkerKindThrows()
    {
      const MarkerKind Unknown = (MarkerKind)4;
      Assert.That(() => new Payload(Unknown, 1, 2, MarkerFlags.NoFlags, new NanosecondTimeSpan(3)), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => g_payload.WithKind(Unknown), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(
        () => new Payload((MarkerKind)255, 1, 2, MarkerFlags.NoFlags, NanosecondTimeSpan.Zero),
        Throws.TypeOf<ArgumentOutOfRangeException>()
      );
      // Every MarkerKind is taken, and a default payload is a frame marker
      foreach (MarkerKind kind in Enum.GetValues(typeof(MarkerKind)))
        Assert.That(g_payload.WithKind(kind).Kind, Is.EqualTo(kind));
      Assert.That(default(Payload).Kind, Is.EqualTo(MarkerKind.Frame));
    }

    [Test]
    public void Payload_WithKindKeepsEveryOtherValue()
    {
      Payload end = g_payload.WithKind(MarkerKind.SequenceEnd);
      Assert.That(
        end,
        Is.EqualTo(
          new Payload(
            MarkerKind.SequenceEnd,
            1,
            2,
            MarkerFlags.StaticAfter,
            new NanosecondTimeSpan(3),
            NanosecondTimeDuration.FromNanoseconds(4),
            NanosecondTimeDuration.FromNanoseconds(5),
            new NanosecondTickCount(6),
            new NanosecondTickCount(7),
            NanosecondTimeDuration.FromNanoseconds(8)
          )
        )
      );
      Assert.That(g_payload.WithKind(MarkerKind.Frame), Is.EqualTo(g_payload));
    }

    [Test]
    public void Payload_TheFlagsAreKeptAsGiven()
    {
      var reserved = (MarkerFlags)0xFF;
      Assert.That(new Payload(MarkerKind.Frame, 1, 2, reserved, NanosecondTimeSpan.Zero).Flags, Is.EqualTo(reserved));
    }

    [Test]
    public void Payload_OnDemandIsTheLargestValueOfAFrameTimesFourBytes_AndTheStartMarkerTheLongestPayload()
    {
      Assert.That(Payload.OnDemandFrameTime.Nanoseconds, Is.EqualTo(uint.MaxValue));
      Assert.That(Payload.MaxFrameTime.Nanoseconds, Is.EqualTo(uint.MaxValue - 1));
      Assert.That(Payload.MaxCpuBusy.Nanoseconds, Is.EqualTo(uint.MaxValue));
      Assert.That(Payload.MaxEncodedByteCount, Is.EqualTo(WireFormat.StartPayloadByteCount));
      Assert.That(Payload.MaxEncodedByteCount, Is.LessThanOrEqualTo(WireFormat.QrCapacityBytes));
      Assert.That(WireFormat.SyncPayloadByteCount, Is.LessThanOrEqualTo(WireFormat.SyncQrCapacityBytes));
    }

    [Test]
    public void Payload_ComparesEveryField()
    {
      Payload p = g_payload;
      Assert.That(p.Equals(g_payload), Is.True);
      Assert.That(p == g_payload, Is.True);
      Assert.That(p != g_payload, Is.False);
      Assert.That(p.Equals((object)g_payload), Is.True);
      Assert.That(p.Equals("payload"), Is.False);
      Assert.That(p.GetHashCode(), Is.EqualTo(g_payload.GetHashCode()));
      var others = new[]
      {
        p.WithKind(MarkerKind.SequenceEnd),
        new Payload(
          p.Kind,
          9,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          9,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          MarkerFlags.NoFlags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          new NanosecondTimeSpan(9),
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          NanosecondTimeDuration.FromNanoseconds(9),
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          NanosecondTimeDuration.FromNanoseconds(9),
          p.IntendedDisplayTime,
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          new NanosecondTickCount(9),
          p.CpuStartTime,
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          new NanosecondTickCount(9),
          p.CpuBusy
        ),
        new Payload(
          p.Kind,
          p.RunId,
          p.FrameIndex,
          p.Flags,
          p.AnimationTime,
          p.PreferredFrameTime,
          p.TargetFrameTime,
          p.IntendedDisplayTime,
          p.CpuStartTime,
          NanosecondTimeDuration.FromNanoseconds(9)
        ),
      };
      foreach (Payload other in others)
      {
        Assert.That(p.Equals(other), Is.False, other.ToString());
        Assert.That(p != other, Is.True, other.ToString());
        Assert.That(p.GetHashCode(), Is.Not.EqualTo(other.GetHashCode()), other.ToString());
      }
    }

    [Test]
    public void Payload_WritesEveryFieldWithItsTimesInNanoseconds()
    {
      Assert.That(
        g_payload.ToString(),
        Is.EqualTo(
          "{Frame, run 1, frame 2, flags StaticAfter, animation 3 ns, preferred 4 ns, target 5 ns, intended 6 ns, cpu start 7 ns, cpu busy 8 ns}"
        )
      );
    }

    [Test]
    public void MarkerQuad_ComparesByValue()
    {
      var quad = new MarkerQuad(new Rectangle(1, 2, 3, 4), true);
      Assert.That(quad == new MarkerQuad(new Rectangle(1, 2, 3, 4), true), Is.True);
      Assert.That(quad != new MarkerQuad(new Rectangle(1, 2, 3, 4), false), Is.True);
      Assert.That(quad.Equals(new MarkerQuad(new Rectangle(1, 2, 3, 5), true)), Is.False);
      Assert.That(quad.Equals((object)new MarkerQuad(new Rectangle(1, 2, 3, 4), true)), Is.True);
      Assert.That(quad.Equals("quad"), Is.False);
      Assert.That(quad.GetHashCode(), Is.EqualTo(new MarkerQuad(new Rectangle(1, 2, 3, 4), true).GetHashCode()));
      Assert.That(quad.GetHashCode(), Is.Not.EqualTo(new MarkerQuad(new Rectangle(1, 2, 3, 4), false).GetHashCode()));
      Assert.That(quad.ToString(), Is.EqualTo("{1,2,3,4 dark}"));
      Assert.That(new MarkerQuad(new Rectangle(1, 2, 3, 4), false).ToString(), Is.EqualTo("{1,2,3,4 light}"));
    }

    [Test]
    public void Vertex_ComparesByValue()
    {
      var vertex = new Vertex(1, 2, 255);
      Assert.That(vertex == new Vertex(1, 2, 255), Is.True);
      Assert.That(vertex != new Vertex(1, 2, 0), Is.True);
      Assert.That(vertex.Equals(new Vertex(9, 2, 255)), Is.False);
      Assert.That(vertex.Equals(new Vertex(1, 9, 255)), Is.False);
      Assert.That(vertex.Equals((object)new Vertex(1, 2, 255)), Is.True);
      Assert.That(vertex.Equals("vertex"), Is.False);
      Assert.That(vertex.GetHashCode(), Is.EqualTo(new Vertex(1, 2, 255).GetHashCode()));
      Assert.That(vertex.GetHashCode(), Is.Not.EqualTo(new Vertex(1, 2, 0).GetHashCode()));
      Assert.That(vertex.ToString(), Is.EqualTo("{1,2 luma 255}"));
    }

    [Test]
    public void SequenceId_ComparesByValue_AndNeedsSixteenBytes()
    {
      var id = new SequenceId(0x0102_0304_0506_0708, 0x090A_0B0C_0D0E_0F10);
      Assert.That((id.High, id.Low), Is.EqualTo((0x0102_0304_0506_0708UL, 0x090A_0B0C_0D0E_0F10UL)));
      Assert.That(id == new SequenceId(0x0102_0304_0506_0708, 0x090A_0B0C_0D0E_0F10), Is.True);
      Assert.That(id != new SequenceId(0x0102_0304_0506_0708, 1), Is.True);
      Assert.That(id.Equals(new SequenceId(1, 0x090A_0B0C_0D0E_0F10)), Is.False);
      Assert.That(id.Equals((object)new SequenceId(0x0102_0304_0506_0708, 0x090A_0B0C_0D0E_0F10)), Is.True);
      Assert.That(id.Equals("id"), Is.False);
      Assert.That(id.GetHashCode(), Is.EqualTo(new SequenceId(0x0102_0304_0506_0708, 0x090A_0B0C_0D0E_0F10).GetHashCode()));
      Assert.That(id.GetHashCode(), Is.Not.EqualTo(new SequenceId(0x0102_0304_0506_0708, 1).GetHashCode()));

      var bytes = new byte[SequenceId.ByteCount];
      Assert.That(id.TryCopyTo(bytes), Is.True);
      Assert.That(SequenceId.FromBytes(bytes), Is.EqualTo(id));
      Assert.That(id.TryCopyTo(new byte[SequenceId.ByteCount - 1]), Is.False);
      Assert.That(() => SequenceId.FromBytes(new byte[SequenceId.ByteCount - 1]), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Options_ComparesByValue()
    {
      var options = new Options(3, 2);
      Assert.That(options == new Options(3, 2), Is.True);
      Assert.That(options != new Options(3, 4), Is.True);
      Assert.That(options.Equals(new Options(4, 2)), Is.False);
      Assert.That(options.Equals((object)new Options(3, 2)), Is.True);
      Assert.That(options.Equals("options"), Is.False);
      Assert.That(options.GetHashCode(), Is.EqualTo(new Options(3, 2).GetHashCode()));
      Assert.That(options.GetHashCode(), Is.Not.EqualTo(new Options(3, 4).GetHashCode()));
    }
  }
}
