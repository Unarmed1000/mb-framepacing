# The frame record: what an application logs to a trace (experimental)

> **Experimental.** The record, the trace module that defines it and the tools that read it are checked against traces their own
> tests make. **No recorded trace has been through them yet.** The strings and the module's API may change in any release.

A capture card recording says when each frame was on screen. A [Perfetto](https://perfetto.dev) trace of the same run says what
the application and the system did meanwhile. The tools merge the two ([the tools' guide](../../measure/doc/perfetto.md)), and
for that a frame of the trace has to be known as a frame of the capture. Nothing in a trace says so unless the application
writes it.

**The frame record is what the application writes: one slice per frame, with the values the frame's marker carries.** It is the
one thing the merge needs from an application, and a trace without it is not merged. An application that records no trace has
nothing to do here: the marker alone is enough for the measurement.

The application logs the record itself, with whatever it traces with, so that it lands in the application's own trace, in order
with everything else it logs and on the same clock. The SDK writes no trace: its **trace module** defines every string of the
record and works out, from a marker's payload, what to log.

## The record

| What            | The rule                                                                                                                                                                      |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Which frames    | One slice for every frame whose main marker (a frame, start or end marker) is drawn and carries a CPU start time greater than 0. None for a sync marker                       |
| Name            | `mbfp frame <run id> <frame index>`: the two ids of that frame's marker, as decimal numbers without leading zeros                                                             |
| Start           | The marker's CPU start time                                                                                                                                                   |
| Length          | The marker's CPU busy time; a slice without length for a CPU busy time of 0                                                                                                   |
| Precision       | Both as the payload carries them, to the nanosecond: the CPU busy time after the payload's cap                                                                                |
| Clock           | The system's `CLOCK_MONOTONIC` (Perfetto's built-in clock 3): the clock the times in the marker are read from                                                                 |
| Category        | `mbfp`                                                                                                                                                                        |
| Track           | A track named `mbfp frames` under the application's process. A writer that can only begin and end a section on the running thread writes it there, on that thread's own track |
| How many        | At most one for a run id and frame index, and never one inside another: frames whose CPU work overlaps go on further tracks of the same name                                  |
| Annotations     | The marker's other times, as whole numbers of nanoseconds (below)                                                                                                             |
| A CPU start ≤ 0 | No slice: 0 is "not known" in a marker, and a trace has no time before its clock's start                                                                                      |

The annotations, in the order of the marker's fields. Each is the payload's value, after its cap where it has one:

| Annotation          | The marker's field    | Written        |
| ------------------- | --------------------- | -------------- |
| `animationNs`       | Animation time        | Always         |
| `markerPreferredNs` | Preferred frame time  | Unless it is 0 |
| `markerTargetNs`    | Target frame time     | Unless it is 0 |
| `intendedDisplayNs` | Intended display time | Unless it is 0 |

A value of 0 in those three fields is "not known" ([the marker's fields](marker-fields.md)), so its annotation is left out. The
names are the columns [the analysis output](analysis-output-format.md) has for the same values.

The record says what a trace has and names no tracing API. Written with its timestamps, after the frame's CPU work, it is exact:
the slice has the payload's own numbers. Written live, as a section begun and ended around the work, it is close, and the tools
report how close.

## The trace module

`mb_framepacing::trace` (C++, `MB::FramePacing::Trace`) and `MB.FramePacing.Trace` (C#). It links the marker module, has no
other dependency, reads no clock, calls no tracing API and allocates nothing. Off by default in C++
(`-DMB_FRAMEPACING_BUILD_TRACE=ON`, Conan's `with_trace`), as every experimental module is.

| C++                             | C#                         | What                                                                                              |
| ------------------------------- | -------------------------- | ------------------------------------------------------------------------------------------------- |
| `FrameRecordText`               | `FrameRecordText`          | The strings: the category, the track's name, the start of a slice's name, the annotations' names  |
| `FrameRecordName`               | `FrameRecordName`          | A slice's name for a run id and frame index, held inline: nothing is allocated                    |
| `FrameRecord`                   | `FrameRecord`              | What to log for one marker: the name, the start, the length and the annotations with their values |
| `FrameRecordAnnotation`         | `FrameRecordAnnotation`    | One annotation: its name and its value                                                            |
| `FrameRecordUtil::TryCreate`    | `FrameRecord.TryCreate`    | The record of a marker's payload; false for a marker that gets none                               |
| (none: applications only write) | `FrameRecordName.TryParse` | The run id and frame index of a slice's name, for a reader                                        |

The strings:

| Constant            | Value                          |
| ------------------- | ------------------------------ |
| `Category`          | `mbfp`                         |
| `TrackName`         | `mbfp frames`                  |
| `NamePrefix`        | `mbfp frame ` (with the space) |
| `AnimationNs`       | `animationNs`                  |
| `MarkerPreferredNs` | `markerPreferredNs`            |
| `MarkerTargetNs`    | `markerTargetNs`               |
| `IntendedDisplayNs` | `intendedDisplayNs`            |
| `ClockId`           | 3                              |

In C++, after the frame's marker is made:

```cpp
#include <mb/framepacing/trace/FrameRecord.hpp>
#include <mb/framepacing/trace/FrameRecordUtil.hpp>

namespace FT = MB::FramePacing::Trace;

FT::FrameRecord record;
if (FT::FrameRecordUtil::TryCreate(payload, record))
{
  // With your own tracing: a slice named record.Name.CStr(), in the category FrameRecordText::Category, on the track named
  // FrameRecordText::TrackName, from record.StartTime for record.Duration on the clock FrameRecordText::ClockId, with every
  // entry of record.Annotations() as an annotation of that name and value
}
```

`payload` is the payload the frame's marker was drawn from, so the record and the marker can not disagree. Every text is a string
literal that ends with a zero, and the name's `CStr()` does too: they can be given to a function that takes a C string.

`sdk/test-data/trace/frame-records.csv` holds payloads with the record each gives; the tests of both languages read it.

## Say so in the start marker

A capture does not show whether a trace of the run exists. An application that logs the frame record says so in the run's start
marker, with bit 2 of the marker's flags byte ([the marker format](marker-format.md#flags)):

```cpp
// The start marker of a run whose frames are logged to the trace
const FM::Payload start(FM::MarkerKind::SequenceStart, runId, frameIndex, FM::MarkerFlags::FrameRecordLogged, animationTime);
```

The application sets it, as it logs the record: the trace module writes no trace and can not know, so nothing in the SDK sets the
flag and it is off unless set. Set it only for a run whose frames are logged, and combine it with the other flags of that frame
where they apply (`FM::MarkerFlags::FrameRecordLogged | FM::MarkerFlags::StaticAfter`). It says nothing on a frame, end or sync
marker. The tools note it with the run; a trace is merged with or without it.

## What the tools do with it

[A capture and a Perfetto trace](../../measure/doc/perfetto.md) has the whole of it. In short:

- **Placing.** Each frame's CPU work is where its slice is, so the tools need not be told, or assume, which clock the
  application's is.
- **The application.** The slice's track says which process is the application.
- **A check.** What the tools decoded from each marker is held against the slice of that frame: a difference is a finding, such
  as a marker decoded as another frame's.

What it can not give: the capture card's clock is tied to the application's by the frames only, so a delay the whole run shares
can not be told; and which of the system's frames belongs to which frame is still a match by time, against the slice.
