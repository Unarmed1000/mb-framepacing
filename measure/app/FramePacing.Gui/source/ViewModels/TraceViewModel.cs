//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The Perfetto trace of a run (measure/doc/perfetto.md), on the Capture page (stored with the capture) and on the Analyze page (the
//* capture's own, or another for the next Analyze): the trace's file and which of its processes is the application. Once a trace is
//* picked its processes are listed, those the system's frame timeline has frames of first. The join's other options are the command
//* line's.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MB.FramePacing.Capture;
using MB.FramePacing.TraceJoin;
using NLog;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed partial class TraceViewModel : ObservableObject
  {
    /// <summary>What a trace is: shown where one is picked.</summary>
    public const string PrivacyText = "A trace names the device and its programs: what is written with it is as private as the recording.";

    private static readonly Logger g_logger = LogManager.GetCurrentClassLogger();

    private readonly IDialogService m_dialogs;
    private CancellationTokenSource? m_listing;

    public TraceViewModel(IDialogService dialogs)
    {
      m_dialogs = dialogs;
    }

    /// <summary>The trace's file; empty for none.</summary>
    [ObservableProperty]
    public partial string TracePath { get; set; } = string.Empty;

    /// <summary>The application's process name in the trace (* and ? as in a file name); empty for none.</summary>
    [ObservableProperty]
    public partial string Process { get; set; } = string.Empty;

    /// <summary>The trace's processes, for picking the application: those with frames in the system's frame timeline first.</summary>
    public ObservableCollection<string> Processes { get; } = new ObservableCollection<string>();

    /// <summary>What listing the trace's processes found, or why it could not.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>The listing of the trace's processes, for a caller that waits for it (the tests).</summary>
    public Task Listed { get; private set; } = Task.CompletedTask;

    partial void OnTracePathChanged(string value) => Listed = ListProcessesAsync(value.Trim());

    [RelayCommand]
    private async Task BrowseAsync()
    {
      var path = await m_dialogs.PickFileAsync("Select the Perfetto trace of the run (.pftrace, .perfetto-trace)");
      if (path != null)
        TracePath = path;
    }

    /// <summary>
    /// The trace as the fields have it; null without a file. The options the fields do not show (the layer, the thread, the clocks) are
    /// those of <paramref name="remembered"/> while it names the same file.
    /// </summary>
    public TraceJoinOptions? Options(TraceJoinOptions? remembered = null)
    {
      string path = TracePath.Trim();
      // A capture that names a trace of the application's own keeps it: the page has no field for it
      if (path.Length == 0)
        return remembered is { Path.Length: 0, AppTracePath: not null } ? remembered : null;
      string? process = string.IsNullOrWhiteSpace(Process) ? null : Process.Trim();
      return remembered != null && string.Equals(remembered.Path, path, StringComparison.Ordinal)
        ? remembered with
        {
          Process = process,
        }
        : new TraceJoinOptions
        {
          Path = path,
          Process = process,
          AppTracePath = remembered?.AppTracePath,
        };
    }

    /// <summary>Show <paramref name="options"/> (a capture's own trace); null clears the fields.</summary>
    public void Show(TraceJoinOptions? options)
    {
      Process = options?.Process ?? string.Empty;
      TracePath = options?.Path ?? string.Empty;
    }

    /// <summary>The user's trace processor; null, with the reason as the status, when there is none.</summary>
    public static string? FindProcessor(out string problem)
    {
      try
      {
        problem = string.Empty;
        return TraceProcessorLocator.Find(null, MainWindowViewModel.SafeLoadConfig());
      }
      catch (FileNotFoundException ex)
      {
        problem = ex.Message;
        return null;
      }
    }

    /// <summary>List the processes of the trace at <paramref name="path"/>: read in the background, and dropped when another trace is picked meanwhile.</summary>
    private async Task ListProcessesAsync(string path)
    {
      m_listing?.Cancel();
      var listing = m_listing = new CancellationTokenSource();
      Processes.Clear();
      StatusText = string.Empty;
      if (path.Length == 0 || !File.Exists(path))
      {
        StatusText = path.Length == 0 ? string.Empty : "The file does not exist.";
        return;
      }
      if (FindProcessor(out string problem) is not { } processor)
      {
        StatusText = problem;
        return;
      }
      StatusText = "Reading the trace...";
      try
      {
        var info = await Task.Run(
          () =>
          {
            using var trace = TraceProcessor.Start(processor, path, cancellationToken: listing.Token);
            return TraceReader.Inspect(trace);
          },
          listing.Token
        );
        if (listing.IsCancellationRequested)
          return;
        foreach (var process in info.Processes.OrderByDescending(p => p.TimelineFrames).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
          if (process.Name.Length > 0 && !Processes.Contains(process.Name))
            Processes.Add(process.Name);
        }
        int withFrames = info.Processes.Count(p => p.TimelineFrames > 0);
        StatusText =
          $"{Processes.Count} processes, {withFrames} with frames in the system's frame timeline (listed first)."
          + (info.VBlankSources.Count == 0 ? " The trace has no vertical blanks." : string.Empty);
      }
      catch (OperationCanceledException)
      {
        // Another trace was picked
      }
      catch (TraceException ex)
      {
        g_logger.Warn("Could not list the processes of a trace: {0}", ex.Message);
        if (!listing.IsCancellationRequested)
          StatusText = ex.Message;
      }
    }
  }
}
