# MB Frame Pacing Data for C#

`MB.FramePacing.Data`, the SDK's C# data module, reads and writes the data of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) tools: the
capture data (`captures.mbcd`) and the analysis output (`summary.json`, `captures.csv`, `run-<id>-frames.csv`). .NET 10, no dependencies
besides the marker module (`MB.FramePacing.Marker`), which decodes the markers' payloads, and the core (`MB.FramePacing`). The tools write every file through it.

It is not published as a NuGet package: use the source at an `sdk-v*` tag, as a project reference to `MB.FramePacing.Data.csproj`
(it references `../marker`).

## Quick start

```csharp
using MB.FramePacing.Data;

// The analysis output of a capture folder (or the analysis folder itself)
string analysis = AnalysisFiles.Find(captureFolder) ?? throw new FileNotFoundException("no analysis");
var summary = AnalysisSummary.Read(Path.Combine(analysis, AnalysisFiles.SummaryFileName));
foreach (var run in summary.Runs)
{
  Console.WriteLine($"Run {run.RunId}: {run.Statistics.AverageFps:0.0} fps, {run.Pacing?.LateFrames} late frames");
  foreach (var frame in FramesCsv.Read(Path.Combine(analysis, run.FramesFile)))
  {
    // Points in time are TickCount64s, spans TimeSpans; null where the file has an empty cell
    if (frame.AnimationError is { } error)
      Console.WriteLine($"{frame.FrameIndex}: {error.TotalMilliseconds:0.000} ms");
  }
}

// The capture data: every captured frame's times and markers
using var reader = new CaptureDataReader(Path.Combine(captureFolder, CaptureDataHeader.FileName));
foreach (var record in reader.ReadAll())
{
  if (record.TryDecodeMain(out var payload, out _))
    Console.WriteLine($"capture {record.CaptureIndex}: frame {payload.FrameIndex}");
}
```

## API

| Type                                                            | What it is                                                                                                                                                     |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CaptureDataHeader`, `CaptureDataRecord`, `CaptureDataStatus`   | The header and records of `captures.mbcd` (a record's `SourceDrops`: frames the source reported dropping before it); `TryDecodeMain` decodes a record's marker |
| `CaptureDataReader`, `CaptureDataWriter`                        | Read and write `captures.mbcd`                                                                                                                                 |
| `MarkerLocation` (its bounds a core `Rectangle`)                | Where the markers are                                                                                                                                          |
| `AnalysisSummary` and the `Summary…` records, `ValueStatistics` | `summary.json`: `Read`, `Parse`, `Write`, `ToJson`                                                                                                             |
| `FramesCsv`, `FrameRow`, `OlderFrame`                           | A run's frames CSV                                                                                                                                             |
| `CapturesCsv`, `CaptureCsvRow`                                  | `captures.csv`                                                                                                                                                 |
| `AnalysisFiles`                                                 | The file names, and finding the analysis folder of a capture                                                                                                   |

The times are typed, with the C++ data module's names: points in time are `TickCount64`s, on the capture's clock (a frame's
`FirstSeenTime`, a record's `HostTime` and `DeviceTime`, null when the device gave none) or the frame pacer's (`IntendedDisplayTime`,
`CpuStartTime`); spans are `TimeSpan`s (`DisplayDelta`, `AnimationError`, ...); the marker's own 32-bit values (`MarkerTargetFrameTime`,
`CpuBusy`) are `TimeSpan32`s. The files hold each as its whole 100 ns ticks, in the integer type it has, so a value is read
exactly as it was written: nothing goes through a floating point number.

Readers throw `InvalidDataException` for a file that is not in the format: a newer format version than they know (the message says
to update), a required field or column missing, or a value that is not of its type or outside its range.

## Tests

```sh
dotnet test sdk/csharp/data/UnitTest
```

NUnit: the golden data in `test-data/data` (read as `digest.json` says, and written back as it was), and the formats' edge cases.

## License

BSD 3-Clause ([LICENSE](../../LICENSE)).
