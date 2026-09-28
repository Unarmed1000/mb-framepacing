//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which items a report card shows (ReportItem ids): all of them by default; Hide switches some off, ShowOnly keeps just the ones named. A
//* tile shows when neither it nor "tiles" is hidden.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts
{
  public sealed record ReportOptions
  {
    public static readonly ReportOptions Default = new ReportOptions();

    /// <summary>The ids of the items not shown.</summary>
    public IReadOnlySet<string> Hidden { get; init; } = new HashSet<string>();

    public bool IsShown(string id) => !Hidden.Contains(id) && !(ReportItem.TileIds.Contains(id) && Hidden.Contains(ReportItem.Tiles));

    /// <summary>These options with <paramref name="ids"/> hidden too.</summary>
    public ReportOptions Hide(IEnumerable<string> ids) => this with { Hidden = new HashSet<string>(Hidden.Concat(Validate(ids))) };

    /// <summary>Only <paramref name="ids"/> shown. Naming a tile keeps the tiles row for it; naming "tiles" keeps every tile.</summary>
    public static ReportOptions ShowOnly(IEnumerable<string> ids)
    {
      var shown = new HashSet<string>(Validate(ids));
      if (shown.Contains(ReportItem.Tiles))
        shown.UnionWith(ReportItem.TileIds);
      if (ReportItem.TileIds.Any(shown.Contains))
        shown.Add(ReportItem.Tiles);
      return new ReportOptions { Hidden = new HashSet<string>(ReportItem.All.Select(i => i.Id).Where(id => !shown.Contains(id))) };
    }

    /// <summary>Parse a comma separated list of ids ("late-share,refresh-strip").</summary>
    public static IReadOnlyList<string> ParseIds(string text) =>
      Validate(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();

    private static IEnumerable<string> Validate(IEnumerable<string> ids)
    {
      var list = ids.ToList();
      var unknown = list.Where(id => !ReportItem.IsKnown(id)).ToList();
      if (unknown.Count > 0)
        throw new ArgumentException(
          $"Unknown report item(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", ReportItem.All.Select(i => i.Id))}"
        );
      return list;
    }
  }
}
