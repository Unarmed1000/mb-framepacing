//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The camera rig wizard window (VERY EXPERIMENTAL camera support). Closes with true when a camera was set up or chosen.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class CameraWizardWindow : Window
  {
    public CameraWizardWindow()
    {
      InitializeComponent();
      DataContextChanged += (_, _) =>
      {
        if (DataContext is CameraWizardViewModel viewModel)
          viewModel.CloseRequested += done => Close(done);
      };
    }
  }
}
