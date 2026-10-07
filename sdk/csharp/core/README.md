# MB Frame Pacing Core for C#

`MB.FramePacing`, the core of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK for C#: the types every module
shares. .NET Standard 2.1, C# 9, no dependencies, so the same sources also compile inside Unity (2021.3+). The marker module
([`MB.FramePacing.Marker`](../marker/README.md)) and the data module ([`MB.FramePacing.Data`](../data/README.md)) reference it.

It is not published as a NuGet package: use the source at an `sdk-v*` tag, as a project reference to `MB.FramePacing.csproj` or as a
copy of `source/`. **Unity:** the [Unity package](../../unity/README.md) contains it.

| Type                  | What it is                                                                                                                                                               |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Point`               | A pixel position: origin at the top-left corner, +x to the right, +y down                                                                                                |
| `Rectangle`           | An integer pixel rectangle, `[Left, Right) × [Top, Bottom)`. Always valid: a negative width or height is 0 (`FromLeftTopRightBottom` too)                                |
| `TickCount64`         | A point on your steady clock in ticks of 100 ns (`FromNanoseconds`, `FromCounter` for `Stopwatch.GetTimestamp`), stored unsigned; compares and subtracts across the wrap |
| `TickCount32`         | A point on a 32-bit clock of ticks that wraps every 429.5 s; compares correctly across the wrap                                                                          |
| `TimeSpanUtil`        | `FromSeconds`: seconds to a `System.TimeSpan`, truncated to a tick on every runtime (Unity's `TimeSpan.FromSeconds` rounds to a millisecond)                             |
| `TimeSpan32`          | An unsigned 32-bit interval of 0 to 429.5 s: the form of the marker's 32-bit intervals                                                                                   |
| `TimeDuration`        | A `System.TimeSpan` that is never negative (a negative one becomes zero); two added are a duration, their difference a `TimeSpan`                                        |
| `NanosecondTimeSpan`  | A signed interval in nanoseconds, kept as a platform that counts in nanoseconds gives it (`FromTimeSpan` is exact, `ToTimeSpan` truncates to a tick)                     |
| `NanosecondTickCount` | A point on a clock that counts in nanoseconds, stored unsigned and compared across the wrap as `TickCount64` (`ToTickCount64` is the tick it is in)                      |

The time types (`source/Time/`) are the C++ core's, member for member; a signed interval is .NET's `System.TimeSpan`, whose ticks
are the SDK's unit. Out of range throws (`OverflowException` from the `From...` factories, `ArgumentOutOfRangeException` from
`TimeSpan32.FromTimeSpan` and `FromCounter`, `ArgumentException` from `TimeSpanUtil.FromSeconds` for NaN); nothing else throws or
allocates. The SDK never reads a clock: the application gives
its own clock's times, for example `TickCount64.FromCounter(Stopwatch.GetTimestamp(), Stopwatch.Frequency)`.

The C++ core (`MB::FramePacing` in [`sdk/cpp/core`](../../cpp/README.md#the-core)) has the same types; the Python package's root
(`mb_framepacing`) has the same `Point` and `Rectangle`.
