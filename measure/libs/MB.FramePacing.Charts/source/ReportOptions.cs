//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which items a report card shows (ReportItem ids): all of them by default; Hide switches some off, ShowOnly keeps just the ones named. A
//* tile shows when neither it nor "tiles" is hidden. An overlay is opt-in: it shows when Show (or ShowOnly) names it and its panel shows.
//* Also how the card is laid out, for a card in a document: its own title, the refresh strip over only the first seconds, tiles and panels
//* with nothing to show left out, and the tiles per row. The defaults draw the standard card.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Charts
{
  public sealed record ReportOptions
  {
    /// <summary>At least this many tiles in a row when the row length follows the tiles (<see cref="TilesPerRow"/> null).</summary>
    public const int MinAutoTilesPerRow = 4;
    public const int MaxTilesPerRow = 8;

    public static readonly ReportOptions Default = new ReportOptions();

    private readonly double? m_stripSeconds;
    private readonly int? m_tilesPerRow;

    /// <summary>The ids of the items not shown.</summary>
    public IReadOnlySet<string> Hidden { get; init; } = new HashSet<string>();

    /// <summary>The opt-in overlays asked for (<see cref="ReportItem.Overlays"/>), and the auto tiles to show whatever they count.</summary>
    public IReadOnlySet<string> Shown { get; init; } = new HashSet<string>();

    /// <summary>The card's title instead of the run's ("Run 1  'name'"); a section's time range is still added.</summary>
    public string? Title { get; init; }

    /// <summary>The refresh strip covers only the section's first this many seconds (its own time axis), instead of the whole section.</summary>
    public double? StripSeconds
    {
      get => m_stripSeconds;
      init =>
        m_stripSeconds = value is null or > 0
          ? value
          : throw new ArgumentOutOfRangeException(nameof(StripSeconds), value, "The refresh strip's seconds must be above 0");
    }

    /// <summary>Leave out the tiles without a value (too few frames, no pacing) and the late share when no frame is late.</summary>
    public bool HideEmpty { get; init; }

    /// <summary>
    /// How many headline tiles go in a row (1 to <see cref="MaxTilesPerRow"/>); null (the default): two rows, half the tiles each, at least
    /// <see cref="MinAutoTilesPerRow"/> (<see cref="TilesPerRowFor"/>).
    /// </summary>
    public int? TilesPerRow
    {
      get => m_tilesPerRow;
      init =>
        m_tilesPerRow = value is null or (>= 1 and <= MaxTilesPerRow)
          ? value
          : throw new ArgumentOutOfRangeException(nameof(TilesPerRow), value, $"The tiles per row must be 1 to {MaxTilesPerRow}");
    }

    /// <summary>The tiles per row for <paramref name="tiles"/> tiles: <see cref="TilesPerRow"/>, else half of them (rounded up), 4 to 8.</summary>
    public int TilesPerRowFor(int tiles) => TilesPerRow ?? Math.Clamp((tiles + 1) / 2, MinAutoTilesPerRow, MaxTilesPerRow);

    public bool IsShown(string id)
    {
      if (Hidden.Contains(id) || (ReportItem.TileIds.Contains(id) && Hidden.Contains(ReportItem.Tiles)))
        return false;
      return !ReportItem.Overlays.TryGetValue(id, out string? panel) || (Shown.Contains(id) && IsShown(panel));
    }

    /// <summary>These options with <paramref name="ids"/> hidden too.</summary>
    public ReportOptions Hide(IEnumerable<string> ids) => this with { Hidden = new HashSet<string>(Hidden.Concat(Validate(ids))) };

    /// <summary>
    /// These options with the overlays <paramref name="ids"/> shown too (on their panels, when those show), and the auto tiles among them
    /// shown whatever they count.
    /// </summary>
    public ReportOptions Show(IEnumerable<string> ids)
    {
      var list = Validate(ids).ToList();
      var notOptIn = list.Where(id => !ReportItem.Overlays.ContainsKey(id) && !ReportItem.AutoTiles.Contains(id)).ToList();
      if (notOptIn.Count > 0)
        throw new ArgumentException(
          $"Only overlays and the auto tiles are opt-in; {string.Join(", ", notOptIn)} show by default. Opt-in: "
            + string.Join(", ", ReportItem.Overlays.Keys.Concat(ReportItem.AutoTiles))
        );
      return this with { Shown = new HashSet<string>(Shown.Concat(list)), Hidden = new HashSet<string>(Hidden.Except(list)) };
    }

    /// <summary>
    /// Only <paramref name="ids"/> shown. Naming a tile keeps the tiles row for it; naming "tiles" keeps every tile; naming an overlay keeps
    /// its panel.
    /// </summary>
    public static ReportOptions ShowOnly(IEnumerable<string> ids)
    {
      var shown = new HashSet<string>(Validate(ids));
      if (shown.Contains(ReportItem.Tiles))
        shown.UnionWith(ReportItem.TileIds);
      if (ReportItem.TileIds.Any(shown.Contains))
        shown.Add(ReportItem.Tiles);
      foreach (var (overlay, panel) in ReportItem.Overlays)
      {
        if (shown.Contains(overlay))
          shown.Add(panel);
      }
      return new ReportOptions
      {
        Hidden = new HashSet<string>(ReportItem.All.Select(i => i.Id).Where(id => !shown.Contains(id))),
        Shown = new HashSet<string>(shown.Where(ReportItem.Overlays.ContainsKey)),
      };
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
