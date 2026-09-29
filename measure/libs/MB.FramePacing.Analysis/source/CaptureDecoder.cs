//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Turns a capture into one CaptureRow per capture index. The analysis always starts from the capture data (captures.mbcd, every captured
//* frame's decoded markers): FromData reads it into rows, filling the capture index gaps the recorder's drops left with NotRecorded rows.
//* Captures that only have their frames (frames.mbfc) are decoded into the same data first (DecodeFrames), with the steps a capture takes
//* live: search frame by frame in capture order until MarkerLocator knows where the markers are, then decode every remaining frame in parallel
//* with the locked decoder. So both produce the same records.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Capture;
using MB.FramePacing.Data;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  public static class CaptureDecoder
  {
    /// <summary>Decode the frames of <paramref name="reader"/> and read the result into rows (<see cref="DecodeFrames"/>, <see cref="FromData"/>).</summary>
    public static DecodedCapture Decode(
      CaptureFileReader reader,
      TimeSource timeSource = TimeSource.Auto,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default,
      ScanoutModel scanout = ScanoutModel.SingleScanout
    )
    {
      var (header, records) = DecodeFrames(reader, scanout == ScanoutModel.Camera, progress, cancellationToken);
      return FromData(header, records, timeSource);
    }

    /// <summary>The capture data of a capture's stored frames: the records a live capture would have written, and its header.</summary>
    public static (CaptureDataHeader Header, CaptureDataRecord[] Records) DecodeFrames(
      CaptureFileReader reader,
      bool camera,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default
    )
    {
      long count = reader.RecordCount;
      var records = new CaptureDataRecord[count];
      long done = 0;
      void Report()
      {
        long finished = Interlocked.Increment(ref done);
        if (progress != null && finished % 256 == 0)
          progress.Report((double)finished / count);
      }

      // In capture order until the markers are located, as the capture does live
      var live = new LiveFrameDecoder(reader.Header, camera);
      var image = reader.CreateFrameImage();
      long next = 0;
      for (; next < count && live.Layout == null; ++next)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var header = reader.ReadRecord(next, image);
        records[next] = ToRecord(header, live.Decode(image));
        Report();
      }
      var layout =
        live.Finish()
        ?? throw new InvalidOperationException(
          "No frame markers were found in the capture. Check that the application draws the marker, and see doc/marker-format.md 'Sizing'."
        );

      Parallel.For(
        next,
        count,
        new ParallelOptions { CancellationToken = cancellationToken },
        () => (Decoder: new MarkerDecoder(sampleModuleGrid: camera), Image: reader.CreateFrameImage()),
        (index, _, local) =>
        {
          var header = reader.ReadRecord(index, local.Image);
          records[index] = ToRecord(header, FrameMarkerDecoder.DecodeLocked(local.Decoder, local.Image, layout, camera));
          Report();
          return local;
        },
        _ => { }
      );
      progress?.Report(1.0);
      return (reader.Header.ToDataHeader(layout.Locks, framesStored: true, camera), records);
    }

    /// <summary>
    /// The rows of the capture data: one per capture index, the ones the recorder dropped as NotRecorded. With <see cref="TimeSource.Auto"/>
    /// the capture device's clock is used when every record has its timestamp, the host clock otherwise.
    /// </summary>
    public static DecodedCapture FromData(CaptureDataHeader header, IReadOnlyList<CaptureDataRecord> records, TimeSource timeSource = TimeSource.Auto)
    {
      var locks = header.ToLocks();
      if (locks.Count == 0)
        throw new InvalidOperationException(
          "No frame markers were found in the capture. Check that the application draws the marker, and see doc/marker-format.md 'Sizing'."
        );
      var layout = MarkerLayout.For(locks, header.Camera);
      var effectiveTime =
        timeSource == TimeSource.Auto ? (records.Count > 0 && records.All(r => r.HasDeviceTicks) ? TimeSource.Device : TimeSource.Host) : timeSource;

      var rows = new List<CaptureRow>(records.Count + 64);
      long expectedIndex = records.Count > 0 ? records[0].CaptureIndex : 0;
      foreach (var record in records)
      {
        for (; expectedIndex < record.CaptureIndex; ++expectedIndex)
          rows.Add(new CaptureRow(expectedIndex, 0, CaptureStatus.NotRecorded, default));
        rows.Add(ToRow(record, effectiveTime, header.Camera));
        expectedIndex = record.CaptureIndex + 1;
      }
      return new DecodedCapture(header.ToFileHeader(), layout, effectiveTime, rows);
    }

    private static CaptureDataRecord ToRecord(CaptureRecordHeader header, FrameDecode decode) =>
      new CaptureDataRecord(
        header.CaptureIndex,
        header.HostTicks,
        header.DeviceTicks,
        header.Flags,
        decode.Status,
        decode.MainBytes,
        decode.SecondBytes
      );

    private static CaptureRow ToRow(CaptureDataRecord record, TimeSource time, bool camera)
    {
      long ticks = time == TimeSource.Device && record.HasDeviceTicks ? record.DeviceTicks : record.HostTicks;
      bool sourceDrop = (record.Flags & CaptureRecordFlags.SourceDropBefore) != 0;
      // Camera: the second zone's frame measures the scanout (capture cards: the sync marker only checked tearing, in the status)
      MarkerPayload? secondary = camera && record.SecondBytes != null && MarkerPayload.TryDecode(record.SecondBytes, out var second) ? second : null;

      var status = record.Status switch
      {
        CaptureDataStatus.Decoded => CaptureStatus.Decoded,
        CaptureDataStatus.Torn => CaptureStatus.Torn,
        _ => CaptureStatus.Undecodable,
      };
      MarkerPayload payload = default;
      StartMetadata? start = null;
      if (record.MainBytes == null || !MarkerPayload.TryDecode(record.MainBytes, out payload, out start))
      {
        // No main marker: undecodable, or torn without one
        payload = default;
        start = null;
        if (status == CaptureStatus.Decoded)
          status = CaptureStatus.Undecodable;
      }
      return new CaptureRow(record.CaptureIndex, ticks, status, payload, start, sourceDrop, secondary)
      {
        HostTicks = record.HostTicks,
        DeviceTicks = record.HasDeviceTicks ? record.DeviceTicks : null,
        MarkerBytes = record.MainBytes,
      };
    }
  }
}
