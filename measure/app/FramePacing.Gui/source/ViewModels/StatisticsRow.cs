//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One row of the detailed statistics table on the Analysis page (values formatted in milliseconds).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Globalization;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed record StatisticsRow(string Name, string Min, string Mean, string P50, string P95, string P99, string Max, string StdDev)
  {
    public static StatisticsRow From(string name, Statistics s) =>
      new StatisticsRow(name, F(s.Min), F(s.Mean), F(s.P50), F(s.P95), F(s.P99), F(s.Max), F(s.StdDev));

    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
  }
}
