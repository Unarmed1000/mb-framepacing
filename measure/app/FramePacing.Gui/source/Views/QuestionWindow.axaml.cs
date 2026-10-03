//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A yes/no question dialog. ShowDialog returns the QuestionAnswer, or null when the window was closed without one.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class QuestionWindow : Window
  {
    public QuestionWindow()
    {
      InitializeComponent();
      DataContextChanged += (_, _) =>
      {
        if (DataContext is QuestionViewModel viewModel)
          viewModel.CloseRequested += answer => Close(answer);
      };
    }
  }
}
