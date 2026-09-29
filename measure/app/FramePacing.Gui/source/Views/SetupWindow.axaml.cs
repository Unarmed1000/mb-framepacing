//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Setup dialog window. Returns true from ShowDialog when the settings were saved.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class SetupWindow : Window
  {
    public SetupWindow()
    {
      InitializeComponent();
      DataContextChanged += (_, _) =>
      {
        if (DataContext is SetupViewModel viewModel)
          viewModel.CloseRequested += saved => Close(saved);
      };
    }
  }
}
