//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Parses capture durations such as "30s", "1500ms", "2m", "1h" or "00:00:30" (shared by the command line tool and the GUI).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Globalization;

namespace MB.FramePacing.Capture
{
  public static class DurationParser
  {
    public static TimeSpan Parse(string text)
    {
      text = text.Trim();
      (string Suffix, double Scale)[] units = [("ms", 0.001), ("s", 1), ("m", 60), ("h", 3600)];
      foreach (var (suffix, scale) in units)
      {
        if (
          text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
          && double.TryParse(text.AsSpan(0, text.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
          && value > 0
        )
          return TimeSpan.FromSeconds(value * scale);
      }
      if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero)
        return span;
      throw new FormatException($"Invalid duration '{text}' (use e.g. 30s, 1500ms, 2m or 00:00:30)");
    }

    /// <summary>Like <see cref="Parse"/>, but an empty text means "no limit" (null).</summary>
    public static TimeSpan? ParseOptional(string? text) => string.IsNullOrWhiteSpace(text) ? null : Parse(text);
  }
}
