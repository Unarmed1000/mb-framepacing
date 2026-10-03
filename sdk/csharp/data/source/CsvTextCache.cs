//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The values made from a CSV cell's text that a file repeats on every line (a status name, a set of flags): made once per distinct text
//* and looked up by the cell's characters, so a file of a million lines shares a handful of them instead of making one per line. It
//* holds a limited number of texts; beyond that a value is made for its line alone.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Data
{
  internal sealed class CsvTextCache<T>
  {
    private const int MaxEntries = 256;

    private readonly Dictionary<string, T> m_values = new Dictionary<string, T>(StringComparer.Ordinal);
    private readonly Dictionary<string, T>.AlternateLookup<ReadOnlySpan<char>> m_lookup;
    private readonly Func<string, T> m_create;

    /// <param name="create">Makes the value of a text; what it throws for a text that is not one goes to the caller, and nothing is kept.</param>
    public CsvTextCache(Func<string, T> create)
    {
      m_create = create ?? throw new ArgumentNullException(nameof(create));
      m_lookup = m_values.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>The value of <paramref name="text"/>.</summary>
    public T Get(ReadOnlySpan<char> text)
    {
      if (m_lookup.TryGetValue(text, out var value))
        return value;
      string key = text.ToString();
      value = m_create(key);
      if (m_values.Count < MaxEntries)
        m_values[key] = value;
      return value;
    }
  }
}
