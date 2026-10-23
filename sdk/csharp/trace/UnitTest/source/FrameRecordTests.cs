//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The frame record (sdk/doc/trace-frame-record.md): its strings, a slice's name written and read back, and the record of a payload. The
//* strings are spelled out here as the document has them: they are what applications and readers of a trace agree on.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Marker;
using NUnit.Framework;

namespace MB.FramePacing.Trace.UnitTest
{
  [TestFixture]
  public class FrameRecordTests
  {
    private static Payload Frame(
      long cpuStartNs,
      long cpuBusyNs = 0,
      long animationNs = 0,
      long preferredNs = 0,
      long targetNs = 0,
      long intendedDisplayNs = 0,
      long refreshPeriodNs = 0,
      MarkerKind kind = MarkerKind.Frame
    ) =>
      new Payload(
        kind,
        7,
        200,
        MarkerFlags.NoFlags,
        NanosecondTimeSpan.FromNanoseconds(animationNs),
        NanosecondTimeDuration.FromNanoseconds(preferredNs),
        NanosecondTimeDuration.FromNanoseconds(targetNs),
        NanosecondTimeDuration.FromNanoseconds(refreshPeriodNs),
        NanosecondTickCount.FromNanoseconds(intendedDisplayNs),
        NanosecondTickCount.FromNanoseconds(cpuStartNs),
        NanosecondTimeDuration.FromNanoseconds(cpuBusyNs)
      );

    [Test]
    public void TheStrings_AreTheDocuments()
    {
      Assert.That(FrameRecordText.Category, Is.EqualTo("mbfp"));
      Assert.That(FrameRecordText.TrackName, Is.EqualTo("mbfp frames"));
      Assert.That(FrameRecordText.NamePrefix, Is.EqualTo("mbfp frame "));
      Assert.That(FrameRecordText.AnimationNs, Is.EqualTo("animationNs"));
      Assert.That(FrameRecordText.MarkerPreferredNs, Is.EqualTo("markerPreferredNs"));
      Assert.That(FrameRecordText.MarkerTargetNs, Is.EqualTo("markerTargetNs"));
      Assert.That(FrameRecordText.MarkerRefreshPeriodNs, Is.EqualTo("markerRefreshPeriodNs"));
      Assert.That(FrameRecordText.IntendedDisplayNs, Is.EqualTo("intendedDisplayNs"));
      Assert.That(FrameRecordText.ClockId, Is.EqualTo(3));
    }

    [TestCase(0u, 0ul, "mbfp frame 0 0")]
    [TestCase(1u, 1234ul, "mbfp frame 1 1234")]
    [TestCase(10u, 100ul, "mbfp frame 10 100")]
    [TestCase(uint.MaxValue, ulong.MaxValue, "mbfp frame 4294967295 18446744073709551615")]
    public void AName_IsWrittenAndReadBack(uint runId, ulong frameIndex, string expected)
    {
      Assert.That(FrameRecordName.Format(runId, frameIndex), Is.EqualTo(expected));
      Assert.That(expected.Length, Is.LessThanOrEqualTo(FrameRecordName.MaxLength));

      Span<char> exact = stackalloc char[expected.Length];
      Assert.That(FrameRecordName.TryFormat(runId, frameIndex, exact, out int written), Is.True);
      Assert.That((written, new string(exact)), Is.EqualTo((expected.Length, expected)));

      Assert.That(FrameRecordName.TryParse(expected, out uint parsedRun, out ulong parsedFrame), Is.True);
      Assert.That((parsedRun, parsedFrame), Is.EqualTo((runId, frameIndex)));
    }

    [Test]
    public void TheLongestName_IsMaxLength()
    {
      Assert.That(FrameRecordName.Format(uint.MaxValue, ulong.MaxValue).Length, Is.EqualTo(FrameRecordName.MaxLength));
    }

    [Test]
    public void ANameThatDoesNotFit_WritesNothing()
    {
      var small = new char[13];
      Array.Fill(small, '#');

      Assert.That(FrameRecordName.TryFormat(0, 0, small, out int written), Is.False);

      Assert.That(written, Is.Zero);
      Assert.That(new string(small), Is.EqualTo(new string('#', 13)));
    }

    [TestCase("mbfp frame 7")]
    [TestCase("mbfp frame 7 ")]
    [TestCase("mbfp frame  7")]
    [TestCase("mbfp frame 7 -1")]
    [TestCase("mbfp frame +7 1")]
    [TestCase("mbfp frame a 1")]
    [TestCase("mbfp frame 7 1 2")]
    [TestCase("mbfp frame 4294967296 1")]
    [TestCase("mbfp frame 1 18446744073709551616")]
    [TestCase("frame 7 1")]
    [TestCase("MBFP frame 7 1")]
    [TestCase("")]
    public void ANameThatIsNotAFrames_IsRefused(string name)
    {
      Assert.That(FrameRecordName.TryParse(name, out _, out _), Is.False);
    }

