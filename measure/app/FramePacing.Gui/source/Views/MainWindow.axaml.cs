//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Main window.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
      (DataContext as MainWindowViewModel)?.SaveSettings();
      base.OnClosing(e);
    }
  }
}
