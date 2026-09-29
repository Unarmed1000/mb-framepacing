# MB Frame Pacing Data for Python

`mb_framepacing_data` reads the data of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) tools: the capture data
(`captures.mbcd`, [format](../../doc/capture-data-format.md)) and the analysis output (`summary.json`, `captures.csv`,
`run-<id>-frames.csv`, [format](../../doc/analysis-output-format.md)). Standard library only, Python 3.12 or later. It reads what the
C# library reads: the tests check it against the same golden data ([`test-data/data`](../../test-data/data)).

## Quick start

```python
from mb_framepacing_data import CaptureDataReader, find_analysis, read_frames, read_summary

analysis = find_analysis(capture_folder)  # the capture folder's analysis folder (or the folder itself)
summary = read_summary(analysis / "summary.json")
for run in summary.runs:
    print(run.run_id, run.statistics.average_fps, run.pacing.late_frames if run.pacing else None)
    for frame in read_frames(analysis / run.frames_file):
        # Times are 100 ns ticks; None where the file has an empty cell
        if frame.animation_error_ticks is not None:
            print(frame.frame_index, frame.animation_error_ticks / 10_000, "ms")

with CaptureDataReader(capture_folder / "captures.mbcd") as reader:
    for record in reader.records():
        decoded = record.try_decode_main()  # needs mb_framemarker (sdk/marker/python)
        if decoded is not None:
            payload, start = decoded
            print(record.capture_index, payload.frame_index)
```

## API

| Python                                                                        | What it is                                                                |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `CaptureDataReader`, `CaptureDataHeader`, `CaptureDataRecord`                 | `captures.mbcd`: the header, the records (`records()`, `read_all()`, ...) |
| `CaptureDataStatus`, `DataRect`, `MarkerLocation`, `UNKNOWN_TICKS`            | A record's status, where the markers are, a missing device time           |
| `read_summary`, `parse_summary`, `AnalysisSummary` and the `Summary…` classes | `summary.json`                                                            |
| `read_frames`, `FrameRow`                                                     | A run's frames CSV                                                        |
| `read_captures`, `CaptureCsvRow`                                              | `captures.csv`                                                            |
| `find_analysis`, `frames_file_name`, `parse_ticks`, `ms_to_ticks`             | The analysis folder, the file names, the CSV time format                  |
| `DataFormatError`                                                             | Raised for another kind of file or a newer format version ("update ...")  |

Decoding a marker's payload (`CaptureDataRecord.try_decode_main`) uses the `mb_framemarker` package
([`sdk/marker/python`](../../marker/python/README.md)); everything else needs only the standard library.

## Tests

```powershell
python -m unittest discover -s sdk/data/python -t sdk/data/python
```

The golden data is found by walking up to `test-data/data`, or from the `MB_FRAMEPACING_DATA_TEST_DATA` environment variable;
without it those tests are skipped. The tests use `sdk/marker/python` when `mb_framemarker` is not installed.

## License

BSD 3-Clause ([LICENSE](mb_framepacing_data/LICENSE)).
