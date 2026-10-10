//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Something that answers SQL about one trace: Perfetto's trace processor (TraceProcessor), or a test's stand-in.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Answers SQL about one trace.</summary>
  public interface ITraceQueries
  {
    /// <summary>The rows of <paramref name="sql"/>. A query that is refused is a <see cref="TraceException"/>.</summary>
    TraceQueryResult Query(string sql);
  }
}
