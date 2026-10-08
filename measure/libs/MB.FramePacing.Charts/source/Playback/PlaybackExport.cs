//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Writes playback reports, the one path the command line and the GUI share (they differ only in decide and progress). Each report (a run or
//* a section) has a folder of its own with its page, video and playback.json, naming nothing outside it; an export touches only its own.
//*   1. the capture must be one the page can show (PlaybackCapture.Problem); the recording: the one given, else capture.json's;
//*   2. the report's playback.json: saving it again uses its video again, without a question, while the recording is unchanged (file
//*      name, size, modification time), unless the answer given in advance asks for another;
//*   3. ffmpeg describes the recording (FfmpegVideoProbe): one browsers play is copied into the folder; for one they cannot play, the
//*      question (in advance, or through decide, once per export): make a playable copy, or no video;
//*   4. the copy, with progress, cancellable (nothing left behind); playback.json, the page, then the video the report played before goes.
//* A video the user names instead (--playback-video-url, a URL or a path relative to the folder) replaces 2 to 4: written into the page as
//* given, nothing copied or asked; only a file that is there and shorter than the run is a warning.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
      if (capture.Problem(options.VideoPath, namedVideo: options.VideoUrl != null) is { } problem)
        throw new InvalidOperationException(problem);
      var pages = new List<string>();
      var warnings = new List<string>();
      PlaybackVideo? first = null;
      string? firstFolder = null;
      foreach (var run in runs)
      {
        var section = PlaybackFiles.Section(run.Chart, options.FromSeconds, options.ToSeconds);
        string folder = PlaybackFiles.FolderOf(capture.PlaybackDirectory, run.FilePrefix, section);
        var earlier = PlaybackVideo.Read(folder);
        PlaybackVideo video;
        if (options.VideoUrl is { } url)
        {
          video = NamedVideo(url, folder);
          if (CheckNamedVideo(url, folder, run.Chart, probe) is { } warning && !warnings.Contains(warning))
            warnings.Add(warning);
        }
        else
          video =
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
      return new PlaybackResult(pages, first ?? throw new InvalidOperationException("No run to write a playback page of"), warnings);
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

    /// <summary>The video the user named, <paramref name="url"/>, for the report in <paramref name="folder"/>: its playback.json says so.</summary>
    private static PlaybackVideo NamedVideo(string url, string folder)
    {
      Directory.CreateDirectory(folder);
      string name = url.Split('?', '#')[0].TrimEnd('/', '\\');
      name = name[(name.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
      var video = new PlaybackVideo(PlaybackVideoKind.External, null, name, 0, default, true, "a video named for the report", null, url);
      video.Write(folder);
      return video;
    }

    /// <summary>
    /// A warning when the video the user named is a file that is there (a path, relative to the report's <paramref name="folder"/>, or a
    /// file URL) and ffmpeg finds it shorter than the run: perhaps another recording. A web URL, a file not made yet, or one ffmpeg cannot
    /// describe is not judged; this never fails.
    /// </summary>
    internal static string? CheckNamedVideo(string url, string folder, ChartRun run, Func<string, VideoCodecInfo> probe)
    {
      string path;
      if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
      {
        if (!uri.IsFile)
          return null;
        path = uri.LocalPath;
      }
      else
        path = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(url)));
      var frames = run.Run.Frames;
      if (!File.Exists(path) || frames.Count == 0)
        return null;
      try
      {
        // The page plays the run at its own timestamps: the video must reach the run's last capture
        double needed = (frames[^1].LastSeenTime.Nanoseconds + run.CapturePeriod.Nanoseconds) / (double)NanosecondTimeSpan.NanosecondsPerSecond;
        if (probe(path).Duration is { } duration && duration.TotalSeconds + 0.5 < needed)
          return string.Create(
            CultureInfo.InvariantCulture,
            $"The video {url} is {duration.TotalSeconds:0.0} s long, but the run plays to {needed:0.0} s on the recording's timestamps: is it the same recording?"
          );
      }
      catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException or Win32Exception)
      {
        // Not judged
      }
      return null;
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
    private static bool Agrees(PlaybackVideo earlier, PlaybackExportOptions options)
    {
      // A video the user named for an earlier save is not the report's own: the copy is made again
      if (earlier.Kind == PlaybackVideoKind.External)
        return false;
      if (earlier.SourcePlayable)
        return earlier.Kind == PlaybackVideoKind.Copied;
      return options.TranscodeChoice switch
      {
        PlaybackTranscodeChoice.Yes => earlier.Kind == PlaybackVideoKind.Transcoded,
        PlaybackTranscodeChoice.No => earlier.Kind == PlaybackVideoKind.None,
        _ => true,
      };
    }

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
