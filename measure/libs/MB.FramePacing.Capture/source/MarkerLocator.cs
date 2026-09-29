//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Finds where the markers are, from the markers the search found in a capture's frames, fed in capture order: once enough frames agree
//* it clusters them by origin (the main marker's frame, start and end kinds share one; the sync marker has its own) and locks onto each.
//* Used live during a capture and afterwards on frames.mbfc alike, so both lock onto the same layout.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public sealed class MarkerLocator
  {
    /// <summary>Lock once this many markers were found...</summary>
    public const int EnoughMarkers = 24;

    /// <summary>... or once this many frames were searched and at least one marker was found.</summary>
    public const int MaxFrames = 240;

    private readonly List<MarkerDecodeResult> m_found = new List<MarkerDecodeResult>();
    private int m_frames;

    /// <summary>The layout once locked, else null.</summary>
    public MarkerLayout? Layout { get; private set; }

    /// <summary>Add the markers the search found in the next frame. Returns true once the layout is known.</summary>
    public bool Feed(IEnumerable<MarkerDecodeResult> markers)
    {
      if (Layout != null)
        return true;
      ++m_frames;
      m_found.AddRange(markers.Where(m => m.IsDecoded && m.ModuleSizePx > 0));
      if (m_found.Count >= EnoughMarkers || (m_frames >= MaxFrames && m_found.Count > 0))
        Layout = MarkerLayout.For(Cluster(m_found), camera: false);
      return Layout != null;
    }

    /// <summary>
    /// No more frames (the capture or the file ended): lock onto the markers found so far, if any. Returns true once the layout is known.
    /// </summary>
    public bool Finish()
    {
      if (Layout == null && m_found.Count > 0)
        Layout = MarkerLayout.For(Cluster(m_found), camera: false);
      return Layout != null;
    }

    /// <summary>The locks of the markers found: one per origin seen in at least a tenth of them, the main marker first.</summary>
    public static IReadOnlyList<MarkerLock> Cluster(IReadOnlyList<MarkerDecodeResult> found)
    {
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

      // The main marker first: it carries the payload and the timing; a sync marker only checks tearing
      return clusters
        .Where(c => c.Count >= Math.Max(1, found.Count / 10))
        .Select(c =>
        {
          double moduleSize = Median(c.Select(m => m.ModuleSizePx));
          int x = (int)Math.Round(Median(c.Select(m => (double)m.Bounds.X)));
          int y = (int)Math.Round(Median(c.Select(m => (double)m.Bounds.Y)));
          bool sync = c.Count(m => m.Payload.Kind == MarkerKind.Sync) * 2 > c.Count;
          return (Sync: sync, Lock: MarkerLock.At(x, y, moduleSize, sync ? MarkerKind.Sync : MarkerKind.Frame));
        })
        .OrderBy(l => l.Sync)
        .ThenBy(l => l.Lock.Bounds.Y)
        .Select(l => l.Lock)
        .Take(CaptureDataHeader.MaxMarkers)
        .ToList();
    }

    private static double Median(IEnumerable<double> values)
    {
      var sorted = values.OrderBy(v => v).ToArray();
      return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
  }
}
