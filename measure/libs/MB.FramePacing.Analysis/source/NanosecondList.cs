//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A list of times in nanoseconds for the statistics of a run: its values are gathered, sorted once (NanosecondSort) and read where they
//* are. Its array is its own, not a rented one: a run of an hour has a dozen such lists of a million values at once, and a pool would
//* keep them all after the analysis.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  internal sealed class NanosecondList
  {
    private long[] m_values;
    private int m_count;

    /// <param name="capacity">How many values to make room for; more can be added.</param>
    public NanosecondList(int capacity = 0)
    {
      m_values = capacity > 0 ? GC.AllocateUninitializedArray<long>(capacity) : Array.Empty<long>();
    }

    public int Count => m_count;

    /// <summary>The values, in the order they were added or sorted into.</summary>
    public Span<long> Values => m_values.AsSpan(0, m_count);

    public void Add(long nanoseconds)
    {
      if (m_count == m_values.Length)
      {
        var larger = GC.AllocateUninitializedArray<long>(Math.Max(256, m_values.Length * 2));
        Values.CopyTo(larger);
        m_values = larger;
      }
      m_values[m_count++] = nanoseconds;
    }

    public void Add(NanosecondTimeSpan span) => Add(span.Nanoseconds);

    /// <summary>Sort the values ascending.</summary>
    public void Sort() => NanosecondSort.Sort(Values);
  }
}
