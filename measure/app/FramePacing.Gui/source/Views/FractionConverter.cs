//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A share of a number, for bindings: the Timeline's scrollbar steps by a tenth of the range in view.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MB.FramePacing.Gui.Views
{
  public sealed class FractionConverter : IValueConverter
  {
    public static readonly FractionConverter Tenth = new FractionConverter(0.1);

    private readonly double m_fraction;

    public FractionConverter(double fraction)
    {
      m_fraction = fraction;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
      value is double number ? number * m_fraction : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
  }
}
