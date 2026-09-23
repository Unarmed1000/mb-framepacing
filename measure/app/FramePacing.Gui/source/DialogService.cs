//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Folder/file pickers and "show in file manager", behind an interface so the view models stay free of UI types.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace MB.FramePacing.Gui
{
  public sealed class DialogService : IDialogService
  {
    private readonly TopLevel m_topLevel;

    public DialogService(TopLevel topLevel)
    {
      m_topLevel = topLevel;
    }

    public async Task<string?> PickFolderAsync(string title, string? startDirectory)
    {
      var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
      if (startDirectory != null && Directory.Exists(startDirectory))
        options.SuggestedStartLocation = await m_topLevel.StorageProvider.TryGetFolderFromPathAsync(new System.Uri(Path.GetFullPath(startDirectory)));
      var folders = await m_topLevel.StorageProvider.OpenFolderPickerAsync(options);
      return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title)
    {
      var files = await m_topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
      return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task CopyToClipboardAsync(string text)
    {
      if (m_topLevel.Clipboard is { } clipboard)
        await clipboard.SetTextAsync(text);
    }

    public async Task<bool> ShowSetupAsync(ViewModels.SetupViewModel viewModel)
    {
      var window = new Views.SetupWindow { DataContext = viewModel };
      return m_topLevel is Window owner ? await window.ShowDialog<bool>(owner) : false;
    }

    public void ShowInFileManager(string directory)
    {
      if (Directory.Exists(directory))
        Open(directory);
    }

    public void OpenUrl(string url) => Open(url);

    public void OpenFile(string path)
    {
      if (File.Exists(path))
        Open(path);
    }

    /// <summary>Hand a folder, file or URL to the desktop (Explorer / Finder / xdg-open).</summary>
    private static void Open(string target)
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        // The shell picks the default browser, editor or Explorer; a file type without an associated application falls back to Notepad
        try
        {
          Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) when (File.Exists(target))
        {
          Process.Start(new ProcessStartInfo("notepad.exe", $"\"{target}\"") { UseShellExecute = false })?.Dispose();
        }
        return;
      }
      var startInfo = new ProcessStartInfo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open") { UseShellExecute = false };
      startInfo.ArgumentList.Add(target);
      Process.Start(startInfo)?.Dispose();
    }
  }
}
