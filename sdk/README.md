# mb-framepacing SDK

The part of [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) that goes into your own code. The **marker libraries** draw
a small QR code into every frame your application renders. The **data libraries** read what the mb-framepacing tools capture and
analyse. Everything here is under the BSD 3-Clause License. The measuring tools themselves (capture, analysis, GUI) are in
[`measure/`](../measure) under another license.

## Where to start

| You want to                                 | Start with                                                                                                                                                    |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Put the marker into a C++, C# or Python app | [Integrating the marker](doc/integrating.md), then the library's README below                                                                                 |
| Put the marker into a Unity game            | [Unity](doc/unity.md)                                                                                                                                         |
| Know what to write in each marker field     | [Filling the marker fields](doc/marker-fields.md)                                                                                                             |
| Read the tools' results in your own code    | [The data libraries](data/README.md), [the analysis output format](doc/analysis-output-format.md)                                                             |
| Implement the marker or a reader yourself   | [The marker format](doc/marker-format.md), [the capture data format](doc/capture-data-format.md), [the analysis output format](doc/analysis-output-format.md) |

## What is here

| Path                          | Contents                                                                                                                                                                                                                                                       |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`marker/`](marker/README.md) | The marker libraries, which draw the same pixels: [C++20](marker/cpp/README.md), [C#](marker/csharp/README.md), [Unity](marker/unity/README.md), [Python](marker/python/README.md), and [reference shaders](marker/shaders/README.md) that draw it as one quad |
| [`data/`](data/README.md)     | The data libraries, which read `captures.mbcd` and the analysis output: [C#](data/csharp/README.md) (also writes), [Python](data/python/README.md), [C++20](data/cpp/README.md)                                                                                |
| [`doc/`](doc)                 | The formats, the integration guides and the [vocabulary](doc/vocabulary.md)                                                                                                                                                                                    |
| [`conan/`](conan)             | Conan 2 recipes of both C++ libraries                                                                                                                                                                                                                          |
| [`test-data/`](test-data)     | The golden data every language's tests check against: marker images (`markers/`) and an analysed test clip (`data/`)                                                                                                                                           |

## Versions

The marker libraries and the data libraries are versioned and released separately:

- the marker libraries: [`marker/VERSION`](marker/VERSION), with `marker-v*` tags;
- the data libraries: [`data/VERSION`](data/VERSION), with `data-v*` tags.

The versions follow semantic versioning. While a version is 0.x, a new minor version may change the API.

## License

BSD 3-Clause, the same for everything under `sdk/`. [`marker/LICENSE`](marker/LICENSE) and [`data/LICENSE`](data/LICENSE) hold the text,
and the release archives and packages ship it. Third-party code keeps its own notices next to it (the QR encoder the C++ and Python
marker libraries vendor, in their `third_party/` folders).
