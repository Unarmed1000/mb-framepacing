//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run's markers against the frame record of a trace (FrameRecordSlice), frame by frame by the marker's frame index. Two things come
//* of it:
//*
//* - Where the frames are on the record's clock: the median of each slice's start minus its marker's CPU start time (ClockOffsetNs). An
//*   application that reads the marker's times from the record's clock, as the record asks, gives 0. The join places the run by it.
//* - What differs: the CPU start time against the slice's start (after that offset), the CPU busy time against its length, and the
//*   marker's other values against the slice's annotations where it has them. An application that logs the payload's own numbers gives
//*   no difference: anything else is a finding (a marker decoded as another's, a value lost on the way). One that can only log a section
//*   live is close, and the numbers say how close. Numbers only: nothing here says pass or fail.
//*
//* It says nothing of the capture's clock or of what the system did with a frame: the record is the application's own values.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. A run's markers as decoded, against the frame record of a trace.</summary>
  /// <param name="Slices">The record's slices of this run.</param>
  /// <param name="NamedFrames">Frames with a CPU start time whose frame index has a slice.</param>
  /// <param name="WorkedFrames">Frames with a CPU start time.</param>
  /// <param name="ClockOffsetNs">The slice's start minus the marker's CPU start time: the median over the named frames.</param>
  /// <param name="StartDifferentFrames">Of the named frames, those whose slice does not start that far from the marker's CPU start time.</param>
  /// <param name="StartDifferenceMinNs">The slice's start minus the marker's CPU start time, less the offset: the smallest.</param>
  /// <param name="BusyDifferentFrames">Those whose slice's length is not the marker's CPU busy time to the nanosecond.</param>
  /// <param name="BusyDifferenceMedianNs">The slice's length minus the marker's CPU busy time.</param>
  /// <param name="Annotations">For each annotation a slice of the run has: how many frames have it, and for how many it differs.</param>
  public sealed record FrameRecordCheck(
    int Slices,
    int NamedFrames,
    int WorkedFrames,
    long ClockOffsetNs,
    int StartDifferentFrames,
    long StartDifferenceMinNs,
    long StartDifferenceMaxNs,
    int BusyDifferentFrames,
    long BusyDifferenceMedianNs,
    long BusyDifferenceMinNs,
    long BusyDifferenceMaxNs,
    IReadOnlyList<(string Name, int Frames, int Different)> Annotations
  )
  {
    /// <summary>What every text of the check begins with.</summary>
    public const string Heading = "The markers as decoded against the application's frame record";

    private const string NameForm = FrameRecordText.NamePrefix + "<run id> <frame index>";

    /// <summary>
    /// The check of <paramref name="frames"/> of the run <paramref name="runId"/> against <paramref name="records"/> (every run's; the
    /// first slice of a frame index counts).
    /// </summary>
    public static FrameRecordCheck Of(uint runId, IReadOnlyList<FrameRow> frames, IReadOnlyList<FrameRecordSlice> records)
    {
      var byIndex = new Dictionary<ulong, FrameRecordSlice>();
      int runSlices = 0;
      foreach (var record in records)
      {
        if (record.RunId != runId)
          continue;
        ++runSlices;
        byIndex.TryAdd(record.FrameIndex, record);
      }
      var starts = new List<long>();
      var busyDifferences = new List<long>();
      var annotations = FrameRecordSlice.AnnotationNames.Select(name => (Name: name, Frames: 0, Different: 0)).ToArray();
      int worked = 0;
      foreach (var frame in frames)
      {
        if (frame.CpuStartNs() is not { } start)
          continue;
        ++worked;
        if (!byIndex.TryGetValue(frame.FrameIndex, out var record))
          continue;
        starts.Add(record.StartNs - start);
        // A CPU busy time of 0 is "not known" in a marker: its slice has no length
        busyDifferences.Add(record.EndNs - record.StartNs - (frame.CpuBusyNs() ?? 0));
        Compare(ref annotations[0], record.AnimationNs, frame.AnimationTime.Nanoseconds);
        Compare(ref annotations[1], record.MarkerPreferredNs, frame.MarkerPreferredFrameTime?.Nanoseconds ?? 0);
        Compare(ref annotations[2], record.MarkerTargetNs, frame.MarkerTargetFrameTime?.Nanoseconds ?? 0);
        Compare(ref annotations[3], record.IntendedDisplayNs, frame.IntendedDisplayNs() ?? 0);
      }
      starts.Sort();
      busyDifferences.Sort();
      long offset = Middle(starts);
      return new FrameRecordCheck(
        runSlices,
        starts.Count,
        worked,
        offset,
        starts.Count(start => start != offset),
        starts.Count > 0 ? starts[0] - offset : 0,
        starts.Count > 0 ? starts[^1] - offset : 0,
        busyDifferences.Count(difference => difference != 0),
        Middle(busyDifferences),
        busyDifferences.Count > 0 ? busyDifferences[0] : 0,
        busyDifferences.Count > 0 ? busyDifferences[^1] : 0,
        annotations.Where(annotation => annotation.Frames > 0).ToList()
      );
    }

    /// <summary>The numbers in words: one line, or what a trace without the record means.</summary>
    public string Line()
    {
      if (Slices == 0)
        return $"  {Heading}: the trace has no slice named '{NameForm}' for this run, so nothing is held against the markers.";
      var text =
        $"  {Heading} ({NameForm}), {Count(Slices)} slices of this run: {Count(NamedFrames)} of {Count(WorkedFrames)} frames with a CPU "
        + $"start time have theirs. Slice start minus the marker's CPU start time: median {Ns(ClockOffsetNs)} ns, which the frames are "
        + $"placed by; {Count(StartDifferentFrames)} frames differ from it (from {Ns(StartDifferenceMinNs)} to {Ns(StartDifferenceMaxNs)} "
        + $"ns). Slice length minus the marker's CPU busy time: {Count(BusyDifferentFrames)} frames differ (median "
        + $"{Ns(BusyDifferenceMedianNs)} ns, from {Ns(BusyDifferenceMinNs)} to {Ns(BusyDifferenceMaxNs)} ns).";
      foreach (var (name, frames, different) in Annotations)
        text += $" {name}: {Count(different)} of {Count(frames)} frames differ.";
      return text;
    }

    private static void Compare(ref (string Name, int Frames, int Different) annotation, long? logged, long decoded)
    {
      if (logged is not { } value)
        return;
      ++annotation.Frames;
      annotation.Different += value != decoded ? 1 : 0;
    }

    private static long Middle(List<long> sorted) => sorted.Count > 0 ? sorted[(sorted.Count - 1) / 2] : 0;

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Ns(long nanoseconds) => nanoseconds.ToString("+0;-0;0", CultureInfo.InvariantCulture);
  }
}
