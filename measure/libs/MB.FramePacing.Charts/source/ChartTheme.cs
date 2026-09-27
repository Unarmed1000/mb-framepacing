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
  /// <param name="Foreground">Axes, labels and legend text.</param>
  /// <param name="Grid">Major grid lines and the legend outline.</param>
  /// <param name="StripEven">Refresh strip: every other frame.</param>
  /// <param name="StripOdd">Refresh strip: the frames between.</param>
  /// <param name="StripUnknown">Refresh strip: captures between frames that could not be decoded.</param>
  public sealed record ChartTheme(Color Background, Color Foreground, Color Grid, Color StripEven, Color StripOdd, Color StripUnknown)
  {
    /// <summary>Late frames, in every chart and theme.</summary>
    public static readonly Color Late = Color.FromHex("#E4572E");

    public static readonly ChartTheme Light = new ChartTheme(
      Color.FromHex("#FFFFFF"),
      Color.FromHex("#2B2F36"),
      Color.FromHex("#E6E8EC"),
      Color.FromHex("#2F6DB5"),
      Color.FromHex("#9CC2EC"),
      Color.FromHex("#D5D8DD")
    );

    public static readonly ChartTheme Dark = new ChartTheme(
      Color.FromHex("#202328"),
      Color.FromHex("#D6DAE0"),
      Color.FromHex("#33373E"),
      Color.FromHex("#4C8DD6"),
      Color.FromHex("#8DB8E8"),
      Color.FromHex("#3A3E45")
    );
  }
}
