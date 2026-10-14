# MB Frame Pacing Trace for C#

`MB.FramePacing.Trace`, the trace module of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK for C#: the
**frame record**, the slice an application logs to its own trace for every frame, so that the tools can merge a trace of a run
with a capture of it. [The frame record](../../doc/trace-frame-record.md) is the specification. .NET Standard 2.1, C# 9; it
references the marker module ([`MB.FramePacing.Marker`](../marker/README.md)) and the core, and nothing else.

The module writes no trace and calls no tracing API: it defines every string of the record and works out, from a marker's
payload, what to log. The application logs it with whatever it traces with. An application that records no trace does not need
it. It is not part of the Unity package.

It is not published as a NuGet package: use the source at an `sdk-v*` tag, as a project reference to `MB.FramePacing.Trace.csproj`
or as a copy of `source/`.

| Type                    | What it is                                                                                                                                       |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `FrameRecordText`       | The strings: the category, the track's name, the start of a slice's name, the annotations' names; and the clock's id                             |
| `FrameRecord`           | What to log for one marker (`TryCreate` from its payload): the start, the length, the annotations (`AnnotationCount`, `GetAnnotation`), the name |
| `FrameRecordName`       | A slice's name for a run id and frame index: `TryFormat` into your buffer (nothing is allocated), `Format` as a string, `TryParse` for a reader  |
| `FrameRecordAnnotation` | One annotation: its name and its value, a whole number of nanoseconds                                                                            |

```csharp
using MB.FramePacing.Trace;

// Every frame, after the frame's marker is made: payload is the payload the marker was drawn from
if (FrameRecord.TryCreate(payload, out var record))
{
  Span<char> name = stackalloc char[FrameRecordName.MaxLength];
  record.TryFormatName(name, out int length);
  // With your own tracing: a slice of that name, in the category FrameRecordText.Category, on the track named
  // FrameRecordText.TrackName, from record.StartTime for record.Duration on the clock FrameRecordText.ClockId, with
  // record.GetAnnotation(0) to record.GetAnnotation(record.AnnotationCount - 1) as its annotations
}
```

`TryCreate` is false for a marker that gets no record: a sync marker, or a CPU start time that is not greater than 0. Nothing on
that path allocates; `FrameRecord.Name` and `FrameRecordName.Format` make a string and are for a reader or a log line.

The tests read `sdk/test-data/trace/frame-records.csv`, the golden data the C++ module's tests read too.
