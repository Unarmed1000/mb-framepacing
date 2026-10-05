# Encoding performance: how the marker's QR encoder was made faster

An application encodes up to two QR symbols every frame: the main marker (QR version 6, 41×41 modules) and, when it draws one, the
sync marker (version 2, 25×25: required for a camera, optional for a capture card). Measured with the benchmarks, that encoding took
far longer than everything else the marker does: about 0.33 ms for a main marker and 0.11 ms for a sync marker, while drawing either
takes a few microseconds.

The C++ and C# marker modules now have a QR encoder made for the marker. It produces **exactly the same symbols** as the encoders it
replaces (in C++ the [QR Code generator library](https://www.nayuki.io/page/qr-code-generator-library) by Project Nayuki, qrcodegen;
in C# a port of it), and is 20 to 40 times faster.

## Results

Release builds, one core of a 4.7 GHz desktop CPU. "Original" is qrcodegen (C++) or its module-by-module port (C#), "own" is the
module's encoder; both encode the same payloads, a new one every iteration, as an application does every frame.

The payload's own bytes, its CRC-32 included, are a small part of that: 0.17 µs for a frame marker, 0.24 µs for a start marker and
0.06 µs for a sync marker in C++ (0.14, 0.21 and 0.03 µs in C#).

| What                                     | Language, compiler | Original |     Own | Faster by |
| ---------------------------------------- | ------------------ | -------: | ------: | --------: |
| Main marker (frame, 57 bytes)            | C++, MSVC          |   351 µs |  9.2 µs |       38× |
| Main marker (start, 81 bytes)            | C++, MSVC          |   354 µs |  9.3 µs |       38× |
| Sync marker (20 bytes)                   | C++, MSVC          |   109 µs |  4.0 µs |       27× |
| Main marker (frame, 57 bytes)            | C++, Clang         |   213 µs |  8.8 µs |       24× |
| Main marker (start, 81 bytes)            | C++, Clang         |   213 µs |  8.8 µs |       24× |
| Sync marker (20 bytes)                   | C++, Clang         |    66 µs |  3.8 µs |       17× |
| A whole frame: both markers, as geometry | C++, MSVC          |   492 µs |   20 µs |       25× |
| Main marker (frame, 57 bytes)            | C#, .NET 10        |   309 µs | 12.5 µs |       25× |
| Main marker (start, 81 bytes)            | C#, .NET 10        |   334 µs | 13.1 µs |       26× |
| Sync marker (20 bytes)                   | C#, .NET 10        |    98 µs |  5.6 µs |       18× |
| A whole frame: both markers, as geometry | C#, .NET 10        |   378 µs |   23 µs |       16× |

The numbers move by a few percent from run to run. The C# frame's original time is the sum of its measured parts (the two encodes
and the drawing); the others are measured as a whole. C# has no bit counting instructions in .NET Standard 2.1 (which Unity needs), so
its encoder counts bits with plain arithmetic.

## Where the time went

A QR symbol's data area is filled straight from the payload's bytes. Raw bytes can give large areas of one colour, or shapes that
look like the three corner squares a reader finds the symbol by. So the QR standard XORs the data area with one of eight fixed
patterns, the **masks**, and writes which one into the format bits next to the corner squares. A reader undoes it. Any reader decodes
any mask: the choice only affects how easy the symbol is to find and sample.

The standard recommends how to choose: build the symbol with each of the eight masks, give each a **penalty score**, and keep the
lowest. The score has four parts:

- runs of five or more modules of one colour in a row or column;
- 2×2 blocks of one colour;
- shapes like a corner square (dark, light, dark, light, dark in the proportions 1:1:3:1:1, with light beside them);
- how far the share of dark modules is from one half.

The marker format requires every implementation to produce the same modules (`marker-format.md`), so every implementation must pick
the same mask: the rule is part of the format, and it stays.

qrcodegen follows the rule one module at a time. For each mask it visits every module to apply the mask, draws the format bits,
walks every row and every column module by module to score them, and visits every module again to remove the mask. With a fixed
mask the same library encodes a main marker in 9 µs (Clang) to 40 µs (MSVC). **Choosing the mask was 90 to 96 % of the time.**

## What changed

The rule is the same. The symbol is held differently, so that the rule can be applied to whole lines at once.

1. **A line is one 64-bit word.** The symbol is kept as one word per row and one per column (a main marker has 41 modules per
   line). Both directions are then scored by the same code.
2. **What never changes is computed at compile time.** For the two QR versions the marker uses: which modules carry data, the
   function patterns (corner squares, timing, alignment), where the format bits go, and the order in which the data bits are placed.
   Placing the data is one table lookup per bit, without testing each module for being a function module.
3. **A mask is an XOR.** Every mask's pattern repeats after 12 rows and 12 columns. Twelve words per mask describe it, and applying a
   mask to a line is one AND (with the line's data modules) and one XOR, instead of 41 module visits with a division each.
4. **The error correction uses logarithm tables** (made at compile time) instead of eight shift-and-add steps per multiplication.
5. **A line is scored with word operations:**
   - runs of five or more: comparing a word with itself shifted by one marks equal neighbours; four such marks in a row are a run
     of five, and counting bits gives the score;
   - corner-square shapes of unit 1 and 2 (the only ones possible unless a line has nine or more dark modules in a row): the word is
     matched against the shape with shifts and ANDs;
   - 2×2 blocks: three XOR and AND operations and one bit count per pair of rows;
   - the dark share: bit counts.

   A line with nine or more dark modules in a row can hold a larger shape. It is rare, and walked run by run as qrcodegen walks
   every line.

6. **A mask that can no longer win is dropped.** The score only grows while it is added up, so scoring a mask stops when it reaches
   the best score so far.
7. **The result is packed from the row words**, not module by module.

What each step gained, for a main marker with MSVC:

| Step                                                                   |   Time |
| ---------------------------------------------------------------------- | -----: |
| qrcodegen                                                              | 326 µs |
| Bit rows and columns, compile-time tables, masks by XOR (steps 1 to 4) |  73 µs |
| Lines scored with word operations (step 5)                             | 9.2 µs |
| Stopping a mask that can no longer win (step 6)                        | 9.1 µs |

Almost all of it is steps 1 to 5. The early stop is worth about 5 %: the eight masks' scores are close, so a mask is rarely dropped
early.

## How "exactly the same symbols" is checked

qrcodegen stays in the repository, unchanged, as the reference (`sdk/cpp/marker/reference/third_party/qrcodegen`). The library no
longer contains it; only the tests and the benchmarks build it. The tests (`sdk/cpp/marker/tests/QrEncoderTests.cpp`) compare the
two encoders:

- every symbol, module by module, for every payload length of both versions, with random bytes and with the regular bytes a marker
  is mostly made of, and for thousands of further payloads;
- every one of the eight masks: the masked symbol and its penalty score;
- payloads whose two best masks score the same: both encoders take the lower numbered one;
- the penalty score of symbols drawn for the purpose: random, sparse, dense, striped, and corner-square shapes of every unit with
  every amount of light beside them, in rows and in columns.

Beyond the unit tests:

- **A stress test** (`sdk/cpp/marker/tests/stress`, `mb_framepacing_marker_qr_stress`) compares the encoders on far more input, on
  every core: payloads of many kinds (random, mostly zero, mostly 0xFF, repeated patterns, the previous payload with one bit or byte
  changed, the shortest and longest that fit, and too long ones both must refuse), real markers through `GenerateModules` with its
  packing, drawn symbols made of runs that form corner-square shapes, symbols with a dark share exactly on a step of the balance rule,
  and the scoring of single lines. One run compared 12 million payloads (each also as a marker, every eighth with all eight masks and
  their scores), 4 million drawn symbols, 2 billion lines of 25 and 41 modules, and **every possible line of 25 modules** (33 million),
  without a difference. The tests run a short version of it; `mb_framepacing_marker_qr_stress` without arguments runs a long one.
- **A fuzz target** (`sdk/cpp/marker/tests/fuzz`, libFuzzer with AddressSanitizer, `-DMB_FRAMEPACING_BUILD_FUZZERS=ON` with Clang)
  lets the fuzzer choose payloads and drawn symbols to reach every branch of the encoder; a difference from qrcodegen aborts. CI runs
  it for a minute on every change.
- **The checks were checked.** Fourteen deliberate mistakes were put into the encoder, one at a time (a wrong run length in the
  corner-square rule, a wrong shift, a tie taking the last mask, a wrong pad byte, a wrong mask rule, a wrong rounding in the balance,
  and so on). The stress test reported every one within a second. A fifteenth change went unnoticed, rightly: it shortened the light
  border after a line from 41 to 40 modules, which no rule can tell apart.

**C#** is checked the same way. Its reference is the encoder the library had before (`sdk/csharp/marker/Reference/ReferenceQrEncoder.cs`,
module by module, compiled into the unit tests and benchmarks only), itself pinned to the golden modules by a test. `QrEncoderTests`
compares symbols, masks, scores, ties and drawn symbols; `QrEncoderStressTests` is the stress test (one run with
`MB_QR_STRESS_SCALE=200`: 4.8 million payloads, 4.8 million markers, 3.2 million drawn symbols, 800 million lines and every line of 25
modules, without a difference), and ten deliberate mistakes in the C# encoder were each reported by it.

The golden markers (`sdk/test-data/markers`) are unchanged, and all three libraries (the Python one keeps its module-by-module
encoder) produce the same modules. The new encoder is covered completely by the tests (regions, functions, lines and branches).

## What it costs

The tables are data in the library: about 8 KiB. An executable that uses the marker module grows by 20.4 KiB in a Release build
(28.9 KiB with qrcodegen, whose code was larger) and by 18.1 KiB in a build optimized for size (12.8 KiB before: the tables do not
shrink). See "What it adds to your executable" in `sdk/cpp/README.md`. The C# assembly `MB.FramePacing.Marker` grows from 25.5 to 27.0 KiB;
its tables are made when the first encoder is created. (The CRC-32 every payload ends with came later and is not part of the
encoder: with it the numbers are 20.8 and 18.3 KiB, and 27.5 KiB for the C# assembly.)

## What was left out

**A fixed mask.** Always using one mask would remove the choice altogether (a few microseconds per marker). It is a change of the
marker format, and nothing would then guard against a payload that happens to give a symbol that is hard to find or read, which is
what the rule exists for. With the choice down to a few microseconds there is little left to gain.

## Run it yourself

```
cd sdk/cpp && cmake --preset windows && cmake --build --preset windows
build/windows/marker/Release/mb_framepacing_marker_benchmarks --benchmark_filter="GenerateModules|QrcodegenEncode|Frame"
```

`GenerateModules` is the module's encoder (with packing the result), `QrcodegenEncode` the original with the same payloads. The
arguments are the marker kinds: 0 a frame marker, 1 a start marker, 3 the sync marker.

C# (BenchmarkDotNet; `ReferenceEncode` is the original):

```
dotnet run -c Release --project sdk/csharp/marker/Benchmarks/MB.FramePacing.Marker.Benchmarks.csproj -- --filter "*EncodeBenchmarks*"
```

Refresh the numbers in this document in the change that alters an encoder.
