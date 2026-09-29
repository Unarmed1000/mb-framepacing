//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Shows a text in capitals, as the report card's labels and tile captions are (ReportCard writes them with ToUpperInvariant).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MB.FramePacing.Gui.Views
{
  public sealed class UpperCaseConverter : IValueConverter
  {
    public static readonly UpperCaseConverter Instance = new UpperCaseConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
  }
}
