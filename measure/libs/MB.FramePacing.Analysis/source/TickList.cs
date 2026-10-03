//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A list of ticks for the statistics of a run: its values are gathered, sorted once (TickSort) and read where they are. Its array is
//* its own, not a rented one: a run of an hour has a dozen such lists of a million values at once, and a pool would keep them all
//* after the analysis.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  internal sealed class TickList
  {
    private long[] m_ticks;
    private int m_count;

    /// <param name="capacity">How many values to make room for; more can be added.</param>
    public TickList(int capacity = 0)
    {
      m_ticks = capacity > 0 ? GC.AllocateUninitializedArray<long>(capacity) : Array.Empty<long>();
    }

    public int Count => m_count;

    /// <summary>The values, in the order they were added or sorted into.</summary>
    public Span<long> Values => m_ticks.AsSpan(0, m_count);

    public void Add(long ticks)
    {
      if (m_count == m_ticks.Length)
      {
        var larger = GC.AllocateUninitializedArray<long>(Math.Max(256, m_ticks.Length * 2));
        Values.CopyTo(larger);
        m_ticks = larger;
      }
      m_ticks[m_count++] = ticks;
    }

    public void Add(TimeSpan span) => Add(span.Ticks);

    /// <summary>Sort the values ascending.</summary>
    public void Sort() => TickSort.Sort(Values);
  }
}
