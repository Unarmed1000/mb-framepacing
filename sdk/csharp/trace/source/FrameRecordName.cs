//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (the trace module, sdk/doc/trace-frame-record.md). The name of a frame's slice: "mbfp frame <run id> <frame index>", the
//* two ids of the frame's marker as decimal numbers without leading zeros. An application formats it (into its own buffer: nothing is
//* allocated), a reader of a trace parses it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. The name of a frame's slice.</summary>
  public static class FrameRecordName
  {
    /// <summary>The length of the longest name: "mbfp frame 4294967295 18446744073709551615".</summary>
    public const int MaxLength = 42;

    /// <summary>The name for a run id and frame index, as a new string. In a frame loop use <see cref="TryFormat"/>.</summary>
    public static string Format(uint runId, ulong frameIndex)
    {
      Span<char> buffer = stackalloc char[MaxLength];
      TryFormat(runId, frameIndex, buffer, out int length);
      return new string(buffer.Slice(0, length));
    }

    /// <summary>
    /// Write the name for a run id and frame index to <paramref name="destination"/>; false, with nothing written, when it does not fit
    /// (<see cref="MaxLength"/> characters always do). No terminating zero is written.
    /// </summary>
    public static bool TryFormat(uint runId, ulong frameIndex, Span<char> destination, out int charsWritten)
    {
      charsWritten = 0;
      int length = FrameRecordText.NamePrefix.Length + DigitCount(runId) + 1 + DigitCount(frameIndex);
      if (destination.Length < length)
        return false;
      FrameRecordText.NamePrefix.AsSpan().CopyTo(destination);
      int position = FrameRecordText.NamePrefix.Length;
      position += WriteDigits(runId, destination.Slice(position));
      destination[position++] = ' ';
      position += WriteDigits(frameIndex, destination.Slice(position));
      charsWritten = position;
      return true;
    }

    /// <summary>The run id and the frame index of a slice's <paramref name="name"/>; false for a name that is not a frame's.</summary>
    public static bool TryParse(ReadOnlySpan<char> name, out uint runId, out ulong frameIndex)
    {
      runId = 0;
      frameIndex = 0;
      if (!name.StartsWith(FrameRecordText.NamePrefix.AsSpan(), StringComparison.Ordinal))
        return false;
      var numbers = name.Slice(FrameRecordText.NamePrefix.Length);
      int space = numbers.IndexOf(' ');
      return space > 0
        && uint.TryParse(numbers.Slice(0, space), NumberStyles.None, CultureInfo.InvariantCulture, out runId)
        && ulong.TryParse(numbers.Slice(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out frameIndex);
    }

    private static int DigitCount(ulong value)
    {
      int count = 1;
      while (value >= 10)
      {
        value /= 10;
        ++count;
      }
      return count;
    }

    private static int WriteDigits(ulong value, Span<char> destination)
    {
      int count = DigitCount(value);
      for (int i = count - 1; i >= 0; --i)
      {
        destination[i] = (char)('0' + (int)(value % 10));
        value /= 10;
      }
      return count;
    }
  }
}
