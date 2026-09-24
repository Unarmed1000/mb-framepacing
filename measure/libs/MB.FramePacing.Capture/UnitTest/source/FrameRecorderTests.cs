//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Recorder behaviour: ordering, drops when the ring is full, late device timestamps and armed pre-roll.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class FrameRecorderTests
  {
    private static readonly CaptureFileHeader g_header = new CaptureFileHeader(8, 4, FrameRate.FromFps(500));

    /// <summary>Blocks the writer thread inside TryGetDeviceTicks until released, which makes a full ring deterministic.</summary>
    private sealed class GatedTimestamps : IDeviceTimestampSource
    {
      private readonly ManualResetEventSlim m_gate = new ManualResetEventSlim(false);

      public void Release() => m_gate.Set();

      public bool TryGetDeviceTicks(long captureIndex, out long deviceTicks)
      {
        m_gate.Wait();
        deviceTicks = captureIndex * 10;
        return true;
      }
    }

    private sealed class DictionaryTimestamps : IDeviceTimestampSource
    {
      public readonly Dictionary<long, long> Values = new Dictionary<long, long>();

      public bool TryGetDeviceTicks(long captureIndex, out long deviceTicks)
      {
        lock (Values)
          return Values.TryGetValue(captureIndex, out deviceTicks);
      }
    }

    private static void Produce(FrameRecorder recorder, long count, long deviceTicks)
    {
      for (long i = 0; i < count; ++i)
      {
        var pixels = recorder.BeginFrame();
        pixels.Fill((byte)(i & 0xFF));
        recorder.EndFrame(i * 100, deviceTicks == DeviceTimestamps.PendingTicks ? deviceTicks : i * 7, CaptureRecordFlags.None);
      }
    }

    [Test]
    public void WritesEveryFrameInOrder()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      using (var writer = new CaptureFileWriter(path, g_header))
      using (var recorder = new FrameRecorder(writer, new FrameRecorderOptions { RingFrames = 8 }, new CaptureClock()))
      {
        for (int i = 0; i < 1000; ++i)
        {
          // Keep the ring from overflowing so every frame must arrive
          while (recorder.Stats.RingFill >= 7)
            Thread.Sleep(0);
          var pixels = recorder.BeginFrame();
          pixels.Fill((byte)i);
          recorder.EndFrame(i, i * 3, CaptureRecordFlags.None);
        }
        recorder.Complete();
        Assert.That(recorder.Stats.FramesDropped, Is.Zero);
      }

      using var reader = new CaptureFileReader(path);
      Assert.That(reader.RecordCount, Is.EqualTo(1000));
      var image = reader.CreateFrameImage();
      for (int i = 0; i < 1000; ++i)
      {
        var header = reader.ReadRecord(i, image);
        Assert.That(header.CaptureIndex, Is.EqualTo(i));
        Assert.That(header.DeviceTicks, Is.EqualTo(i * 3));
        Assert.That(image[0, 0], Is.EqualTo((byte)i));
      }
    }

    [Test]
    public void FullRing_DropsFrames_ButCaptureIndexKeepsCounting()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var gate = new GatedTimestamps();
      const int RingFrames = 16;
      using (var writer = new CaptureFileWriter(path, g_header))
      using (var recorder = new FrameRecorder(writer, new FrameRecorderOptions { RingFrames = RingFrames }, new CaptureClock(), gate))
      {
        Produce(recorder, RingFrames + 5, DeviceTimestamps.PendingTicks);
        Assert.That(recorder.Stats.FramesDropped, Is.EqualTo(5));
        gate.Release();
        recorder.Complete();
        Assert.That(recorder.Stats.FramesCaptured, Is.EqualTo(RingFrames + 5));
      }

      using var reader = new CaptureFileReader(path);
      Assert.That(reader.RecordCount, Is.EqualTo(RingFrames));
      for (int i = 0; i < RingFrames; ++i)
      {
        var header = reader.ReadRecordHeader(i);
        Assert.That(header.CaptureIndex, Is.EqualTo(i));
        Assert.That(header.DeviceTicks, Is.EqualTo(i * 10), "late device timestamp resolved by the writer");
      }
    }

    [Test]
    public void LateDeviceTicks_AreFilledIn_OrMarkedUnknownAfterTheWait()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var timestamps = new DictionaryTimestamps();
      lock (timestamps.Values)
      {
        for (int i = 0; i < 10; i += 2)
          timestamps.Values[i] = 1_000_000 + i;
      }
      var options = new FrameRecorderOptions { RingFrames = 32, DeviceTicksWait = TimeSpan.FromMilliseconds(30) };
      using (var writer = new CaptureFileWriter(path, g_header))
      using (var recorder = new FrameRecorder(writer, options, new CaptureClock(), timestamps))
      {
        var clock = new CaptureClock();
        for (int i = 0; i < 10; ++i)
        {
          recorder.BeginFrame();
          recorder.EndFrame(clock.NowTicks, DeviceTimestamps.PendingTicks, CaptureRecordFlags.None);
        }
        recorder.Complete();
      }

      using var reader = new CaptureFileReader(path);
      for (int i = 0; i < 10; ++i)
      {
        var header = reader.ReadRecordHeader(i);
        if (i % 2 == 0)
          Assert.That(header.DeviceTicks, Is.EqualTo(1_000_000 + i));
        else
          Assert.That(header.HasDeviceTicks, Is.False);
      }
    }

    [Test]
    public void Armed_KeepsOnlyThePreRoll_ThenWritesEverything()
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var options = new FrameRecorderOptions
      {
        RingFrames = 64,
        StartArmed = true,
        PreRollFrames = 10,
      };
      using (var writer = new CaptureFileWriter(path, g_header))
      using (var recorder = new FrameRecorder(writer, options, new CaptureClock()))
      {
        for (int i = 0; i < 50; ++i)
        {
          recorder.BeginFrame();
          recorder.EndFrame(i, i, CaptureRecordFlags.None);
          // Give the writer a chance to trim so the ring never fills
          if (i % 8 == 0)
            Thread.Sleep(5);
        }
        // Wait until the writer has trimmed down to the pre-roll
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (recorder.Stats.RingFill > 10 && DateTime.UtcNow < deadline)
          Thread.Sleep(1);

        recorder.StartWriting();
        for (int i = 50; i < 60; ++i)
        {
          recorder.BeginFrame();
          recorder.EndFrame(i, i, CaptureRecordFlags.None);
        }
        recorder.Complete();
        Assert.That(recorder.Stats.FramesDropped, Is.Zero);
        Assert.That(recorder.Stats.FramesDiscardedWhileArmed, Is.EqualTo(40));
      }

      using var reader = new CaptureFileReader(path);
      Assert.That(reader.RecordCount, Is.EqualTo(20));
      Assert.That(reader.ReadRecordHeader(0).CaptureIndex, Is.EqualTo(40), "the pre-roll starts 10 frames before the trigger");
      Assert.That(reader.ReadRecordHeader(19).CaptureIndex, Is.EqualTo(59));
    }

    /// <summary>Reports a start marker in exactly one frame and an end marker in exactly one later frame; records what it was shown.</summary>
    private sealed class OneFrameMarkers : IFrameInspector
    {
      private readonly long m_startIndex;
      private readonly long m_endIndex;
      public readonly List<long> Inspected = new List<long>();

      public OneFrameMarkers(long startIndex, long endIndex)
      {
        m_startIndex = startIndex;
        m_endIndex = endIndex;
      }

      public FrameTrigger Inspect(Marker.GrayImage frame, long captureIndex)
      {
        Inspected.Add(captureIndex);
        // The pixels are the frame's own (Produce fills them with the low byte of the index)
        Assert.That(frame.Pixels[0], Is.EqualTo((byte)(captureIndex & 0xFF)));
        if (captureIndex == m_startIndex)
          return FrameTrigger.Start;
        return captureIndex == m_endIndex ? FrameTrigger.End : FrameTrigger.None;
      }
    }

    [TestCase(true, TestName = "Inspector_OneFrameStartAndEnd_Trigger(waiting source, like a file)")]
    [TestCase(false, TestName = "Inspector_OneFrameStartAndEnd_Trigger(live source)")]
    public void Inspector_OneFrameStartAndEnd_Trigger(bool waitWhenFull)
    {
      using var temp = new TempDirectory();
      var path = temp.File("frames.mbfc");
      var inspector = new OneFrameMarkers(startIndex: 500, endIndex: 700);
      var options = new FrameRecorderOptions
      {
        RingFrames = 64,
        StartArmed = true,
        PreRollFrames = 10,
        Inspector = inspector,
        StopAtEnd = true,
        EndTailFrames = 5,
        WaitWhenFull = waitWhenFull,
      };
      using (var writer = new CaptureFileWriter(path, g_header))
      using (var recorder = new FrameRecorder(writer, options, new CaptureClock()))
      {
        // As fast as possible: far faster than real time, like a video file
        for (long i = 0; i < 1000; ++i)
        {
          var pixels = recorder.BeginFrame();
          pixels.Fill((byte)(i & 0xFF));
          recorder.EndFrame(i * 100, i * 7, CaptureRecordFlags.None);
          // A live source cannot run ahead of the inspector without dropping; give it the time real frames would take
          if (!waitWhenFull && i % 16 == 0)
            Thread.Sleep(1);
        }
        recorder.Complete();
        Assert.That(recorder.StopRequested, Is.True);
        if (waitWhenFull)
        {
          Assert.That(recorder.Stats.FramesDropped, Is.Zero);
          Assert.That(inspector.Inspected, Is.EqualTo(Enumerable.Range(0, 1000).Select(i => (long)i)), "every frame inspected, in order");
        }
      }

      using var reader = new CaptureFileReader(path);
      var written = Enumerable.Range(0, (int)reader.RecordCount).Select(i => reader.ReadRecordHeader(i).CaptureIndex).ToList();
      Assert.That(written, Does.Contain(500L), "the single start marker frame is recorded");
      Assert.That(written, Does.Contain(700L), "the single end marker frame is recorded");
      if (waitWhenFull)
      {
        Assert.That(written.First(), Is.EqualTo(490), "10 frames of pre-roll");
        Assert.That(written.Last(), Is.EqualTo(705), "5 frames of end tail, nothing after it");
        Assert.That(written, Is.EqualTo(Enumerable.Range(490, 216).Select(i => (long)i)));
      }
    }
  }
}
