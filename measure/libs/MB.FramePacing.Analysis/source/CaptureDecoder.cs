//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a frames.mbfc capture and produces one CaptureRow per capture index:
//*  1. Locate: search sampled frames for every marker (the top one times the frame, lower ones detect tearing) and lock onto them.
//*  2. Decode: decode every record in parallel with the locked decoder.
//*  3. Fill the capture index gaps left by recorder drops with NotRecorded rows.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Capture;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  public static class CaptureDecoder
  {
    public const int LocateSampleCount = 240;

    public static DecodedCapture Decode(
      CaptureFileReader reader,
      TimeSource timeSource = TimeSource.Auto,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default
    )
    {
      var layout = Locate(reader, cancellationToken);
      var effectiveTime = timeSource == TimeSource.Auto ? (AllRecordsHaveDeviceTicks(reader) ? TimeSource.Device : TimeSource.Host) : timeSource;

      long count = reader.RecordCount;
      var headers = new CaptureRecordHeader[count];
      var results = new (CaptureStatus Status, MarkerPayload Payload, StartMetadata? Start)[count];
      long done = 0;

      Parallel.For(
        0,
        count,
        new ParallelOptions { CancellationToken = cancellationToken },
        () => (Decoder: new MarkerDecoder(), Image: reader.CreateFrameImage()),
        (index, _, local) =>
        {
          headers[index] = reader.ReadRecord(index, local.Image);
          results[index] = DecodeFrame(local.Decoder, local.Image, layout);
          long finished = Interlocked.Increment(ref done);
          if (progress != null && finished % 256 == 0)
            progress.Report((double)finished / count);
          return local;
        },
        _ => { }
      );
      progress?.Report(1.0);

      var rows = new List<CaptureRow>((int)Math.Min(count + 64, int.MaxValue));
      long expectedIndex = count > 0 ? headers[0].CaptureIndex : 0;
      for (long i = 0; i < count; ++i)
      {
        var header = headers[i];
        for (; expectedIndex < header.CaptureIndex; ++expectedIndex)
          rows.Add(new CaptureRow(expectedIndex, 0, CaptureStatus.NotRecorded, default));
        long ticks = effectiveTime == TimeSource.Device && header.HasDeviceTicks ? header.DeviceTicks : header.HostTicks;
        var (status, payload, start) = results[i];
        rows.Add(new CaptureRow(header.CaptureIndex, ticks, status, payload, start, (header.Flags & CaptureRecordFlags.SourceDropBefore) != 0));
        expectedIndex = header.CaptureIndex + 1;
      }
      return new DecodedCapture(reader.Header, layout, effectiveTime, rows);
    }

    /// <summary>Find the markers in a sample of the capture and lock onto them.</summary>
    public static MarkerLayout Locate(CaptureFileReader reader, CancellationToken cancellationToken = default)
    {
      var warnings = new List<string>();
      var decoder = new MarkerDecoder(tryHarder: true);
      var image = reader.CreateFrameImage();
      var found = new List<MarkerDecodeResult>();

      foreach (long index in SampleIndices(reader.RecordCount))
      {
        cancellationToken.ThrowIfCancellationRequested();
        reader.ReadRecord(index, image);
        var markers = decoder.DecodeAll(image);
        if (markers.Count == 0)
        {
          var single = decoder.Decode(image);
          if (single.IsDecoded)
            markers.Add(single);
        }
        found.AddRange(markers.Where(m => m.IsDecoded && m.ModuleSizePx > 0));
        // Enough evidence once several frames agree on the layout
        if (found.Count >= 24)
          break;
      }
      if (found.Count == 0)
        throw new InvalidOperationException(
          "No frame markers were found in the capture. Check that the application draws the marker, and see doc/marker-format.md 'Sizing'."
        );

      // Cluster by origin (all markers of one slot share it, whatever their kind)
      var clusters = new List<List<MarkerDecodeResult>>();
      foreach (var marker in found)
      {
        var cluster = clusters.FirstOrDefault(c =>
          Math.Abs(c[0].Bounds.X - marker.Bounds.X) <= 3 * marker.ModuleSizePx && Math.Abs(c[0].Bounds.Y - marker.Bounds.Y) <= 3 * marker.ModuleSizePx
        );
        if (cluster == null)
          clusters.Add(new List<MarkerDecodeResult> { marker });
        else
          cluster.Add(marker);
      }

      var locks = clusters
        .Where(c => c.Count >= Math.Max(1, found.Count / 10))
        .Select(c =>
        {
          float moduleSize = Median(c.Select(m => m.ModuleSizePx));
          int x = (int)Math.Round(Median(c.Select(m => (float)m.Bounds.X)));
          int y = (int)Math.Round(Median(c.Select(m => (float)m.Bounds.Y)));
          int size = (int)Math.Round(MarkerRenderer.MarkerSizePx(1) * moduleSize);
          return new MarkerLock(new PixelRect(x, y, size, size), moduleSize);
        })
        .OrderBy(l => l.Bounds.Y)
        .ToList();

      float module = locks[0].ModuleSizePx;
      if (module < 2f)
        warnings.Add($"The marker is only {module:0.0} stored pixels per module (minimum 2, recommended 3): decoding will be unreliable.");
      else if (module < 2.75f)
        warnings.Add($"The marker is {module:0.0} stored pixels per module (recommended 3 or more).");
      return new MarkerLayout(locks, module, warnings);
    }

    private static (CaptureStatus, MarkerPayload, StartMetadata?) DecodeFrame(MarkerDecoder decoder, GrayImage image, MarkerLayout layout)
    {
      var primary = decoder.DecodeLocked(image, layout.Primary);
      bool torn = false;
      for (int i = 1; i < layout.Locks.Count; ++i)
      {
        var other = decoder.DecodeLocked(image, layout.Locks[i]);
        if (other.IsDecoded && primary.IsDecoded && other.Payload.FrameIndex != primary.Payload.FrameIndex)
          torn = true;
        if (other.IsDecoded && !primary.IsDecoded)
          torn = true;
      }
      if (!primary.IsDecoded)
        return (torn ? CaptureStatus.Torn : CaptureStatus.Undecodable, default, null);
      return (torn ? CaptureStatus.Torn : CaptureStatus.Decoded, primary.Payload, primary.Start);
    }

    private static bool AllRecordsHaveDeviceTicks(CaptureFileReader reader)
    {
      // A record without a device timestamp is rare but possible (late timestamp); sample the file instead of reading every header twice.
      foreach (long index in SampleIndices(reader.RecordCount, 512))
      {
        if (!reader.ReadRecordHeader(index).HasDeviceTicks)
          return false;
      }
      return reader.RecordCount > 0;
    }

    /// <summary>The first frames, then an even spread over the rest of the capture.</summary>
    private static IEnumerable<long> SampleIndices(long count, int samples = LocateSampleCount)
    {
      if (count <= samples)
      {
        for (long i = 0; i < count; ++i)
          yield return i;
        yield break;
      }
      int head = samples / 4;
      for (long i = 0; i < head; ++i)
        yield return i;
      long step = Math.Max(1, (count - head) / (samples - head));
      for (long i = head; i < count; i += step)
        yield return i;
    }

    private static float Median(IEnumerable<float> values)
    {
      var sorted = values.OrderBy(v => v).ToArray();
      return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
  }
}
