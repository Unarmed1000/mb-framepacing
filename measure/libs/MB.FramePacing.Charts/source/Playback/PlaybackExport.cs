//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes playback reports: the one path the command line and the GUI share, which differ only in how they ask (the decide callback) and
//* show progress. Each report (a run, or a section of one) has a folder of its own (PlaybackFiles.FolderOf) with its page, its video and its
//* playback.json, and names nothing outside it: no link to the recording, no local path. An export touches only the folders of the reports
//* it writes. In order:
//*   1. the capture must be one the page can show (PlaybackCapture.Problem);
//*   2. the recording: the one given, else the one capture.json names;
//*   3. the report's playback.json: saving the same report again uses its video again, without a question, while the recording is
//*      unchanged (file name, size and modification time), unless the answer given in advance asks for another;
//*   4. ffmpeg describes the recording (FfmpegVideoProbe): browsers play it or not;
//*   5. one browsers play is copied into the folder; for one they cannot play, the question: make a playable copy, or no video, answered
//*      in advance (an option, the configuration) or asked through decide, once per export (its other reports follow the answer);
//*   6. the copy, with progress, cancellable (a cancelled copy leaves nothing behind);
//*   7. playback.json and the page; the video the report played before goes only once the page plays the new one.
//* No playable copy is made without a yes: given in advance, or to the question.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MB.FramePacing.Analysis;
using MB.FramePacing.Capture;
using MB.FramePacing.Capture.Ffmpeg;

namespace MB.FramePacing.Charts.Playback
{
  public static class PlaybackExport
  {
    /// <summary>
    /// Write the reports of <paramref name="runs"/> of <paramref name="capture"/> (each all of the run, or the section the options give), a
    /// folder each. Throws <see cref="InvalidOperationException"/> when the page cannot show the capture, <see cref="FileNotFoundException"/>
    /// when the recording is gone and the report has no copy of it, <see cref="OperationCanceledException"/> when cancelled (decide may
    /// throw it too).
    /// </summary>
    public static Task<PlaybackResult> WriteAsync(
      PlaybackCapture capture,
      IReadOnlyList<AnalysisOutputRun> runs,
      PlaybackExportOptions options,
      Func<PlaybackQuestion, Task<bool>> decide,
      IProgress<PlaybackProgress>? progress = null,
      CancellationToken cancellationToken = default
    ) =>
      WriteAsync(
        capture,
        runs,
        options,
        decide,
        progress,
        cancellationToken,
        file => FfmpegVideoProbe.Probe(options.FfmpegPath, file),
        (file, target, codec, fraction, token) => PlayableCopy.Make(options.FfmpegPath, file, target, codec, fraction, token)
      );

    /// <summary><see cref="WriteAsync(PlaybackCapture, IReadOnlyList{AnalysisOutputRun}, PlaybackExportOptions, Func{PlaybackQuestion, Task{bool}}, IProgress{PlaybackProgress}?, CancellationToken)"/> with its ffmpeg steps given (tests).</summary>
    internal static async Task<PlaybackResult> WriteAsync(
      PlaybackCapture capture,
      IReadOnlyList<AnalysisOutputRun> runs,
      PlaybackExportOptions options,
      Func<PlaybackQuestion, Task<bool>> decide,
      IProgress<PlaybackProgress>? progress,
      CancellationToken cancellationToken,
      Func<string, VideoCodecInfo> probe,
      Action<string, string, VideoCodecInfo, IProgress<double>, CancellationToken> makePlayableCopy
    )
    {
      if (capture.Problem(options.VideoPath) is { } problem)
        throw new InvalidOperationException(problem);
      var pages = new List<string>();
      PlaybackVideo? first = null;
      string? firstFolder = null;
      foreach (var run in runs)
      {
        var section = PlaybackFiles.Section(run.Chart, options.FromSeconds, options.ToSeconds);
        string folder = PlaybackFiles.FolderOf(capture.PlaybackDirectory, run.FilePrefix, section);
        var earlier = PlaybackVideo.Read(folder);
        var video =
          first == null
            ? await PrepareVideoAsync(capture, folder, options, decide, progress, cancellationToken, probe, makePlayableCopy).ConfigureAwait(false)
            : await ShareVideoAsync(first, firstFolder!, folder, earlier, progress, cancellationToken).ConfigureAwait(false);
        first ??= video;
        firstFolder ??= folder;
        progress?.Report(new PlaybackProgress("Writing the playback page", null));
        pages.Add(PlaybackFiles.Write(section, folder, video, options.Report, options.ToolVersion));
        // The report's page plays the new video now: the one it played before goes
        if (earlier?.VideoFile is { } old && old != video.VideoFile)
          File.Delete(Path.Combine(folder, old));
      }
      return new PlaybackResult(pages, first ?? throw new InvalidOperationException("No run to write a playback page of"));
    }

