//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The name of a slice or a moment of the frame record's second part: the detail's prefix ("mbfp present " and so on) and the two ids of the
//* frame's marker as decimal numbers without leading zeros: "mbfp present 1 388". An application formats it (into its own buffer: nothing
//* is allocated), a reader of a trace parses it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Trace
{
  /// <summary>The name of a slice or a moment of the frame record's second part.</summary>
  public static class FrameDetailName
  {
    /// <summary>The length of the longest name: "mbfp not presented 4294967295 18446744073709551615".</summary>
    public const int MaxLength = 50;

    private static readonly FrameDetail[] g_details =
    {
      FrameDetail.Wait,
      FrameDetail.Present,
      FrameDetail.Gpu,
      FrameDetail.Display,
      FrameDetail.NotPresented,
    };

    /// <summary>What the name of a detail begins with, the space included; empty for a value that is no <see cref="FrameDetail"/>.</summary>
    public static string PrefixOf(FrameDetail detail) =>
      detail switch
      {
        FrameDetail.Wait => FrameRecordText.WaitNamePrefix,
        FrameDetail.Present => FrameRecordText.PresentNamePrefix,
        FrameDetail.Gpu => FrameRecordText.GpuNamePrefix,
        FrameDetail.Display => FrameRecordText.DisplayNamePrefix,
        FrameDetail.NotPresented => FrameRecordText.NotPresentedNamePrefix,
        _ => string.Empty,
      };

    /// <summary>The name for a detail, run id and frame index, as a new string; empty for a value that is no detail.</summary>
    public static string Format(FrameDetail detail, uint runId, ulong frameIndex)
    {
      Span<char> buffer = stackalloc char[MaxLength];
      return TryFormat(detail, runId, frameIndex, buffer, out int length) ? new string(buffer.Slice(0, length)) : string.Empty;
    }

    /// <summary>
    /// Write the name for a detail, run id and frame index to <paramref name="destination"/>; false, with nothing written, when it does not
    /// fit (<see cref="MaxLength"/> characters always do) or the detail is none. No terminating zero is written.
    /// </summary>
    public static bool TryFormat(FrameDetail detail, uint runId, ulong frameIndex, Span<char> destination, out int charsWritten)
    {
      charsWritten = 0;
      string prefix = PrefixOf(detail);
      if (prefix.Length == 0)
        return false;
      Span<char> ids = stackalloc char[FrameRecordName.MaxLength];
      // The record's name has the ids after its own prefix: the same digits, a space between
      FrameRecordName.TryFormat(runId, frameIndex, ids, out int idsLength);
      var digits = ids.Slice(FrameRecordText.NamePrefix.Length, idsLength - FrameRecordText.NamePrefix.Length);
      if (destination.Length < prefix.Length + digits.Length)
        return false;
      prefix.AsSpan().CopyTo(destination);
      digits.CopyTo(destination.Slice(prefix.Length));
      charsWritten = prefix.Length + digits.Length;
      return true;
    }

    /// <summary>The detail, run id and frame index of a <paramref name="name"/>; false for a name that is not of the second part's frames.</summary>
    public static bool TryParse(ReadOnlySpan<char> name, out FrameDetail detail, out uint runId, out ulong frameIndex)
    {
      foreach (var candidate in g_details)
      {
        string prefix = PrefixOf(candidate);
        if (!name.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
          continue;
        // The ids as the record's name has them
        Span<char> record = stackalloc char[FrameRecordText.NamePrefix.Length + name.Length - prefix.Length];
        FrameRecordText.NamePrefix.AsSpan().CopyTo(record);
        name.Slice(prefix.Length).CopyTo(record.Slice(FrameRecordText.NamePrefix.Length));
        detail = candidate;
        return FrameRecordName.TryParse(record, out runId, out frameIndex);
      }
      detail = default;
      runId = 0;
      frameIndex = 0;
      return false;
    }
  }
}
