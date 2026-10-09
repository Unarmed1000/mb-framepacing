//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A stand-in for the trace processor in the test of the read-back check: answers what a trace has under a root track and what errors it
//* counts, and remembers what it was asked.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Trace.UnitTest
{
  public sealed class StubQueries : ITraceQueries
  {
    private readonly SortedDictionary<string, long> m_slices;
    private readonly SortedDictionary<string, long> m_values;
    private readonly SortedDictionary<string, long> m_errors;

    public StubQueries(Dictionary<string, long> slices, Dictionary<string, long> values, Dictionary<string, long> errors)
    {
      m_slices = new SortedDictionary<string, long>(slices, StringComparer.Ordinal);
      m_values = new SortedDictionary<string, long>(values, StringComparer.Ordinal);
      m_errors = new SortedDictionary<string, long>(errors, StringComparer.Ordinal);
    }

    public List<string> Asked { get; } = new List<string>();

    public TraceQueryResult Query(string sql)
    {
      Asked.Add(sql);
      var counts =
        sql.Contains("FROM stats", StringComparison.Ordinal) ? m_errors
        : sql.Contains("FROM counter c", StringComparison.Ordinal) ? m_values
        : m_slices;
      return QueryReply.Result(new[] { "name", "count" }, counts.Select(pair => new object?[] { pair.Key, pair.Value }).ToArray());
    }
  }
}