    [Test]
    public void ARecord_HasTheMarkersValues()
    {
      var payload = Frame(1_000, 400, animationNs: -5, preferredNs: 16, targetNs: 33, intendedDisplayNs: 2_000, refreshPeriodNs: 8);

      Assert.That(FrameRecord.TryCreate(payload, out var record), Is.True);

      Assert.That((record.RunId, record.FrameIndex), Is.EqualTo((7u, 200ul)));
      Assert.That(record.Name, Is.EqualTo("mbfp frame 7 200"));
      Assert.That(record.ToString(), Is.EqualTo("mbfp frame 7 200"));
      Assert.That((record.StartTime.Nanoseconds, record.Duration.Nanoseconds, record.EndTime.Nanoseconds), Is.EqualTo((1_000L, 400L, 1_400L)));
      Assert.That(record.AnnotationCount, Is.EqualTo(FrameRecord.MaxAnnotationCount));
      Assert.That(record.GetAnnotation(0), Is.EqualTo(new FrameRecordAnnotation("animationNs", -5)));
      Assert.That(record.GetAnnotation(1), Is.EqualTo(new FrameRecordAnnotation("markerPreferredNs", 16)));
      Assert.That(record.GetAnnotation(2), Is.EqualTo(new FrameRecordAnnotation("markerTargetNs", 33)));
      Assert.That(record.GetAnnotation(3), Is.EqualTo(new FrameRecordAnnotation("markerRefreshPeriodNs", 8)));
      Assert.That(record.GetAnnotation(4), Is.EqualTo(new FrameRecordAnnotation("intendedDisplayNs", 2_000)));
      Assert.Throws<ArgumentOutOfRangeException>(() => Annotation(payload, 5));
      Assert.Throws<ArgumentOutOfRangeException>(() => Annotation(payload, -1));

      Span<char> name = stackalloc char[FrameRecordName.MaxLength];
      Assert.That(record.TryFormatName(name, out int length), Is.True);
      Assert.That(new string(name.Slice(0, length)), Is.EqualTo(record.Name));
    }

    [Test]
    public void WhatTheMarkerDoesNotKnow_HasNoAnnotation()
    {
      Assert.That(FrameRecord.TryCreate(Frame(1_000), out var bare), Is.True);
      Assert.That(bare.AnnotationCount, Is.EqualTo(1));
      Assert.That(bare.GetAnnotation(0), Is.EqualTo(new FrameRecordAnnotation("animationNs", 0)));
      Assert.Throws<ArgumentOutOfRangeException>(() => Annotation(Frame(1_000), 1));

      // Each of the other four alone: the second annotation is that one
      Assert.That(Annotation(Frame(1_000, preferredNs: 16), 1), Is.EqualTo(new FrameRecordAnnotation("markerPreferredNs", 16)));
      Assert.That(Annotation(Frame(1_000, targetNs: 33), 1), Is.EqualTo(new FrameRecordAnnotation("markerTargetNs", 33)));
      Assert.That(Annotation(Frame(1_000, refreshPeriodNs: 8), 1), Is.EqualTo(new FrameRecordAnnotation("markerRefreshPeriodNs", 8)));
      Assert.That(Annotation(Frame(1_000, intendedDisplayNs: -2), 1), Is.EqualTo(new FrameRecordAnnotation("intendedDisplayNs", -2)));
      // Two of them: in the order of the marker's fields
      var two = Frame(1_000, targetNs: 33, intendedDisplayNs: 9);
      Assert.That(Annotation(two, 1).Name, Is.EqualTo("markerTargetNs"));
      Assert.That(Annotation(two, 2).Name, Is.EqualTo("intendedDisplayNs"));
      Assert.Throws<ArgumentOutOfRangeException>(() => Annotation(two, 3));
    }

    [Test]
    public void TheValues_AreThePayloadsAfterItsCap()
    {
      var payload = Frame(1, cpuBusyNs: 5_000_000_000, preferredNs: 5_000_000_000, targetNs: 4_294_967_295, refreshPeriodNs: 5_000_000_000);

      Assert.That(FrameRecord.TryCreate(payload, out var record), Is.True);

      Assert.That(record.Duration, Is.EqualTo(Payload.MaxCpuBusy));
      Assert.That(record.GetAnnotation(1).Value, Is.EqualTo(Payload.MaxFrameTime.Nanoseconds));
      Assert.That(record.GetAnnotation(2).Value, Is.EqualTo(Payload.OnDemandFrameTime.Nanoseconds));
      Assert.That(record.GetAnnotation(3).Value, Is.EqualTo(Payload.MaxRefreshPeriod.Nanoseconds));
    }

