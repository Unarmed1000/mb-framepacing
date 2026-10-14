//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The protobuf messages the library writes and reads by hand: fields worked out by hand from the wire format, a track's packets byte for
//* byte, and bytes that are no message.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.TraceJoin.UnitTest
{
  [TestFixture]
  public class ProtoTests
  {
    [Test]
    public void Fields_AreWrittenAsTheWireFormatHasThem()
    {
      var writer = new ProtoWriter();
      writer.Varint(1, 1);
      writer.Varint(8, 300);
      writer.Signed(4, -1);
      writer.Double(5, 1.0);
      writer.Fixed64(47, 0x0102030405060708);
      writer.Text(2, "é!");

      Assert.That(
        Convert.ToHexString(writer.Bytes),
        Is.EqualTo(
          "0801" // field 1, varint 1
            + "40AC02" // field 8: 300 is 0b1_0010_1100, low seven bits first
            + "20FFFFFFFFFFFFFFFFFF01" // field 4: -1 as its 64 bits, ten bytes
            + "29000000000000F03F" // field 5, eight bytes: 1.0
            + "F9020807060504030201" // field 47 (tag 377: two bytes), eight bytes little endian
            + "1203C3A921" // field 2, length 3: the text's UTF-8
        )
      );

      var inner = new ProtoWriter();
      inner.Varint(1, 5);
      writer.Clear();
      writer.Message(60, inner);
      Assert.That(Convert.ToHexString(writer.Bytes), Is.EqualTo("E203020805"));
    }

    [Test]
    public void AMessageLongerThanTheBuffer_Grows()
    {
      var writer = new ProtoWriter();
      string text = new string('x', 1000);
      writer.Text(1, text);
      writer.Text(1, text);

      var reader = new ProtoReader(writer.Bytes);
      Assert.That(reader.Next() && reader.Field == 1 && reader.WireType == ProtoReader.WireLength, Is.True);
      Assert.That(reader.Bytes().Length, Is.EqualTo(1000));
      Assert.That(reader.Next(), Is.True);
      Assert.That(reader.Bytes().Length, Is.EqualTo(1000));
      Assert.That(reader.Next(), Is.False);
      Assert.That(reader.AtEnd, Is.True);
    }

    [Test]
    public void Fields_AreReadBackAndUnknownOnesPassedOver()
    {
      var writer = new ProtoWriter();
      writer.Varint(3, ulong.MaxValue);
      writer.Double(7, -2.5);
      writer.Text(9, "abc");
      var bytes = writer.Bytes.ToArray().Concat(new byte[] { (11 << 3) | 5, 1, 2, 3, 4 }).ToArray();

      var reader = new ProtoReader(bytes);
      Assert.That(reader.Next() && reader.Field == 3, Is.True);
      Assert.That(reader.Varint(), Is.EqualTo(ulong.MaxValue));
      Assert.That(reader.Next() && reader.Field == 7 && reader.WireType == ProtoReader.WireFixed64, Is.True);
      Assert.That(BitConverter.UInt64BitsToDouble(reader.Fixed64()), Is.EqualTo(-2.5));

      // The rest passed over, whatever its kind
      var skipping = new ProtoReader(bytes);
      int fields = 0;
      while (skipping.Next())
      {
        skipping.Skip();
        ++fields;
      }
      Assert.That(fields, Is.EqualTo(4));
    }

    [TestCase("08")] // a number with no byte
    [TestCase("08FFFFFFFFFFFFFFFFFFFF01")] // a number of eleven bytes
    [TestCase("0900")] // eight bytes cut short
    [TestCase("0A05AA")] // a length past the end
    [TestCase("0D0000")] // four bytes cut short
    [TestCase("0B")] // a wire type that is not read (a group)
    public void BytesThatAreNoMessage_AreRefused(string hex)
    {
      Assert.Throws<InvalidDataException>(() =>
      {
        var reader = new ProtoReader(Convert.FromHexString(hex));
        while (reader.Next())
          reader.Skip();
      });
    }

    [Test]
    public void ATracksPackets_AreWrittenFieldByField()
    {
      using var output = new MemoryStream();
      var writer = new TraceTrackWriter(output, "1/", "r", time => time + 1);
      var events = new[]
      {
        TraceEvent.Slice(
          TraceTrack.FrameEvents,
          9,
          4,
          "s",
          new[] { TraceArgument.Of("i", 7), TraceArgument.Of("d", 0.5), TraceArgument.Of("t", "x") },
          3
        ),
        TraceEvent.Instant(TraceTrack.FrameEvents, 20, "m", Array.Empty<TraceArgument>()),
      };

      writer.WriteTrack(TraceTrack.FrameEvents, events);

      string id = Varint(TraceTrackWriter.StableId("1/", "Frame events"));
      string root = Varint(TraceTrackWriter.StableId("1/", "root"));
      string flow = Convert.ToHexString(BitConverter.GetBytes(TraceTrackWriter.StableId("1/", "flow/3")));
      string description = TraceTrackText.DescriptionOf(TraceTrack.FrameEvents);
      string descriptor =
        "08"
        + id
        + "120C"
        + Hex("Frame events")
        + "28"
        + root
        + "60" // the track's place among its siblings
        + Varint((ulong)TraceTrack.FrameEvents)
        + "72"
        + Varint((ulong)description.Length)
        + Hex(description)
        + "7802";
      string begin =
        "22"
        + "052007"
        + "5201"
        + Hex("i") // an argument: its value (field 4), then its name (field 10)
        + "22"
        + "0C29000000000000E03F5201"
        + Hex("d")
        + "22"
        + "063201"
        + Hex("x")
        + "5201"
        + Hex("t")
        + "4801"
        + "58"
        + id
        + "BA0101"
        + Hex("s")
        + "F902"
        + flow;
      string end = "4802" + "58" + id;
      string instant = "4803" + "58" + id + "BA0101" + Hex("m");
      string sequence = "50" + Varint(TraceTrackWriter.SequenceBase + (uint)TraceTrack.FrameEvents);
      string expected =
        Packet("E203" + Length(descriptor) + descriptor)
        + Packet("400A" + sequence + "5A" + Length(begin) + begin)
        // A slice does not end before it begins
        + Packet("400A" + sequence + "5A" + Length(end) + end)
        + Packet("4015" + sequence + "5A" + Length(instant) + instant);
      Assert.That(Convert.ToHexString(output.ToArray()), Is.EqualTo(expected));
      Assert.That(writer.Slices[TraceTrack.FrameEvents], Is.EqualTo(2));
      Assert.That(writer.Values, Is.Empty);
    }

    [Test]
    public void StableIds_AreMadeFromTheRunAndTheName()
    {
      ulong id = TraceTrackWriter.StableId("1/a", "root");
      Assert.That(id, Is.EqualTo(TraceTrackWriter.StableId("1/a", "root")));
      Assert.That(id, Is.Not.EqualTo(TraceTrackWriter.StableId("1/b", "root")));
      Assert.That(id, Is.Not.EqualTo(TraceTrackWriter.StableId("1/a", "other")));
      Assert.That(id >> 63, Is.Zero);
      Assert.That(TraceTrackWriter.RootName("x"), Is.EqualTo("mb-framepacing: x"));
    }

    private static string Hex(string text) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text));

    private static string Varint(ulong value)
    {
      var bytes = new MemoryStream();
      while (value >= 0x80)
      {
        bytes.WriteByte((byte)(value | 0x80));
        value >>= 7;
      }
      bytes.WriteByte((byte)value);
      return Convert.ToHexString(bytes.ToArray());
    }

    /// <summary>The length of a message given as hexadecimal, as a varint.</summary>
    private static string Length(string hex) => Varint((ulong)(hex.Length / 2));

    private static string Packet(string hex) => "0A" + Length(hex) + hex;
  }
}
