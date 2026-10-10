//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL (the trace module, sdk/doc/trace-frame-record.md). One annotation of a frame's slice: its name and its value.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. One annotation of a frame's slice.</summary>
  public readonly struct FrameRecordAnnotation : IEquatable<FrameRecordAnnotation>
  {
    public FrameRecordAnnotation(string name, long value)
    {
      Name = name;
      Value = value;
    }

    /// <summary>The annotation's name: one of <see cref="FrameRecordText"/>'s.</summary>
    public readonly string Name;

    /// <summary>Its value: a whole number of nanoseconds, as the marker's payload carries it.</summary>
    public readonly long Value;

    public bool Equals(FrameRecordAnnotation other) => Name == other.Name && Value == other.Value;

    public override bool Equals(object obj) => obj is FrameRecordAnnotation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, Value);

    public static bool operator ==(FrameRecordAnnotation left, FrameRecordAnnotation right) => left.Equals(right);

    public static bool operator !=(FrameRecordAnnotation left, FrameRecordAnnotation right) => !left.Equals(right);

    public override string ToString() => $"{Name}={Value}";
  }
}
