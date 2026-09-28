# MB Frame Pacing Data

The data libraries of [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing): read what the tools write, in your own code.

- **The capture data** (`captures.mbcd`): every captured frame's timestamps and the markers' bytes as read
  ([format](../doc/capture-data-format.md)).
- **The analysis output** (`analysis/summary.json`, `captures.csv`, `run-<id>-frames.csv`): every run's counts, statistics, pacing and
  histograms, and every presented frame's display time, display time step, animation error and pacing
  ([format](../doc/analysis-output-format.md)).

| Language | Library                                             | Reads | Writes |
| -------- | --------------------------------------------------- | ----- | ------ |
| C#       | [`MB.FramePacing.Data`](csharp/README.md) (.NET 10) | yes   | yes    |
| Python   | [`mb_framepacing_data`](python/README.md) (3.11+)   | yes   | no     |
| C++      | [`mb_framepacingdata`](cpp/README.md) (C++20)       | yes   | no     |

The C# library is the reference: the tools write every file through it. A marker payload inside the capture data is decoded with the
marker library of the same language ([`marker/`](../marker/README.md)).

## Format versions

`captures.mbcd` has a format version in its header, and `summary.json` a `formatVersion` that covers the CSV files it names. A reader
refuses a newer version than it knows. Within a version, fields and columns may be added; readers look CSV columns up by name and ignore
the ones they do not know.

## Golden data

[`test-data/data`](../test-data/data) holds a test clip imported and analysed by the tools, and `digest.json`: the counts and sums every
library's reader must read from it. `python tools/update_test_data.py` regenerates it after a format change.

## Version

The data libraries share one version, [`VERSION`](VERSION), released with `data-v<version>` tags (see [Releasing](../doc/releasing.md)).
While it is 0.x, a new minor version may change the API.

## License

BSD 3-Clause ([LICENSE](LICENSE)), like the marker libraries and the formats they read.
