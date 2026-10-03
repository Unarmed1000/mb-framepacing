//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The answer to a QuestionViewModel: yes or no, and whether to remember it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Gui.ViewModels
{
  /// <param name="Yes">The answer that writes a video.</param>
  /// <param name="Remember">Store the answer in the configuration, so it is not asked again.</param>
  public sealed record QuestionAnswer(bool Yes, bool Remember);
}
