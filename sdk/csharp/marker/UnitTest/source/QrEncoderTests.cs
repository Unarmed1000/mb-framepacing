//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The marker's QR encoder against the reference it must agree with (Reference/ReferenceQrEncoder.cs, the module-by-module encoder the
//* library had before): every symbol module by module, every mask's penalty score, and the pieces the encoder is made of. The reference
//* itself is pinned to the golden modules.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Marker.Reference;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class QrEncoderTests
  {
    private static readonly int[] g_versions = { 2, 6 };

    /// <summary>The bytes each version holds in byte mode at level M.</summary>
    private static int CapacityOf(int version) => version == 2 ? WireFormat.SyncQrCapacityBytes : WireFormat.QrCapacityBytes;

    /// <summary>Both encoders hold the same symbol, and ours holds it the same as rows and as columns. Null when they agree.</summary>
    private static string? Difference(QrEncoder encoder, ReferenceQrEncoder reference)
    {
      if (encoder.Size != reference.Size)
        return $"size {encoder.Size}, the reference's is {reference.Size}";
      for (int y = 0; y < reference.Size; ++y)
      {
        for (int x = 0; x < reference.Size; ++x)
        {
          bool expected = reference.IsDark(x, y);
          bool inColumn = (encoder.Column(x) & QrEncoder.Bit(y)) != 0;
          if (encoder.IsDark(x, y) != expected || inColumn != expected)
            return $"module ({x}, {y}) is {(expected ? "dark" : "light")} in the reference";
        }
      }
      return null;
    }

    private static byte[] Bytes(Random random, int count)
    {
      var bytes = new byte[count];
      random.NextBytes(bytes);
      return bytes;
    }

    /// <summary>A line (module i is bit 63 - i) from its runs, the first one light: {2, 1, 3} is two light, one dark, three light modules.</summary>
    private static ulong LineOf(params int[] runs)
    {
      ulong line = 0;
      int position = 0;
      bool dark = false;
      foreach (int run in runs)
      {
        for (int i = 0; i < run; ++i, ++position)
        {
          if (dark)
            line |= QrEncoder.Bit(position);
        }
        dark = !dark;
      }
      return line;
    }

    /// <summary>
    /// Lines with finder-like patterns of every unit that fits, with every amount of light on either side that matters, at the line's start,
    /// after a dark module, followed by alternating modules or by a long dark run.
    /// </summary>
    private static List<ulong> FinderLikeLines(int size)
    {
      var lines = new List<ulong>();
      for (int unit = 1; unit * 7 <= size; ++unit)
      {
        for (int prefix = 0; prefix <= 1; ++prefix)
        {
          for (int left = 0; left <= (unit * 4) + 1; ++left)
          {
            for (int right = 0; right <= (unit * 4) + 1; ++right)
            {
              int used = prefix + left + (unit * 7) + right;
              if (used > size || (prefix == 1 && left == 0))
                continue;
              // light, [dark], light, the core, light
              var core =
                prefix == 0
                  ? new List<int> { left, unit, unit, unit * 3, unit, unit, right }
                  : new List<int> { 0, prefix, left, unit, unit, unit * 3, unit, unit, right };
              // ... then alternating single modules
              var runs = new List<int>(core);
              for (int i = used; i < size && right > 0; ++i)
                runs.Add(1);
              lines.Add(LineOf(runs.ToArray()));
              // ... or one dark run to the end
              if (right > 0 && used < size)
              {
                runs = new List<int>(core) { size - used };
                lines.Add(LineOf(runs.ToArray()));
              }
            }
          }
        }
      }
      return lines;
    }

    private static ulong NextLine(Random random) => ((ulong)(uint)random.Next() << 40) ^ ((ulong)(uint)random.Next() << 20) ^ (uint)random.Next();

    [Test]
    public void TheReferenceStillGivesTheGoldenModules()
    {
      // The reference is the encoder the library had: it gave the golden modules then, and must give them now
      var reference = new ReferenceQrEncoder();
      var payloadBytes = new byte[Payload.MaxEncodedByteCount];
      var rows = GoldenData.ModuleDigest().ToList();
      Assert.That(rows, Has.Count.EqualTo(512));
      foreach (var row in rows)
      {
        int byteCount = FrameMarker.EncodePayload(row.Payload, row.Start, payloadBytes);
        int version = row.Payload.Kind == MarkerKind.Sync ? WireFormat.SyncQrVersion : WireFormat.QrVersion;
        Assert.That(reference.Encode(payloadBytes.AsSpan(0, byteCount), version, version), Is.True);

        var packed = new byte[ModuleMatrix.PackedModuleByteCount(reference.Size)];
        int index = 0;
        for (int y = 0; y < reference.Size; ++y)
        {
          for (int x = 0; x < reference.Size; ++x, ++index)
          {
            if (reference.IsDark(x, y))
              packed[index >> 3] |= (byte)(0x80 >> (index & 7));
          }
        }
        Assert.That(Convert.ToHexString(packed).ToLowerInvariant(), Is.EqualTo(row.ModulesHex.ToLowerInvariant()), $"{row}");
      }
    }

    [Test]
    public void GivesTheReferencesSymbolForEveryLength()
    {
      var random = new Random(20261001);
      var encoder = new QrEncoder();
      var reference = new ReferenceQrEncoder();
      foreach (int version in g_versions)
      {
        for (int length = 0; length <= CapacityOf(version); ++length)
        {
          // Random bytes, and the regular ones a marker is mostly made of
          var payloads = new[] { Bytes(random, length), new byte[length], Filled(length, 0xFF), Filled(length, 0x55), Filled(length, 0x0F) };
          foreach (var payload in payloads)
          {
            Assert.That(reference.Encode(payload, version, version), Is.True);

            Assert.That(encoder.Encode(payload, version), Is.True);

            Assert.That(Difference(encoder, reference), Is.Null, $"version {version}, {length} bytes of {(length > 0 ? payload[0] : 0)}");
          }
        }
      }
    }

    private static byte[] Filled(int length, byte value)
    {
      var bytes = new byte[length];
      Array.Fill(bytes, value);
      return bytes;
    }

    [Test]
    public void GivesTheReferencesSymbolForRandomPayloads()
    {
      var random = new Random(4711);
      var encoder = new QrEncoder();
      var reference = new ReferenceQrEncoder();
      var chosenMasks = new HashSet<string>();
      foreach (int version in g_versions)
      {
        for (int i = 0; i < 1000; ++i)
        {
          // Mostly the lengths the markers have: 20 bytes in version 2; 57 and 81 in version 6
          int length =
            i % 3 == 0 ? random.Next(CapacityOf(version) + 1)
            : version == 2 ? 20
            : i % 3 == 1 ? 57
            : 81;
          var payload = Bytes(random, length);
          if (i % 4 == 0)
          {
            // As a marker: mostly zero bytes
            for (int k = 0; k < payload.Length; ++k)
              payload[k] = k % 5 == 0 ? payload[k] : (byte)0;
          }
          Assert.That(reference.Encode(payload, version, version), Is.True);

          Assert.That(encoder.Encode(payload, version), Is.True);

          Assert.That(Difference(encoder, reference), Is.Null, $"version {version}, payload {i}");
          // The format bits beside the upper left finder name the mask
          chosenMasks.Add($"{encoder.IsDark(2, 8)}{encoder.IsDark(3, 8)}{encoder.IsDark(4, 8)}");
        }
      }
      Assert.That(chosenMasks, Has.Count.EqualTo(QrMaskPatterns.MaskCount), "every mask was the best one for some payload");
    }

    [Test]
    public void EveryMaskGivesTheReferencesSymbolAndPenalty()
    {
      var random = new Random(8128);
      var encoder = new QrEncoder();
      var reference = new ReferenceQrEncoder();
      var rows = new ulong[QrEncoder.MaxSize];
      var columns = new ulong[QrEncoder.MaxSize];
      foreach (int version in g_versions)
      {
        for (int i = 0; i < 150; ++i)
        {
          var payload = Bytes(random, random.Next(CapacityOf(version) + 1));
          for (int mask = 0; mask < QrMaskPatterns.MaskCount; ++mask)
          {
            Assert.That(reference.Encode(payload, version, version, mask), Is.True);

            Assert.That(encoder.EncodeWithMask(payload, version, mask), Is.True);

            Assert.That(Difference(encoder, reference), Is.Null, $"version {version}, payload {i}, mask {mask}");
            CopySymbol(encoder, rows, columns);
            Assert.That(
              QrEncoder.PenaltyScore(rows, columns, encoder.Size),
              Is.EqualTo(reference.PenaltyScore()),
              $"version {version}, payload {i}, mask {mask}"
            );
          }
        }
      }
    }

    private static void CopySymbol(QrEncoder encoder, ulong[] rows, ulong[] columns)
    {
      for (int i = 0; i < encoder.Size; ++i)
      {
        rows[i] = encoder.Row(i);
        columns[i] = encoder.Column(i);
      }
    }

    [Test]
    public void TheFirstOfEqualMasksWins()
    {
      // Payloads whose two best masks score the same: the lower numbered one is taken, as the reference takes it
      var random = new Random(31337);
      var encoder = new QrEncoder();
      var masked = new QrEncoder();
      var reference = new ReferenceQrEncoder();
      var rows = new ulong[QrEncoder.MaxSize];
      var columns = new ulong[QrEncoder.MaxSize];
      var scores = new int[QrMaskPatterns.MaskCount];
      int ties = 0;
      for (int i = 0; i < 20000 && ties < 5; ++i)
      {
        var payload = Bytes(random, 16);
        for (int mask = 0; mask < QrMaskPatterns.MaskCount; ++mask)
        {
          Assert.That(masked.EncodeWithMask(payload, 2, mask), Is.True);
          CopySymbol(masked, rows, columns);
          scores[mask] = QrEncoder.PenaltyScore(rows, columns, masked.Size);
        }
        int best = scores.Min();
        if (scores.Count(score => score == best) < 2)
          continue;
        ++ties;
        Assert.That(masked.EncodeWithMask(payload, 2, Array.IndexOf(scores, best)), Is.True);
        Assert.That(reference.Encode(payload, 2, 2), Is.True);

        Assert.That(encoder.Encode(payload, 2), Is.True);

        Assert.That(Enumerable.Range(0, encoder.Size).Select(encoder.Row), Is.EqualTo(Enumerable.Range(0, masked.Size).Select(masked.Row)));
        Assert.That(Difference(encoder, reference), Is.Null);
      }
      Assert.That(ties, Is.EqualTo(5), "payloads with equal best masks were found");
    }

    [Test]
    public void RefusesOtherVersionsAndDataThatDoesNotFit()
    {
      var encoder = new QrEncoder();
      var payload = Filled(27, 0x11);
      foreach (int version in new[] { 0, 1, 3, 5, 7, 40 })
      {
        Assert.That(encoder.Encode(payload.AsSpan(0, 4), version), Is.False, $"version {version}");
        Assert.That(encoder.EncodeWithMask(payload.AsSpan(0, 4), version, 0), Is.False, $"version {version}");
      }
      Assert.That(encoder.Encode(payload, 2), Is.False, "27 bytes in version 2");
      Assert.That(encoder.EncodeWithMask(payload, 2, 0), Is.False);
      Assert.That(encoder.Encode(new byte[WireFormat.QrCapacityBytes + 1], 6), Is.False, "107 bytes in version 6");
      Assert.That(encoder.Size, Is.Zero, "a refused symbol is left as it was");

      Assert.That(encoder.Encode(payload.AsSpan(0, WireFormat.SyncQrCapacityBytes), 2), Is.True);
      Assert.That(encoder.Size, Is.EqualTo(ModuleMatrix.SyncSize));
      Assert.That(encoder.Encode(payload, 2), Is.False);
      Assert.That(encoder.Size, Is.EqualTo(ModuleMatrix.SyncSize), "a refused symbol leaves the last one");
    }

    [Test]
    public void RefusesAMaskOutsideZeroToSeven()
    {
      var encoder = new QrEncoder();
      var payload = new byte[] { 1, 2, 3, 4 };

      Assert.That(encoder.EncodeWithMask(payload, 6, -1), Is.False);
      Assert.That(encoder.EncodeWithMask(payload, 6, 8), Is.False);
      Assert.That(encoder.Size, Is.Zero);
      Assert.That(encoder.EncodeWithMask(payload, 6, 7), Is.True);
      Assert.That(encoder.Size, Is.EqualTo(ModuleMatrix.MainSize));
    }

    [Test]
    public void FinderPatternsByRunsFindsEveryUnit()
    {
      // 1:1:3:1:1 with four units of light on one side counts once, on both sides twice, with less than one unit on a side not at all
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(4, 1, 1, 3, 1, 1, 4, 1, 1, 1, 1, 1, 1, 1, 1), 25), Is.EqualTo(2));
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1, 1), 25), Is.EqualTo(1), "the border is light");
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 1, 3, 1, 1, 3, 1, 1, 3, 1, 1), 25), Is.Zero);
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 3, 3, 9, 3, 3, 4), 25), Is.EqualTo(2), "unit 3, to both borders");
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 1, 2, 3, 3, 9, 3, 3, 12, 5), 41), Is.Zero, "two light modules before a unit of 3");
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 1, 3, 3, 3, 9, 3, 3, 12, 4), 41), Is.EqualTo(1));
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(12, 3, 3, 9, 3, 3, 2, 6), 41), Is.Zero, "two light modules after a unit of 3");
      Assert.That(QrEncoder.FinderPatternsByRuns(LineOf(0, 5, 5, 15, 5, 5, 6), 41), Is.EqualTo(2), "unit 5");
      Assert.That(QrEncoder.FinderPatternsByRuns(0, 41), Is.Zero);
      Assert.That(QrEncoder.FinderPatternsByRuns(ulong.MaxValue << 23, 41), Is.Zero);
    }

    [Test]
    public void PenaltyScoreEqualsTheReferencesForDrawnSymbols()
    {
      var random = new Random(1681);
      var reference = new ReferenceQrEncoder();
      var rows = new ulong[QrEncoder.MaxSize];
      var columns = new ulong[QrEncoder.MaxSize];
      foreach (int size in new[] { ModuleMatrix.SyncSize, ModuleMatrix.MainSize })
      {
        var crafted = FinderLikeLines(size);
        for (int i = 0; i < 3000; ++i)
        {
          reference.Clear(size);
          Array.Clear(rows, 0, rows.Length);
          Array.Clear(columns, 0, columns.Length);
          int kind = i % 9;
          int first = random.Next(crafted.Count);
          for (int y = 0; y < size; ++y)
          {
            ulong randomBits = NextLine(random);
            // Every third random row gets a dark run of nine or more
            if (kind == 8 && y % 3 == 0)
            {
              int length = 9 + random.Next(4);
              randomBits |= ((1UL << length) - 1) << random.Next(size - length + 1);
            }
            for (int x = 0; x < size; ++x)
            {
              bool randomBit = ((randomBits >> x) & 1) != 0;
              ulong line = crafted[(first + (kind == 6 ? y : x)) % crafted.Count];
              bool dark = kind switch
              {
                0 => randomBit && ((randomBits >> (x + 20)) & 1) != 0, // sparse
                1 => randomBit || ((randomBits >> (x + 20)) & 1) != 0, // dense
                2 => ((x / 5) & 1) != 0, // vertical stripes, five wide
                3 => (((y / 4) + (x / 7)) & 1) != 0, // blocks
                4 => (x * y) % 11 != 0, // all dark but a few
                6 => (line & QrEncoder.Bit(x)) != 0, // finder-like rows
                7 => (line & QrEncoder.Bit(y)) != 0, // finder-like columns
                _ => randomBit, // random
              };
              if (dark)
              {
                reference.SetModule(x, y, true);
                rows[y] |= QrEncoder.Bit(x);
                columns[x] |= QrEncoder.Bit(y);
              }
            }
          }

          Assert.That(QrEncoder.PenaltyScore(rows, columns, size), Is.EqualTo(reference.PenaltyScore()), $"size {size}, symbol {i}");
        }
      }
    }

    [Test]
    public void PenaltyScoreStopsAtTheLimit()
    {
      var encoder = new QrEncoder();
      Assert.That(encoder.EncodeWithMask(new byte[] { 0x4D, 0x46, 0x01, 0x00, 0x2A }, 6, 3), Is.True);
      var rows = new ulong[QrEncoder.MaxSize];
      var columns = new ulong[QrEncoder.MaxSize];
      CopySymbol(encoder, rows, columns);
      int whole = QrEncoder.PenaltyScore(rows, columns, encoder.Size);
      Assert.That(whole, Is.GreaterThan(100));

      // Above the score nothing stops; at or below it the result is at least the limit, and not more than the whole score
      Assert.That(QrEncoder.PenaltyScore(rows, columns, encoder.Size, whole + 1), Is.EqualTo(whole));
      foreach (int limit in new[] { 0, 1, whole / 3, whole / 2, whole - 1 })
      {
        int stopped = QrEncoder.PenaltyScore(rows, columns, encoder.Size, limit);
        Assert.That(stopped, Is.InRange(limit, whole), $"limit {limit}");
      }
      Assert.That(QrEncoder.PenaltyScore(rows, columns, encoder.Size, 0), Is.LessThan(whole), "a limit of 0 stops before the first line");
    }

    [Test]
    public void TablesPlaceEveryCodewordBitOnADataModuleOfItsOwn()
    {
      foreach (var tables in new[] { QrVersionTables.Version2, QrVersionTables.Version6 })
      {
        Assert.That(QrVersionTables.For(tables.Version), Is.SameAs(tables));
        Assert.That(tables.Size, Is.EqualTo((4 * tables.Version) + 17));
        Assert.That(tables.DataCodewordCount + (tables.BlockCount * QrReedSolomon.EccCodewordsPerBlock), Is.EqualTo(tables.CodewordCount));

        var placed = new HashSet<int>();
        foreach (ushort module in tables.CodewordModules)
        {
          int x = QrVersionTables.ModuleX(module);
          int y = QrVersionTables.ModuleY(module);
          Assert.That(QrVersionTables.IsFunctionModule(tables.Size, x, y), Is.False);
          Assert.That(placed.Add(module), Is.True, "a module used twice");
          Assert.That((tables.DataRows[y] & QrEncoder.Bit(x)) != 0 && (tables.DataColumns[x] & QrEncoder.Bit(y)) != 0, Is.True);
        }
        // Versions 2 to 6 have seven data modules more than their codewords need
        Assert.That(tables.DataRows.Sum(BitUtil.PopCount), Is.EqualTo(tables.CodewordModules.Length + 7));
        Assert.That(tables.DataColumns.Sum(BitUtil.PopCount), Is.EqualTo(tables.CodewordModules.Length + 7));
        Assert.That(
          tables.FormatModules.Select(m => QrVersionTables.FormatBitOf(m)),
          Is.EqualTo(Enumerable.Range(0, 15).Concat(Enumerable.Range(0, 15)))
        );
      }
      Assert.That(QrVersionTables.For(1), Is.Null);
      Assert.That(QrVersionTables.For(7), Is.Null);
    }

    [Test]
    public void MaskPatternsRepeatEveryTwelveRowsAndColumns()
    {
      for (int mask = 0; mask < QrMaskPatterns.MaskCount; ++mask)
      {
        for (int y = 0; y < QrEncoder.MaxSize; ++y)
        {
          for (int x = 0; x < QrEncoder.MaxSize; ++x)
          {
            bool inverts = QrMaskPatterns.MaskInverts(mask, x, y);
            ulong row = QrMaskPatterns.RowPatterns(mask)[y % QrMaskPatterns.MaskPeriod];
            ulong column = QrMaskPatterns.ColumnPatterns(mask)[x % QrMaskPatterns.MaskPeriod];
            Assert.That((row & QrEncoder.Bit(x)) != 0, Is.EqualTo(inverts), $"mask {mask} row {y} module {x}");
            Assert.That((column & QrEncoder.Bit(y)) != 0, Is.EqualTo(inverts), $"mask {mask} column {x} module {y}");
          }
        }
      }
      // Level M with mask 0 and mask 7, as the standard's table of format information has them
      Assert.That(QrMaskPatterns.FormatBits(0), Is.EqualTo(0x5412));
      Assert.That(QrMaskPatterns.FormatBits(7), Is.EqualTo(0x4AA0));
    }

    [Test]
    public void ReedSolomonLogarithmsMultiply()
    {
      for (int x = 1; x < 256; ++x)
      {
        for (int y = 1; y < 256; ++y)
        {
          int product = QrReedSolomon.Exp(QrReedSolomon.Log(x) + QrReedSolomon.Log(y));
          if (product != QrReedSolomon.Multiply(x, y))
            Assert.Fail($"{x} * {y}");
        }
      }
      Assert.That(QrReedSolomon.Multiply(0, 0x53), Is.Zero);
      Assert.That(QrReedSolomon.Multiply(0x53, 0), Is.Zero);

      // Zero data has a zero remainder
      var remainder = Filled(QrReedSolomon.EccCodewordsPerBlock, 0xFF);
      QrReedSolomon.ComputeErrorCorrection(new byte[27], remainder);
      Assert.That(remainder, Is.All.Zero);
    }

    [TestCase(0UL, 0, 64)]
    [TestCase(1UL, 1, 63)]
    [TestCase(ulong.MaxValue, 64, 0)]
    [TestCase(0x8000000000000000UL, 1, 0)]
    [TestCase(0x00F0000000000F00UL, 8, 8)]
    [TestCase(0x0123456789ABCDEFUL, 32, 7)]
    public void BitUtilCountsBits(ulong value, int popCount, int leadingZeroCount)
    {
      Assert.That(BitUtil.PopCount(value), Is.EqualTo(popCount));
      Assert.That(BitUtil.LeadingZeroCount(value), Is.EqualTo(leadingZeroCount));
    }
  }
}
