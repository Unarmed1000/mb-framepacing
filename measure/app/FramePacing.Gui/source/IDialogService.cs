//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The dialogs and shell actions the view models need (file and folder pickers, opening URLs and files, the clipboard, the setup dialog), so
//* the view models do not depend on a window.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Threading.Tasks;

namespace MB.FramePacing.Gui
{
  public interface IDialogService
  {
    Task<string?> PickFolderAsync(string title, string? startDirectory);

    Task<string?> PickFileAsync(string title);

    void ShowInFileManager(string directory);

    /// <summary>Open a web page in the default browser.</summary>
    void OpenUrl(string url);

    /// <summary>Open a file with its default application (e.g. the configuration file in a text editor).</summary>
    void OpenFile(string path);

    Task CopyToClipboardAsync(string text);

    /// <summary>Show the setup dialog. True when the user saved the settings.</summary>
    Task<bool> ShowSetupAsync(ViewModels.SetupViewModel viewModel);

    /// <summary>Show the camera rig wizard (VERY EXPERIMENTAL). True when a camera was set up or chosen.</summary>
    Task<bool> ShowCameraWizardAsync(ViewModels.CameraWizardViewModel viewModel);
  }
}
