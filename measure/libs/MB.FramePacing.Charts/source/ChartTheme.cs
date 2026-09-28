//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The chart colours: light (report files, the GUI's light theme) and dark (the GUI's dark theme, the README images).
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using ScottPlot;

namespace MB.FramePacing.Charts
{
  /// <param name="Background">Figure and data area.</param>
  /// <param name="Foreground">Axes, labels and legend text; the zero line, the risers and the error threshold band are drawn in it, faded.</param>
  /// <param name="Grid">Major grid lines and the legend outline.</param>
  /// <param name="StripEven">Refresh strip: every other frame.</param>
  /// <param name="StripOdd">Refresh strip: the frames between.</param>
  /// <param name="StripUnknown">Refresh strip: captures between frames that could not be decoded.</param>
  /// <param name="OnTime">Display time step: a frame held as planned (the next frame is not late).</param>
  /// <param name="Warning">Headline numbers that are a problem (frames off, late frames), as the GUI's tiles show them.</param>
  public sealed record ChartTheme(
    Color Background,
    Color Foreground,
    Color Grid,
    Color StripEven,
    Color StripOdd,
    Color StripUnknown,
    Color OnTime,
    Color Warning
  )
  {
    /// <summary>Late frames, frames held too long and animation error: the one red, in every chart and theme.</summary>
    public static readonly Color Late = Color.FromHex("#E5534B");

    public static readonly ChartTheme Light = new ChartTheme(
      Color.FromHex("#FFFFFF"),
      Color.FromHex("#2B2F36"),
      Color.FromHex("#E6E8EC"),
      Color.FromHex("#2F6DB5"),
      Color.FromHex("#9CC2EC"),
      Color.FromHex("#D5D8DD"),
      Color.FromHex("#1A7F37"),
      Color.FromHex("#9A6700")
    );

    public static readonly ChartTheme Dark = new ChartTheme(
      Color.FromHex("#202328"),
      Color.FromHex("#D6DAE0"),
      Color.FromHex("#33373E"),
      Color.FromHex("#4C8DD6"),
      Color.FromHex("#8DB8E8"),
      Color.FromHex("#3A3E45"),
      Color.FromHex("#2EA043"),
      Color.FromHex("#D29922")
    );
  }
}
