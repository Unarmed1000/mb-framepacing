//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The time types are used every frame, so no operation that does not throw may allocate (a Unity game would see it as garbage collector
//* spikes). The same loop as the C++ core's allocation test.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using NUnit.Framework;

namespace MB.FramePacing.UnitTest
{
  [TestFixture]
  public class AllocationTests
  {
    [Test]
    public void TheTimeTypes_DoNotAllocate()
    {
      long sum = RunFrames(10); // warm up (JIT)
      long before = GC.GetAllocatedBytesForCurrentThread();
      sum += RunFrames(1000);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.That(allocated, Is.Zero);
      Assert.That(sum, Is.Positive, "the calls must actually have produced output");
    }

    private static long RunFrames(int frames)
    {
      long sum = 0;
      var now = new TickCount64(123_456_789);
      TickCount32 now32 = TickCount32.FromTickCount64(now);
      for (int frame = 0; frame < frames; ++frame)
      {
        TimeSpan step = new TimeSpan(166_670) + new TimeSpan(frame % 3);
        TickCount64 previous = now;
        TickCount32 previous32 = now32;
        now += step;
        now32 += step;
        TimeSpan elapsed = now - previous;
        TimeSpan elapsed32 = now32 - previous32;
        TimeSpan32 busy = TimeSpan32.FromTimeSpan(new TimeSpan(elapsed.Ticks / 2));
        sum += elapsed.Ticks + elapsed32.Ticks + busy.ToTimeSpan().Ticks + (now32 > previous32 ? 1 : 0) + (now > previous ? 1 : 0);
        sum += (long)(now.TotalSeconds + now32.TotalMicroseconds) + (busy < TimeSpan32.MaxValue ? 1 : 0) + (busy == TimeSpan32.Zero ? 1 : 0);
        sum += now.Days + now.Milliseconds + now32.Milliseconds + now.GetHashCode() % 2 + (now.Equals(previous) ? 1 : 0);
        sum += TickCount64.FromMilliseconds(frame).Ticks + TickCount32.FromMilliseconds(frame % 1000).Ticks;
        sum += TickCount64.FromNanoseconds(frame * 150L).Ticks + TickCount64.FromCounter(frame * 3L, 3_000_000_000).Ticks;
      }
      return sum;
    }
  }
}
