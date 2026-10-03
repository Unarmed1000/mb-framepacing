//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The header of captures.mbcd (doc/capture-data-format.md): 256 bytes, little endian. It describes the captured frames the markers were read
//* from, where the markers are, and whether the frames were stored too.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace MB.FramePacing.Data
{
  /// <param name="Width">Width of the stored frames, in pixels.</param>
  /// <param name="Height">Height of the stored frames, in pixels.</param>
  /// <param name="FrameRateNumerator">The source's nominal frame rate as a fraction (0/0 = unknown).</param>
  /// <param name="FrameRateDenominator">The source's nominal frame rate as a fraction (0/0 = unknown).</param>
  /// <param name="SourceWidth">The source's frame width before scaling and cropping (0 = the stored width).</param>
  /// <param name="SourceHeight">The source's frame height before scaling and cropping (0 = the stored height).</param>
  /// <param name="Region">The stored region of the source frame, in source pixels (empty = the whole frame).</param>
  /// <param name="Markers">Where the markers were, in stored pixels (the main marker first); empty when none was found.</param>
  /// <param name="FramesStored">The frames were also stored, in frames.mbfc.</param>
  /// <param name="Camera">EXPERIMENTAL: a camera capture (the frames are the rig's rectified zones).</param>
  public sealed record CaptureDataHeader(
    int Width,
    int Height,
    uint FrameRateNumerator,
    uint FrameRateDenominator,
    int SourceWidth,
    int SourceHeight,
    Rectangle Region,
    IReadOnlyList<MarkerLocation> Markers,
    bool FramesStored,
    bool Camera
  )
  {
    /// <summary>The file name a capture folder uses.</summary>
    public const string FileName = "captures.mbcd";

    public const uint Magic = 0x4443424D; // "MBCD" little endian
    public const ushort FormatVersion = 1;
    public const int HeaderSize = 256;
    public const int MaxMarkers = 4;

    private const int MarkersOffset = 72;
    private const int SyncRegionOffset = 168;
    private const int MarkerSize = 24;
    private const uint FramesStoredFlag = 1;
    private const uint CameraFlag = 2;

    /// <summary>
    /// The region of the source stored below <see cref="Region"/> for the sync marker, in source pixels: a capture that stores only the
    /// markers keeps the two as one frame, the main marker's region on top (empty = none: one region, or the whole frame).
    /// </summary>
    public Rectangle SyncRegion { get; init; }

    public void Write(Span<byte> destination)
    {
      if (destination.Length < HeaderSize)
        throw new ArgumentException("Header buffer too small", nameof(destination));
      if (Markers.Count > MaxMarkers)
        throw new ArgumentException($"At most {MaxMarkers} marker locations fit the capture data header");
      destination.Slice(0, HeaderSize).Clear();
      BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
      BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4), FormatVersion);
      BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6), HeaderSize);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), CaptureDataRecord.Size);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12), (FramesStored ? FramesStoredFlag : 0) | (Camera ? CameraFlag : 0));
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16), Width);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20), Height);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24), FrameRateNumerator);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28), FrameRateDenominator);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(32), SourceWidth);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(36), SourceHeight);
      WriteRect(destination.Slice(40), Region);
      BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(64), (uint)Markers.Count);
      for (int i = 0; i < Markers.Count; ++i)
      {
        var slot = destination.Slice(MarkersOffset + (i * MarkerSize));
        WriteRect(slot, Markers[i].Bounds);
        BinaryPrimitives.WriteDoubleLittleEndian(slot.Slice(16), Markers[i].ModuleSizePx);
      }
      WriteRect(destination.Slice(SyncRegionOffset), SyncRegion);
    }

    /// <summary>Parse a header. Throws <see cref="InvalidDataException"/> for another file, a newer format version or wrong sizes.</summary>
    public static CaptureDataHeader Read(ReadOnlySpan<byte> source)
    {
      if (source.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic)
        throw new InvalidDataException("Not an mb-framepacing capture data file (.mbcd)");
      ushort version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4));
      if (version > FormatVersion)
        throw new InvalidDataException(
          $"The capture data file has format version {version}, newer than this reader reads ({FormatVersion}): update the tools or the library"
        );
      if (version != FormatVersion)
        throw new InvalidDataException($"Unsupported capture data file format version {version}");
      if (
        BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6)) != HeaderSize
        || BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8)) != CaptureDataRecord.Size
      )
        throw new InvalidDataException("Unexpected capture data header or record size");

      uint flags = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12));
      uint markerCount = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(64));
      if (markerCount > MaxMarkers)
        throw new InvalidDataException("Invalid marker count in the capture data header");
      var markers = new List<MarkerLocation>();
      for (int i = 0; i < markerCount; ++i)
      {
        var slot = source.Slice(MarkersOffset + (i * MarkerSize));
        markers.Add(new MarkerLocation(ReadRect(slot), BinaryPrimitives.ReadDoubleLittleEndian(slot.Slice(16))));
      }
      return new CaptureDataHeader(
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16)),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(20)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(24)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(28)),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(32)),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(36)),
        ReadRect(source.Slice(40)),
        markers,
        (flags & FramesStoredFlag) != 0,
        (flags & CameraFlag) != 0
      )
      {
        SyncRegion = ReadRect(source.Slice(SyncRegionOffset)),
      };
    }

    private static void WriteRect(Span<byte> destination, Rectangle rect)
    {
      BinaryPrimitives.WriteInt32LittleEndian(destination, rect.X);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), rect.Y);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), rect.Width);
      BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), rect.Height);
    }

    private static Rectangle ReadRect(ReadOnlySpan<byte> source) =>
      new Rectangle(
        BinaryPrimitives.ReadInt32LittleEndian(source),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4)),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8)),
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12))
      );
  }
}