    [TestCase(MarkerKind.Frame, true)]
    [TestCase(MarkerKind.SequenceStart, true)]
    [TestCase(MarkerKind.SequenceEnd, true)]
    [TestCase(MarkerKind.Sync, false)]
    public void EveryMainMarker_GetsARecord_ASyncMarkerNone(MarkerKind kind, bool expected)
    {
      Assert.That(FrameRecord.TryCreate(Frame(1_000, kind: kind), out var record), Is.EqualTo(expected));
      Assert.That(record == default, Is.EqualTo(!expected));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(long.MinValue)]
    public void ACpuStartTimeThatIsNotAfterTheClocksStart_GetsNoRecord(long cpuStartNs)
    {
      Assert.That(FrameRecord.TryCreate(Frame(cpuStartNs, 400), out var record), Is.False);
      Assert.That(record, Is.EqualTo(default(FrameRecord)));
    }

    [Test]
    public void Records_CompareByTheirValues()
    {
      Assert.That(FrameRecord.TryCreate(Frame(1_000, 400, 5, 16, 33, 2_000), out var record), Is.True);
      Assert.That(FrameRecord.TryCreate(Frame(1_000, 400, 5, 16, 33, 2_000), out var same), Is.True);

      Assert.That(record == same, Is.True);
      Assert.That(record != same, Is.False);
      Assert.That(record.Equals((object)same), Is.True);
      Assert.That(record.Equals("mbfp frame 7 200"), Is.False);
      Assert.That(record.GetHashCode(), Is.EqualTo(same.GetHashCode()));
      foreach (
        var other in new[]
        {
          Frame(1_001, 400, 5, 16, 33, 2_000),
          Frame(1_000, 401, 5, 16, 33, 2_000),
          Frame(1_000, 400, 6, 16, 33, 2_000),
          Frame(1_000, 400, 5, 17, 33, 2_000),
          Frame(1_000, 400, 5, 16, 34, 2_000),
          Frame(1_000, 400, 5, 16, 33, 2_001),
          Frame(1_000, 400, 5, 16, 33, 2_000, 8),
          new Payload(MarkerKind.Frame, 8, 200, MarkerFlags.NoFlags, default, cpuStartTime: NanosecondTickCount.FromNanoseconds(1_000)),
          new Payload(MarkerKind.Frame, 7, 201, MarkerFlags.NoFlags, default, cpuStartTime: NanosecondTickCount.FromNanoseconds(1_000)),
        }
      )
      {
        Assert.That(FrameRecord.TryCreate(other, out var different), Is.True);
        Assert.That(record != different, Is.True, different.ToString());
      }
    }

    [Test]
    public void Annotations_CompareByNameAndValue()
    {
      var annotation = new FrameRecordAnnotation("animationNs", 5);

      Assert.That(annotation == new FrameRecordAnnotation("animationNs", 5), Is.True);
      Assert.That(annotation != new FrameRecordAnnotation("animationNs", 6), Is.True);
      Assert.That(annotation != new FrameRecordAnnotation("markerTargetNs", 5), Is.True);
      Assert.That(annotation.Equals((object)new FrameRecordAnnotation("animationNs", 5)), Is.True);
      Assert.That(annotation.Equals("animationNs=5"), Is.False);
      Assert.That(annotation.GetHashCode(), Is.EqualTo(new FrameRecordAnnotation("animationNs", 5).GetHashCode()));
      Assert.That(annotation.ToString(), Is.EqualTo("animationNs=5"));
    }

    [Test]
    public void ARecordInAFrameLoop_AllocatesNothing()
    {
      var payload = Frame(1_000, 400, 5, 16, 33, 2_000);
      var name = new char[FrameRecordName.MaxLength];
      // Once before counting: what the runtime sets up on a first call is not the frame loop's
      Log(payload, name);
      long before = GC.GetAllocatedBytesForCurrentThread();

      long sum = 0;
      for (int i = 0; i < 1_000; ++i)
        sum += Log(payload, name);

      Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
      Assert.That(sum, Is.GreaterThan(0));
    }

    private static long Log(in Payload payload, char[] name)
    {
      FrameRecord.TryCreate(payload, out var record);
      record.TryFormatName(name, out int length);
      long sum = length + record.StartTime.Nanoseconds + record.Duration.Nanoseconds;
      for (int i = 0; i < record.AnnotationCount; ++i)
        sum += record.GetAnnotation(i).Value;
      return sum;
    }

    private static FrameRecordAnnotation Annotation(in Payload payload, int index)
    {
      FrameRecord.TryCreate(payload, out var record);
      return record.GetAnnotation(index);
    }
  }
}
