//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Decodes every frame of a capture in capture order: a full search per frame until MarkerLocator knows where the markers are, then the
//* cheap locked decode. EXPERIMENTAL camera captures know their layout up front. The recorder's inspection thread runs it live; the analysis
//* runs the same steps on the first frames of frames.mbfc, so both lock onto the same layout. Not thread safe.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture
{
  public sealed class LiveFrameDecoder
  {
    private readonly MarkerDecoder m_searchDecoder = new MarkerDecoder(tryHarder: true);
    private readonly MarkerDecoder m_lockedDecoder;
    private readonly MarkerLocator? m_locator;
    private readonly bool m_camera;

    /// <param name="frames">The captured frames' header (a camera capture's layout follows from its size).</param>
    /// <param name="camera">EXPERIMENTAL: a camera capture (the rig's rectified zones).</param>
    public LiveFrameDecoder(CaptureFileHeader frames, bool camera)
    {
      m_camera = camera;
      m_lockedDecoder = new MarkerDecoder(sampleModuleGrid: camera);
      if (camera)
        Layout = FrameMarkerDecoder.CameraLayout(frames);
      else
        m_locator = new MarkerLocator();
    }

    /// <summary>Where the markers are, once known.</summary>
    public MarkerLayout? Layout { get; private set; }

    public bool Camera => m_camera;

    /// <summary>No more frames: lock onto the markers found so far, if the search had not locked yet. Returns the layout, if any.</summary>
    public MarkerLayout? Finish()
    {
      if (Layout == null && m_locator!.Finish())
        Layout = m_locator.Layout;
      return Layout;
    }

    public FrameDecode Decode(GrayImage image)
    {
      if (Layout != null)
        return FrameMarkerDecoder.DecodeLocked(m_lockedDecoder, image, Layout, m_camera);

      var markers = m_searchDecoder.DecodeAll(image);
      if (markers.Count == 0)
      {
        var single = m_searchDecoder.Decode(image);
        if (single.IsDecoded)
          markers.Add(single);
      }
      if (m_locator!.Feed(markers))
        Layout = m_locator.Layout;
      return FrameMarkerDecoder.FromSearch(markers);
    }
  }
}
