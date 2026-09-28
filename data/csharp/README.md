# MB Frame Pacing Data for C#

`MB.FramePacing.Data` reads and writes the data of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) tools: the
capture data (`captures.mbcd`) and the analysis output (`summary.json`, `captures.csv`, `run-<id>-frames.csv`). .NET 10, no dependencies
besides the marker library (`MB.FrameMarker`), which decodes the markers' payloads. The tools write every file through it.

It is not published as a NuGet package: use the source at a `data-v*` tag, as a project reference to `MB.FramePacing.Data.csproj`
(it references `../../marker/csharp`).

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
    // Times are 100 ns ticks; null where the file has an empty cell
    if (frame.AnimationErrorTicks is { } error)
      Console.WriteLine($"{frame.FrameIndex}: {error / (double)Milliseconds.TicksPerMillisecond:0.000} ms");
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

| Type                                                                                | What it is                                                                           |
| ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `CaptureDataHeader`, `CaptureDataRecord`, `CaptureDataStatus`, `CaptureRecordFlags` | The header and records of `captures.mbcd`; `TryDecodeMain` decodes a record's marker |
| `CaptureDataReader`, `CaptureDataWriter`                                            | Read and write `captures.mbcd`                                                       |
| `DataRect`, `MarkerLocation`                                                        | Where the markers are                                                                |
| `AnalysisSummary` and the `Summary…` records, `ValueStatistics`                     | `summary.json`: `Read`, `Parse`, `Write`, `ToJson`                                   |
| `FramesCsv`, `FrameRow`                                                             | A run's frames CSV                                                                   |
| `CapturesCsv`, `CaptureCsvRow`                                                      | `captures.csv`                                                                       |
| `AnalysisFiles`                                                                     | The file names, and finding the analysis folder of a capture                         |
| `Milliseconds`                                                                      | The CSV time format: milliseconds with at most four decimals, which are whole ticks  |

Readers refuse a newer format version than they know (an `InvalidDataException` that says to update).

## Tests

```sh
dotnet test data/csharp/UnitTest
```

NUnit: the golden data in `test-data/data` (read as `digest.json` says, and written back as it was), and the formats' edge cases.

## License

BSD 3-Clause ([LICENSE](../LICENSE)).
