# MB Frame Pacing Core for C#

`MB.FramePacing`, the core of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK for C#: the types every module
shares. .NET Standard 2.1, C# 9, no dependencies, so the same sources also compile inside Unity (2021.3+). The marker module
([`MB.FramePacing.Marker`](../marker/README.md)) and the data module ([`MB.FramePacing.Data`](../data/README.md)) reference it.

It is not published as a NuGet package: use the source at an `sdk-v*` tag, as a project reference to `MB.FramePacing.csproj` or as a
copy of `source/`. **Unity:** the [Unity package](../../unity/README.md) contains it.

| Type        | What it is                                                                                                                                |
| ----------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `Point`     | A pixel position: origin at the top-left corner, +x to the right, +y down                                                                 |
| `Rectangle` | An integer pixel rectangle, `[Left, Right) × [Top, Bottom)`. Always valid: a negative width or height is 0 (`FromLeftTopRightBottom` too) |

The C++ core (`MB::FramePacing` in [`sdk/cpp/core`](../../cpp/README.md#the-core)) and the Python package's root (`mb_framepacing`) have
the same `Point` and `Rectangle`.