    /// <summary>
    /// The video the report in <paramref name="folder"/> plays: the one it played before when the recording is unchanged, else a copy of the
    /// recording in the folder, or, for one browsers cannot play, after <paramref name="decide"/> (or the answer given in advance), a playable
    /// copy or none. Writes the folder's playback.json; the video it names before stays until the page plays the new one.
    /// </summary>
    internal static async Task<PlaybackVideo> PrepareVideoAsync(
      PlaybackCapture capture,
      string folder,
      PlaybackExportOptions options,
      Func<PlaybackQuestion, Task<bool>> decide,
      IProgress<PlaybackProgress>? progress,
      CancellationToken cancellationToken,
      Func<string, VideoCodecInfo> probe,
      Action<string, string, VideoCodecInfo, IProgress<double>, CancellationToken> makePlayableCopy
    )
    {
      if (capture.Problem(options.VideoPath) is { } problem)
        throw new InvalidOperationException(problem);
      Directory.CreateDirectory(folder);
      string sourcePath = Path.GetFullPath(options.VideoPath ?? capture.InputPath!);
      var earlier = PlaybackVideo.Read(folder);

      var source = new FileInfo(sourcePath);
      if (!source.Exists)
      {
        // The report's own copy still plays (the recording was moved or deleted after the copy was made)
        if (options.VideoPath == null && earlier is { VideoFile: not null } && earlier.ExistsIn(folder) && SameName(earlier.SourceName, source.Name))
          return earlier;
        throw new FileNotFoundException(
          $"The recording {sourcePath} is not there (moved or deleted?): name it with --video <file>, or in the GUI pick it when asked.",
          sourcePath
        );
      }
      if (earlier != null && earlier.IsFrom(source) && earlier.ExistsIn(folder) && Agrees(earlier, options))
        return earlier;

      progress?.Report(new PlaybackProgress("Looking at the recording", null));
      var codec = await Task.Run(() => probe(sourcePath), cancellationToken).ConfigureAwait(false);
      cancellationToken.ThrowIfCancellationRequested();
      PlaybackVideo video;
      if (codec.Playable)
      {
        // Browsers play it: the report keeps a copy, so its folder plays anywhere on its own
        string target = Target(folder, codec.Extension, earlier);
        await Task.Run(() => CopyFile(source.FullName, source.Length, target, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        video = Video(PlaybackVideoKind.Copied, Path.GetFileName(target), source, codec);
      }
      else
      {
        string target = Target(folder, "mp4", earlier);
        bool transcode =
          options.TranscodeChoice == PlaybackTranscodeChoice.Yes
          || (
            options.TranscodeChoice == PlaybackTranscodeChoice.Ask
            && await decide(new PlaybackQuestion(sourcePath, source.Length, codec, target)).ConfigureAwait(false)
          );
        if (transcode)
        {
          string step = codec.RemuxIsEnough ? "Copying the video into an MP4 file" : "Making a playable copy";
          var fraction = new Progress<double>(value => progress?.Report(new PlaybackProgress(step, value)));
          progress?.Report(new PlaybackProgress(step, codec.Duration.HasValue ? 0 : null));
          await Task.Run(() => makePlayableCopy(sourcePath, target, codec, fraction, cancellationToken), cancellationToken).ConfigureAwait(false);
          video = Video(PlaybackVideoKind.Transcoded, Path.GetFileName(target), source, codec);
        }
        else
          video = Video(PlaybackVideoKind.None, null, source, codec);
      }
      video.Write(folder);
      return video;
    }

    /// <summary>
    /// Another report of the same export plays what the first one does (<paramref name="first"/>, in <paramref name="firstFolder"/>): a copy
    /// of the first report's video in its own folder (unless it has that video already), or none.
    /// </summary>
    private static async Task<PlaybackVideo> ShareVideoAsync(
      PlaybackVideo first,
      string firstFolder,
      string folder,
      PlaybackVideo? earlier,
      IProgress<PlaybackProgress>? progress,
      CancellationToken cancellationToken
    )
    {
      Directory.CreateDirectory(folder);
      if (first.VideoFile == null || earlier == first && earlier.ExistsIn(folder))
      {
        first.Write(folder);
        return first;
      }
      string from = first.PathIn(firstFolder)!;
      string target = Target(folder, Path.GetExtension(first.VideoFile).TrimStart('.'), earlier);
      await Task.Run(() => CopyFile(from, new FileInfo(from).Length, target, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
      var video = first with { VideoFile = Path.GetFileName(target) };
      video.Write(folder);
      return video;
    }

    /// <summary>The report file prefix of <paramref name="run"/> of <paramref name="report"/>, as its frames CSV is named.</summary>
    public static string PrefixOf(AnalysisReport report, RunAnalysis run)
    {
      int ordinal = 0;
      foreach (var other in report.Timeline.Runs)
      {
        if (ReferenceEquals(other, run))
          break;
        if (other.RunId == run.RunId)
          ++ordinal;
      }
      return CaptureAnalyzer.RunFilePrefix(run, ordinal);
    }

    /// <summary>
    /// Where a new video of the report in <paramref name="folder"/> goes: video.&lt;extension&gt;, or video-2.&lt;extension&gt; when the page
    /// still plays a video of that name (it is replaced only once the new page is written).
    /// </summary>
    private static string Target(string folder, string extension, PlaybackVideo? earlier)
    {
      string name = $"{PlaybackFiles.VideoName}.{extension}";
      if (earlier?.VideoFile == name)
        name = $"{PlaybackFiles.VideoName}-2.{extension}";
      return Path.Combine(folder, name);
    }

    /// <summary>
    /// The video made before still answers the question as asked now: a copy of a playable recording always; for one browsers cannot play,
    /// always when asking, else when it is what the answer makes.
    /// </summary>
    private static bool Agrees(PlaybackVideo earlier, PlaybackExportOptions options) =>
      earlier.SourcePlayable
        ? earlier.Kind == PlaybackVideoKind.Copied
        : options.TranscodeChoice switch
        {
          PlaybackTranscodeChoice.Yes => earlier.Kind == PlaybackVideoKind.Transcoded,
          PlaybackTranscodeChoice.No => earlier.Kind == PlaybackVideoKind.None,
          _ => true,
        };

    private static PlaybackVideo Video(PlaybackVideoKind kind, string? file, FileInfo source, VideoCodecInfo codec) =>
      new PlaybackVideo(kind, file, source.Name, source.Length, source.LastWriteTimeUtc, codec.Playable, codec.Description, codec.Problem);

    /// <summary>Copy a file through a temporary one, reporting the share copied; a cancelled copy leaves nothing behind.</summary>
    private static void CopyFile(
      string source,
      long length,
      string target,
      IProgress<PlaybackProgress>? progress,
      CancellationToken cancellationToken
    )
    {
      const string Step = "Copying the recording";
      string partial = target + ".partial";
      try
      {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
          var buffer = new byte[4 << 20];
          long copied = 0;
          int read;
          progress?.Report(new PlaybackProgress(Step, 0));
          while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
          {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            copied += read;
            progress?.Report(new PlaybackProgress(Step, length > 0 ? (double)copied / length : 1));
          }
        }
        File.Move(partial, target, overwrite: true);
      }
      finally
      {
        if (File.Exists(partial))
          File.Delete(partial);
      }
    }

    private static bool SameName(string a, string b) =>
      string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
  }
}
