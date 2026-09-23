//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Extra data carried by a SequenceStart marker: the wall clock start time and a test name.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FrameMarker
{
  public readonly struct StartMetadata
  {
    public StartMetadata(long utcTicks, string name)
    {
      UtcTicks = utcTicks;
      Name = name;
    }

    /// <summary>Wall clock start time as DateTime UTC ticks (100 ns since 0001-01-01), 0 = unknown.</summary>
    public long UtcTicks { get; }

    /// <summary>Test name, at most <see cref="Marker.MaxStartNameBytes"/> bytes as UTF-8. Null is the same as empty.</summary>
    public string Name { get; }

    /// <summary>Metadata for a run starting at <paramref name="startTime"/> (converted to UTC).</summary>
    public static StartMetadata Create(DateTime startTime, string name) => new StartMetadata(Marker.ToDateTimeTicks(startTime), name);
  }
}
