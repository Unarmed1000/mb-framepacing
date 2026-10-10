//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes a capture's tracks (TraceEvents) as the packets of a Perfetto trace: a trace of their own, or added to the bytes of a trace that
//* was recorded. A port of tools/trace_tracks.py: the same bytes (measure/test-data/trace/digest.json).
//*
//* A Perfetto protobuf trace is its packets one after the other, and packets put next to a trace's bytes are part of it. What is added is
//* told apart from everything a trace has by three things, all made from the run and the track's name so that the same capture gives the
//* same bytes: its own packet sequences, one per track; its own track ids, under one root track named for the run; its own flow numbers,
//* for the arrow from a frame's CPU slice to its slice on screen.
//*
//* The tracks go before the recorded trace's packets, with every time on the trace's own clock and no clock named. A reader takes a trace
//* that was written to its file in pieces (a long recording) piece by piece: packets after the last piece whose times lie in earlier ones
//* come too late and are left out (a third of the events, for a test trace with two such marks). Before the first piece nothing is too
//* late. And a time without a clock is the trace's own time whatever its clock snapshots say later in the file: the caller converts with
//* the trace's snapshots (TraceAlignmentUtil.ToTraceNs).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Writes the tracks of one run to a stream as Perfetto trace packets.</summary>
  public sealed class TraceTrackWriter
  {
    /// <summary>
    /// The packet sequences of what is added: this number and the ones after it, one per track. A recording's own writers are numbered from
    /// 1 upwards.
    /// </summary>
    public const uint SequenceBase = 0x4D42_5000;

    // Perfetto's field numbers (protos/perfetto/trace: trace.proto, trace_packet.proto, track_event/*.proto)
    private const int TracePacketField = 1;
    private const int PacketTimestamp = 8;
    private const int PacketSequenceId = 10;
    private const int PacketTrackEvent = 11;
    private const int PacketTrackDescriptor = 60;
    private const int DescriptorUuid = 1;
    private const int DescriptorName = 2;
    private const int DescriptorParentUuid = 5;
    private const int DescriptorCounter = 8;
    private const int DescriptorChildOrdering = 11;
    private const int DescriptorSiblingOrderRank = 12;
    private const int DescriptorDescription = 14;
    private const int DescriptorSiblingMergeBehavior = 15;
    private const int CounterUnitName = 6;
    private const int EventDebugAnnotations = 4;
    private const int EventType = 9;
    private const int EventTrackUuid = 11;
    private const int EventName = 23;
    private const int EventDoubleCounterValue = 44;
    private const int EventFlowIds = 47;
    private const int AnnotationIntValue = 4;
    private const int AnnotationDoubleValue = 5;
    private const int AnnotationStringValue = 6;
    private const int AnnotationName = 10;

    private const ulong ChildOrderingExplicit = 3;
    private const ulong SiblingMergeBehaviorNone = 2;
    private const ulong TypeSliceBegin = 1;
    private const ulong TypeSliceEnd = 2;
    private const ulong TypeInstant = 3;
    private const ulong TypeCounter = 4;

    private static readonly byte[] g_gzipMagic = { 0x1f, 0x8b };
    private static readonly byte[] g_zstdMagic = { 0x28, 0xb5, 0x2f, 0xfd };

    private readonly Stream m_output;
    private readonly string m_runKey;
    private readonly string m_rootName;
    private readonly ulong m_rootId;
    private readonly Func<long, long> m_toTraceNs;
    private readonly ProtoWriter m_trace = new ProtoWriter();
    private readonly ProtoWriter m_packet = new ProtoWriter();
    private readonly ProtoWriter m_message = new ProtoWriter();
    private readonly ProtoWriter m_inner = new ProtoWriter();
    private readonly Dictionary<TraceTrack, int> m_slices = new Dictionary<TraceTrack, int>();
    private readonly Dictionary<TraceTrack, int> m_values = new Dictionary<TraceTrack, int>();

    /// <param name="toTraceNs">Puts a time of the events (on the system clock the application's clock is) on the trace's own clock.</param>
    public TraceTrackWriter(Stream output, string runKey, string runName, Func<long, long> toTraceNs)
    {
      m_output = output;
      m_runKey = runKey;
      m_rootName = RootName(runName);
      m_rootId = StableId(runKey, "root");
      m_toTraceNs = toTraceNs;
    }

    /// <summary>How many slices and moments were written, per track.</summary>
    public IReadOnlyDictionary<TraceTrack, int> Slices => m_slices;

    /// <summary>How many counter values were written, per track.</summary>
    public IReadOnlyDictionary<TraceTrack, int> Values => m_values;

    /// <summary>An id made from the run and a name: 63 bits of their SHA-256, never zero. The same run and name give the same id.</summary>
    public static ulong StableId(string runKey, string name)
    {
      Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
      SHA256.HashData(Encoding.UTF8.GetBytes($"mb-framepacing/{runKey}/{name}"), digest);
      ulong id = BinaryPrimitives.ReadUInt64LittleEndian(digest) & ((1UL << 63) - 1);
      return id != 0 ? id : 1;
    }

    /// <summary>The name of the track everything of a run is under.</summary>
    public static string RootName(string runName) => $"mb-framepacing: {runName}";

    /// <summary>
    /// The bytes of a recorded trace, as packets can be put next to them: a whole-file gzip is unpacked, a trace as it is. A file that is
    /// not a Perfetto protobuf trace is refused.
    /// </summary>
    public static void CopySource(string sourcePath, Stream output)
    {
      string name = Path.GetFileName(sourcePath);
      using var source = File.OpenRead(sourcePath);
      Span<byte> magic = stackalloc byte[4];
      int read = source.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
      magic = magic[..read];
      source.Position = 0;
      if (magic.StartsWith(g_zstdMagic))
        throw new TraceException($"{name} is a zstd file: unpack it first (zstd -d), and give the trace inside.");
      if (magic.StartsWith(g_gzipMagic))
      {
        using var unpacked = new GZipStream(source, CompressionMode.Decompress);
        unpacked.CopyTo(output);
        return;
      }
      if (magic.Length == 0 || magic[0] != 0x0a)
        throw new TraceException($"{name} is not a Perfetto protobuf trace (it does not begin with a packet): JSON and other formats are not taken.");
      source.CopyTo(output);
    }

    /// <summary>
    /// What the tracks rest on (the clocks' offsets, what they were placed by), as slices' arguments can not say it: the root track's
    /// description, which the viewer shows for the track.
    /// </summary>
    public void WriteAttributes(IReadOnlyList<KeyValuePair<string, string>> attributes)
    {
      var description = new StringBuilder(
        "Measured by mb-framepacing from a capture of the display, and placed on this trace's clock (experimental)."
      );
      foreach (var attribute in attributes)
        description.Append('\n').Append(attribute.Key).Append(": ").Append(attribute.Value);

      m_message.Clear();
      m_message.Varint(DescriptorUuid, m_rootId);
      m_message.Text(DescriptorName, m_rootName);
      m_message.Varint(DescriptorChildOrdering, ChildOrderingExplicit);
      m_message.Text(DescriptorDescription, description.ToString());
      m_packet.Clear();
      m_packet.Message(PacketTrackDescriptor, m_message);
      WritePacket();
    }

    /// <summary>One track: its descriptor, then its events in the order given, which is time order.</summary>
    public void WriteTrack(TraceTrack track, IReadOnlyList<TraceEvent> events)
    {
      int rank = (int)track;
      string name = TraceTrackText.NameOf(track);
      ulong trackId = StableId(m_runKey, name);
      uint sequence = SequenceBase + (uint)rank;

      m_message.Clear();
      m_message.Varint(DescriptorUuid, trackId);
      m_message.Text(DescriptorName, name);
      m_message.Varint(DescriptorParentUuid, m_rootId);
      if (TraceTrackText.IsCounter(track))
      {
        m_inner.Clear();
        m_inner.Text(CounterUnitName, "ms");
        m_message.Message(DescriptorCounter, m_inner);
      }
      m_message.Signed(DescriptorSiblingOrderRank, rank);
      m_message.Text(DescriptorDescription, TraceTrackText.DescriptionOf(track));
      m_message.Varint(DescriptorSiblingMergeBehavior, SiblingMergeBehaviorNone);
      m_packet.Clear();
      m_packet.Message(PacketTrackDescriptor, m_message);
      WritePacket();

      foreach (var one in events)
      {
        switch (one.Kind)
        {
          case TraceEventKind.Slice:
          {
            long start = m_toTraceNs(one.StartNs);
            m_message.Clear();
            Annotate(one.Arguments);
            m_message.Varint(EventType, TypeSliceBegin);
            m_message.Varint(EventTrackUuid, trackId);
            m_message.Text(EventName, one.Name);
            if (one.Flow != 0)
              m_message.Fixed64(EventFlowIds, StableId(m_runKey, "flow/" + one.Flow.ToString(CultureInfo.InvariantCulture)));
            WriteEvent(sequence, start);
            // A slice does not end before it begins, also where the clock it was on is one that can go back
            m_message.Clear();
            m_message.Varint(EventType, TypeSliceEnd);
            m_message.Varint(EventTrackUuid, trackId);
            WriteEvent(sequence, Math.Max(m_toTraceNs(one.EndNs), start));
            Count(m_slices, track);
            break;
          }
          case TraceEventKind.Instant:
            m_message.Clear();
            Annotate(one.Arguments);
            m_message.Varint(EventType, TypeInstant);
            m_message.Varint(EventTrackUuid, trackId);
            m_message.Text(EventName, one.Name);
            WriteEvent(sequence, m_toTraceNs(one.StartNs));
            Count(m_slices, track);
            break;
          case TraceEventKind.Counter:
            m_message.Clear();
            m_message.Varint(EventType, TypeCounter);
            m_message.Varint(EventTrackUuid, trackId);
            m_message.Double(EventDoubleCounterValue, one.Value);
            WriteEvent(sequence, m_toTraceNs(one.StartNs));
            Count(m_values, track);
            break;
          default:
            throw new ArgumentOutOfRangeException(nameof(events));
        }
      }
    }

    private static void Count(Dictionary<TraceTrack, int> counts, TraceTrack track) => counts[track] = counts.GetValueOrDefault(track) + 1;

    /// <summary>The arguments of the event in m_message, each a debug annotation: its value, then its name.</summary>
    private void Annotate(IReadOnlyList<TraceArgument> arguments)
    {
      foreach (var argument in arguments)
      {
        m_inner.Clear();
        switch (argument.Kind)
        {
          case TraceArgumentKind.Whole:
            m_inner.Signed(AnnotationIntValue, argument.Whole);
            break;
          case TraceArgumentKind.Number:
            m_inner.Double(AnnotationDoubleValue, argument.Number);
            break;
          default:
            m_inner.Text(AnnotationStringValue, argument.Text);
            break;
        }
        m_inner.Text(AnnotationName, argument.Name);
        m_message.Message(EventDebugAnnotations, m_inner);
      }
    }

    /// <summary>The event in m_message as a packet at <paramref name="timeNs"/>. No clock is named: the time is the trace's own.</summary>
    private void WriteEvent(uint sequence, long timeNs)
    {
      if (timeNs < 0)
        throw new TraceException(
          $"A time before the clock's zero can not be written ({timeNs.ToString(CultureInfo.InvariantCulture)} ns): the run's frames are "
            + "placed before the trace's clock began."
        );
      m_packet.Clear();
      m_packet.Varint(PacketTimestamp, (ulong)timeNs);
      m_packet.Varint(PacketSequenceId, sequence);
      m_packet.Message(PacketTrackEvent, m_message);
      WritePacket();
    }

    /// <summary>The packet in m_packet, as the trace's next packet.</summary>
    private void WritePacket()
    {
      m_trace.Clear();
      m_trace.Message(TracePacketField, m_packet);
      m_output.Write(m_trace.Bytes);
    }
  }
}
