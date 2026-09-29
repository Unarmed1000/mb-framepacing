//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Shapes of a report card that scroll with the time axis: a panel's bars, holds, lines, marks and time labels, which may reach beyond the
//* plot (the GUI prepares a wider window than it shows, so scrolling only moves them). The GUI clips them to Left..Right and Top..Bottom and
//* shifts them by its scroll offset; the SVG writer writes them where they are, as if they were not grouped.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  public sealed record ScrollShape(double Left, double Top, double Right, double Bottom, IReadOnlyList<CardShape> Children) : CardShape;
}
