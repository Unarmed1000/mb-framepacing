//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Stress test of the marker's QR encoder against the reference (Reference/ReferenceQrEncoder.cs), as the C++ library's
//* mb_framepacing_marker_qr_stress: payloads of many kinds on every core, every mask and its penalty score for a share of them,
//* TryGenerateModules with its packing, drawn symbols, and the line scoring for every line of 25 modules there is. The run is
//* deterministic and stops at the first difference. MB_QR_STRESS_SCALE multiplies the amount (1 by default: a few seconds).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Marker.Reference;
using NUnit.Framework;

namespace MB.FramePacing.Marker.UnitTest
{
  [TestFixture]
  public class QrEncoderStressTests
  {
    private const ulong Seed = 0x9E3779B97F4A7C15UL;

    private static readonly int g_threads = Math.Max(1, Environment.ProcessorCount);

    private static long Scale =>
      long.TryParse(Environment.GetEnvironmentVariable("MB_QR_STRESS_SCALE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long scale)
      && scale > 0
        ? scale
        : 1;

    /// <summary>xorshift64*: fast, and the same numbers everywhere.</summary>
    private sealed class Generator
    {
      private ulong m_state;

      public Generator(ulong seed) => m_state = seed != 0 ? seed : 1;

      public ulong Next()
      {
        m_state ^= m_state >> 12;
        m_state ^= m_state << 25;
        m_state ^= m_state >> 27;
        return m_state * 0x2545F4914F6CDD1DUL;
      }

      /// <summary>0 to bound - 1.</summary>
      public int Below(int bound) => (int)(Next() % (ulong)bound);
    }

    /// <summary>Run <paramref name="work"/> on every core; the first difference any of them reports fails the test.</summary>
    private static void OnEveryCore(Func<int, Generator, string?> work)
    {
      string? failure = null;
      Parallel.For(
        0,
        g_threads,
        thread =>
        {
          string? result = work(thread, new Generator(Seed + ((ulong)thread * 0xD1B54A32D192ED03UL)));
          if (result != null)
            Interlocked.CompareExchange(ref failure, result, null);
        }
      );
      Assert.That(failure, Is.Null);
    }

    private static int CapacityOf(int version) => version == 2 ? WireFormat.SyncQrCapacityBytes : WireFormat.QrCapacityBytes;

    private static string? Difference(QrEncoder encoder, ReferenceQrEncoder reference)
    {
      if (encoder.Size != reference.Size)
        return $"size {encoder.Size}, the reference's is {reference.Size}";
      for (int y = 0; y < reference.Size; ++y)
      {
        for (int x = 0; x < reference.Size; ++x)
        {
          bool expected = reference.IsDark(x, y);
          if (encoder.IsDark(x, y) != expected || ((encoder.Column(x) & QrEncoder.Bit(y)) != 0) != expected)
            return $"module ({x}, {y}) is {(expected ? "dark" : "light")} in the reference";
        }
      }
      return null;
    }

    /// <summary>The next payload: one of several kinds, some of them made from the one before.</summary>
    private static void NextPayload(Generator random, int version, List<byte> payload)
    {
      int capacity = CapacityOf(version);
      int kind = random.Below(10);
      if (kind >= 7 && payload.Count > 0 && payload.Count <= capacity)
      {
        // The payload before, a little different: one bit, one byte, or one byte more or less
        int at = random.Below(payload.Count);
        if (kind == 7)
          payload[at] = (byte)(payload[at] ^ (1 << random.Below(8)));
        else if (kind == 8)
          payload[at] = (byte)random.Next();
        else if (payload.Count < capacity && (random.Next() & 1) != 0)
          payload.Add((byte)random.Next());
        else
          payload.RemoveAt(payload.Count - 1);
        return;
      }

      // The lengths at the ends more often than their share
      int lengthKind = random.Below(8);
      int length = lengthKind switch
      {
        0 => 0,
        1 => capacity,
        2 => capacity - 1,
        _ => random.Below(capacity + 1),
      };
      payload.Clear();
      int period = 1 + random.Below(8);
      byte fill = (byte)random.Next();
      for (int i = 0; i < length; ++i)
      {
        byte value = (byte)random.Next();
        payload.Add(
          kind switch
          {
            0 or 1 => value, // any bytes
            2 => random.Below(6) == 0 ? value : (byte)0, // mostly zero, as a marker's bytes are
            3 => random.Below(6) == 0 ? value : (byte)0xFF, // mostly 0xFF
            4 => fill, // one byte, repeated
            5 => (byte)(fill + ((i % period) * 37)), // a short pattern, repeated
            _ => (byte)(value & (byte)random.Next() & (byte)random.Next()), // few bits set
          }
        );
      }
    }

    [Test]
    public void PayloadsOfEveryKindGiveTheReferencesSymbols()
    {
      long payloads = 24_000 * Scale / g_threads;
      OnEveryCore(
        (thread, random) =>
        {
          var encoder = new QrEncoder();
          var reference = new ReferenceQrEncoder();
          var rows = new ulong[QrEncoder.MaxSize];
          var columns = new ulong[QrEncoder.MaxSize];
          var previous = new[] { new List<byte>(), new List<byte>() };
          for (long i = 0; i < payloads; ++i)
          {
            int which = (int)(i & 1);
            int version = which == 0 ? 6 : 2;
            NextPayload(random, version, previous[which]);
            byte[] payload = previous[which].ToArray();
            // Now and then data that does not fit: both refuse it
            if ((i & 1023) == 0)
              payload = new byte[CapacityOf(version) + 1 + random.Below(4)];

            bool referenceEncoded = reference.Encode(payload, version, version);
            bool encoded = encoder.Encode(payload, version);
            if (encoded != referenceEncoded)
              return $"version {version}, payload {Convert.ToHexString(payload)}: only one encoder took it";
            if (!encoded)
              continue;
            if (Difference(encoder, reference) is { } why)
              return $"version {version}, payload {Convert.ToHexString(payload)}: {why}";

            // Every eighth payload with each of the eight masks, and its penalty score
            for (int mask = 0; (i & 7) == 0 && mask < QrMaskPatterns.MaskCount; ++mask)
            {
              if (!reference.Encode(payload, version, version, mask) || !encoder.EncodeWithMask(payload, version, mask))
                return $"version {version}, mask {mask}, payload {Convert.ToHexString(payload)}: refused";
              if (Difference(encoder, reference) is { } maskedWhy)
                return $"version {version}, mask {mask}, payload {Convert.ToHexString(payload)}: {maskedWhy}";
              for (int k = 0; k < encoder.Size; ++k)
              {
                rows[k] = encoder.Row(k);
                columns[k] = encoder.Column(k);
              }
              int score = QrEncoder.PenaltyScore(rows, columns, encoder.Size);
              if (score != reference.PenaltyScore())
                return $"version {version}, mask {mask}, payload {Convert.ToHexString(payload)}: penalty {score}, the reference's is {reference.PenaltyScore()}";
            }
          }
          return null;
        }
      );
    }

    [Test]
    public void MarkersGiveTheReferencesModules()
    {
      long markers = 24_000 * Scale / g_threads;
      OnEveryCore(
        (thread, random) =>
        {
          var generator = new MarkerGenerator();
          var reference = new ReferenceQrEncoder();
          var bits = new byte[ModuleMatrix.MaxPackedModuleByteCount];
          var payloadBytes = new byte[Payload.MaxEncodedByteCount];
          var sequenceId = new byte[16];
          ulong frameIndex = random.Next() >> 20;
          for (long i = 0; i < markers; ++i)
          {
            int kindChoice = random.Below(8);
            var kind =
              kindChoice == 0 ? MarkerKind.SequenceStart
              : kindChoice == 1 ? MarkerKind.SequenceEnd
              : kindChoice <= 3 ? MarkerKind.Sync
              : MarkerKind.Frame;
            // A running frame index and times, or anything at all
            bool running = random.Below(4) != 0;
            frameIndex = running ? frameIndex + 1 : random.Next();
            long time = running ? (long)(frameIndex * 16_666_667) : (long)random.Next();
            var payload = new Payload(
              kind,
              running ? 0x12345678u : (uint)random.Next(),
              frameIndex,
              (MarkerFlags)(running ? random.Below(4) : random.Below(256)),
              new NanosecondTimeSpan(running ? time : time / 4),
              NanosecondTimeDuration.FromNanoseconds(running ? 16_666_667u : (uint)random.Next()),
              NanosecondTimeDuration.FromNanoseconds(running ? 16_666_667u : (uint)random.Next()),
              new NanosecondTickCount(running ? 3_600_000_000_000 + time : time),
              new NanosecondTickCount(running ? 3_599_990_000_000 + time : time / 2),
              NanosecondTimeDuration.FromNanoseconds((uint)random.Below(40_000_000))
            );
            for (int k = 0; k < sequenceId.Length; ++k)
              sequenceId[k] = (byte)random.Next();
            var metadata = new StartMetadata((long)(random.Next() >> 2), SequenceId.FromBytes(sequenceId));

            int byteCount = FrameMarker.EncodePayload(payload, metadata, payloadBytes);
            int version = kind == MarkerKind.Sync ? WireFormat.SyncQrVersion : WireFormat.QrVersion;
            if (
              !generator.TryGenerateModules(payload, metadata, bits, out var matrix)
              || !reference.Encode(payloadBytes.AsSpan(0, byteCount), version, version)
            )
              return $"a marker was not encoded: payload bytes {Convert.ToHexString(payloadBytes, 0, byteCount)}";
            bool same = matrix.Size == reference.Size;
            for (int y = 0; same && y < reference.Size; ++y)
            {
              for (int x = 0; same && x < reference.Size; ++x)
                same = matrix.IsDark(x, y) == reference.IsDark(x, y);
            }
            if (!same)
              return $"TryGenerateModules differs from the reference: payload bytes {Convert.ToHexString(payloadBytes, 0, byteCount)}";
          }
          return null;
        }
      );
    }

    /// <summary>A line of runs: lengths that make finder-like patterns likely (1, 1, 3, 1, 1 times a unit), long runs, or anything.</summary>
    private static ulong StructuredLine(Generator random, int size)
    {
      ReadOnlySpan<int> finderRuns = stackalloc int[] { 1, 1, 3, 1, 1, 4, 1, 2 };
      ulong line = 0;
      int position = 0;
      bool dark = (random.Next() & 1) != 0;
      int unit = 1 + random.Below(5);
      int kind = random.Below(4);
      while (position < size)
      {
        int length = kind switch
        {
          0 => finderRuns[random.Below(finderRuns.Length)] * unit,
          1 => 1 + random.Below(4),
          2 => 1 + random.Below(14),
          _ => random.Below(3) == 0 ? unit * 3 : unit,
        };
        for (int i = 0; i < length && position < size; ++i, ++position)
        {
          if (dark)
            line |= QrEncoder.Bit(position);
        }
        dark = !dark;
      }
      return line;
    }

    [Test]
    public void DrawnSymbolsScoreAsTheReference()
    {
      long symbols = 16_000 * Scale / g_threads;
      OnEveryCore(
        (thread, random) =>
        {
          var reference = new ReferenceQrEncoder();
          var rows = new ulong[QrEncoder.MaxSize];
          var columns = new ulong[QrEncoder.MaxSize];
          for (long n = 0; n < symbols; ++n)
          {
            int size = (random.Next() & 1) != 0 ? ModuleMatrix.MainSize : ModuleMatrix.SyncSize;
            reference.Clear(size);
            Array.Clear(rows, 0, rows.Length);
            Array.Clear(columns, 0, columns.Length);
            void SetDark(int x, int y)
            {
              reference.SetModule(x, y, true);
              rows[y] |= QrEncoder.Bit(x);
              columns[x] |= QrEncoder.Bit(y);
            }

            bool asColumns = (random.Next() & 1) != 0;
            bool randomLines = random.Below(4) == 0;
            // One in eight: a dark share right on a step of the balance rule (a multiple of 5 % of the modules), which random symbols never hit
            if (random.Below(8) == 0)
            {
              int wanted = random.Below(21) * size * size / 20;
              for (int dark = 0; dark < wanted; )
              {
                int x = random.Below(size);
                int y = random.Below(size);
                if (!reference.IsDark(x, y))
                {
                  SetDark(x, y);
                  ++dark;
                }
              }
            }
            else
            {
              for (int i = 0; i < size; ++i)
              {
                ulong line = randomLines ? random.Next() & random.Next() : StructuredLine(random, size);
                for (int k = 0; k < size; ++k)
                {
                  if ((line & QrEncoder.Bit(k)) != 0)
                    SetDark(asColumns ? i : k, asColumns ? k : i);
                }
              }
            }

            int score = QrEncoder.PenaltyScore(rows, columns, size);
            if (score != reference.PenaltyScore())
              return $"a drawn symbol of {size} modules: penalty {score}, the reference's is {reference.PenaltyScore()}; rows {string.Join(' ', rows)}";
          }
          return null;
        }
      );
    }

    /// <summary>The penalty of a line as the reference scores a row: module by module, with its run history.</summary>
    private static int LinePenaltyByModules(ulong line, int size, int[] history)
    {
      void AddRun(int length)
      {
        if (history[0] == 0)
          length += size;
        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = length;
      }
      int CountPatterns()
      {
        int n = history[1];
        bool core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
        return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
      }

      Array.Clear(history, 0, history.Length);
      int result = 0;
      bool runColor = false;
      int run = 0;
      for (int x = 0; x < size; ++x)
      {
        bool dark = (line & QrEncoder.Bit(x)) != 0;
        if (dark == runColor)
        {
          ++run;
          if (run == 5)
            result += 3;
          else if (run > 5)
            ++result;
        }
        else
        {
          AddRun(run);
          if (!runColor)
            result += CountPatterns() * 40;
          runColor = dark;
          run = 1;
        }
      }
      if (runColor)
      {
        AddRun(run);
        run = 0;
      }
      AddRun(run + size);
      return result + (CountPatterns() * 40);
    }

    [Test]
    public void LinesScoreAsModuleByModule()
    {
      long lines = 4_000_000 * Scale / g_threads;
      OnEveryCore(
        (thread, random) =>
        {
          var history = new int[7];
          for (long i = 0; i < lines; ++i)
          {
            int size = (i & 3) == 0 ? ModuleMatrix.SyncSize : ModuleMatrix.MainSize;
            ulong line = (i & 1) == 0 ? StructuredLine(random, size) : random.Next() & (ulong.MaxValue << (64 - size));
            int expected = LinePenaltyByModules(line, size, history);
            int actual = QrEncoder.LinePenalty(line, size);
            if (actual != expected)
              return $"a line of {size} modules, bits {line >> (64 - size)}: penalty {actual}, module by module it is {expected}";
          }
          return null;
        }
      );
    }

    [Test]
    public void EveryLineOf25ModulesScoresAsModuleByModule()
    {
      OnEveryCore(
        (thread, random) =>
        {
          var history = new int[7];
          for (ulong bits = (ulong)thread; bits < 1UL << 25; bits += (ulong)g_threads)
          {
            ulong line = bits << 39;
            int expected = LinePenaltyByModules(line, ModuleMatrix.SyncSize, history);
            int actual = QrEncoder.LinePenalty(line, ModuleMatrix.SyncSize);
            if (actual != expected)
              return $"the line of 25 modules with bits {bits}: penalty {actual}, module by module it is {expected}";
          }
          return null;
        }
      );
    }
  }
}
