# Capture data format (`captures.mbcd`)

Every capture and import writes `captures.mbcd` next to `capture.json`: one record per captured frame, with the frame's timestamps and
what its markers said. The markers are decoded **while capturing**, so the frames themselves are not needed afterwards; they are only
stored (in `frames.mbfc`) with `--keep-frames` or the GUI's **Store video frames**. The analysis starts from this file.

A capture that only has `frames.mbfc` (made before the capture data existed, or kept for re-decoding) gets `captures.mbcd` the first
time it is analysed; `analyze --redecode` decodes the frames again and replaces it. Both ways decode the same way (a search frame by
frame until the markers are located, then the locked decoder), so they give the same records.

All numbers are little endian. Times are whole nanoseconds. A file from before the nanoseconds counted its times in ticks of 100 ns
in the same bytes and under the same format version: it reads without an error and a hundred times too small, and the markers in
its records are in ticks too. Record such a capture again.

The SDK's data modules ([the SDK](../README.md#the-data-module)) read this file in C#, Python and C++; the C# module also writes it, and the tools
write through it.

## Header (256 bytes)

| Offset | Size | Field                                                                                                                              |
| ------ | ---- | ---------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 4    | Magic `MBCD` (`0x4443424D`)                                                                                                        |
| 4      | 2    | Format version: 1. Readers refuse a newer version ("update the tools")                                                             |
| 6      | 2    | Header size: 256                                                                                                                   |
| 8      | 4    | Record size: 256                                                                                                                   |
| 12     | 4    | Flags: bit 0 = the frames were stored too (`frames.mbfc`), bit 1 = camera capture (very experimental)                              |
| 16     | 8    | Stored frame width, height (`i32` each): the frames the markers were read from                                                     |
| 24     | 8    | Nominal frame rate: numerator, denominator (`u32` each; 0/0 = unknown)                                                             |
| 32     | 8    | Source width, height (`i32` each), before scaling and cropping; 0 = the stored width, height                                       |
| 40     | 16   | Region of the source that was stored: x, y, width, height (`i32` each; all 0 = the whole frame)                                    |
| 56     | 8    | Reserved (0)                                                                                                                       |
| 64     | 4    | Marker lock count (`u32`, at most 4; 0 = no marker was found)                                                                      |
| 68     | 4    | Reserved (0)                                                                                                                       |
| 72     | 96   | Up to 4 locks of 24 bytes: bounds x, y, width, height (`i32` each, stored pixels, the quiet zone included) and module size (`f64`) |
| 168    | 16   | Second region of the source, stored below the first: x, y, width, height (`i32` each; all 0 = none)                                |
| 184    | 72   | Reserved (0)                                                                                                                       |

The first lock is the main marker (frame, start and end markers); a second one is the sync marker (the tearing check) or, for a
camera capture, the second zone. The locks are where the capture's decoder found the markers; the header is completed when the
capture ends.

A capture that stores only the markers (`import`'s default for a recording, `--roi auto`) crops the main marker's region and, when
the source has a sync marker, the sync marker's too, and stores the two as one frame: the first region on top, the second below
it, both downscaled alike, the narrower one padded with white on its right. The locks are in that stored frame. Without a sync
marker, and with a region given as one rectangle, there is one region and the second is all 0.

## Records (256 bytes each)

| Offset | Size | Field                                                                                                         |
| ------ | ---- | ------------------------------------------------------------------------------------------------------------- |
| 0      | 8    | Capture index (`i64`): the source's frame counter. A gap is captures the recorder dropped                     |
| 8      | 8    | Host time (`i64`): when the frame arrived, on the host's steady clock since the capture started               |
| 16     | 8    | Device time (`i64`): the capture device's timestamp, `i64` minimum = none                                     |
| 24     | 4    | Source drops (`u32`): how many frames the capture source reported dropping since the previous record          |
| 28     | 1    | Status: 0 = undecodable, 1 = decoded, 2 = torn (the markers disagree, or only the sync marker was read)       |
| 29     | 1    | Main marker byte count (0 = not read)                                                                         |
| 30     | 1    | Second marker byte count (0 = not read)                                                                       |
| 31     | 1    | Reserved (0)                                                                                                  |
| 32     | 112  | The main marker's encoded bytes exactly as read from the QR code ([marker format](marker-format.md)), then 0s |
| 144    | 112  | The second marker's encoded bytes (the sync marker, or a camera's second zone), then 0s                       |

The two slots are equal, and each holds any payload a main marker's QR code can carry (106 bytes; the longest today, a start
marker, is 81), so a field added to the markers does not change the records. A reader refuses a file whose header gives another
record size.

The first 28 bytes have the layout of a `frames.mbfc` record header, so a record's capture part is the same in both files. A capture
that was stopped mid-write may end with a partial record; readers ignore it.

The analysis also writes the records as text: `analysis/captures.csv` has the host and device time and the main marker's bytes
(`payloadHex`) of every captured frame.
