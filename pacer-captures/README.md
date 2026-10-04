# Pacer captures

Frame logs of the experimental [frame pacer](../sdk/doc/pacer.md) on real swap chains, kept as they were captured. They are what
the guide's statements about real swap chains rest on, and what a change to the pacer can be held against. They are not test data:
no test reads them. The small logs the tests pin are in `sdk/test-data/pacer`.

Each capture session is one zip file, with a document here that says what its logs show. The zips are not in the source tree:
they are assets of this repository's [`pacer-captures` release](https://github.com/Unarmed1000/mb-framepacing/releases/tag/pacer-captures). Download one and unpack it to read the logs; inside,
`README.txt` says what was run.

| File                                                                                                                                        | Runs | Unpacked | What it is                                                                           |
| ------------------------------------------------------------------------------------------------------------------------------------------- | ---- | -------- | ------------------------------------------------------------------------------------ |
| [`2026-10-04-windows-hold.zip`](https://github.com/Unarmed1000/mb-framepacing/releases/download/pacer-captures/2026-10-04-windows-hold.zip) | 247  | 113 MB   | How a frame is held for more than one refresh on plain Vulkan, Windows, 50 to 240 Hz |

The files name no GPU or CPU model and no directory of the machine they were captured on: the GPU is its vendor and driver version,
the directories are `<output>`, `<sdk>` and `<home>`. Keep it that way in anything added here.

## 2026-10-04-windows-hold

The Vulkan FramePacing sample of the unofficial [gtec-demo-framework](https://github.com/Unarmed1000/gtec-demo-framework) with the
pacer at `b5b6ab3`, a release build on Windows 11 with a FIFO swap chain, a 1600x900 window on a display set to 240, 120, 60 and
50 Hz, and a second display at 120 Hz. Present timing (`VK_EXT_present_timing`) is on only to measure when the frames were shown.
A run whose name ends in `_idle` or `_loaded` is one of a pair: the same run on an idle machine and under a CPU load on every
logical CPU.

A run is four files: `<run>.csv` (a row per frame, times in ticks of 100 ns), `<run>.events.csv` (facts and events),
`<run>.run.txt` (notes and checks) and `<run>.app.log`. A folder's `summary.txt` has one line per run.

| Folder                                                  | Runs | What was varied                                                                               |
| ------------------------------------------------------- | ---- | --------------------------------------------------------------------------------------------- |
| `240hz-vsync-sources`                                   | 22   | The vsync wait with two sources of the vsync time, 120, 60 and 30 fps, two timer sleep runs   |
| `second-display-120hz-vsync-sources`                    | 10   | The same with the window on the 120 Hz second display, the primary at 240 Hz                  |
| `240hz-phase-sweep`, `120hz-`, `60hz-`, `50hz-`         | 80   | Where in the refresh the present is placed, from 5 to 95 % of the refresh before the target   |
| `60hz-plain-vulkan`                                     | 26   | Timer sleep against vsync wait, 30 and 20 fps, CPU work of 20, 90 and 130 % of a refresh      |
| `50hz-plain-vulkan`                                     | 22   | The same at 25 fps                                                                            |
| `120hz-plain-vulkan`                                    | 30   | The same at 60, 30 and 15 fps                                                                 |
| `240hz-plain-vulkan`                                    | 30   | The same at 120, 60 and 30 fps with GPU work, one and two frames in flight                    |
| `240hz-gsync-fullscreen-only-variable-refresh`, `-hold` | 12   | G-SYNC on for full screen apps only                                                           |
| `240hz-gsync-windowed-and-fullscreen-…`                 | 12   | G-SYNC on for windowed and full screen apps                                                   |
| `swapchain-refresh-probes`                              | 3    | Short runs without notes: the refresh the swap chain reports with displays of different rates |

G-SYNC was off for every folder but the four named for it, and present feedback was on only in the 8 runs of the two G-SYNC
`-hold` folders.

What they show is in [Windows hold captures, 2026-10-04](2026-10-04-windows-hold.md), with the charts and the row per run
(`2026-10-04-windows-hold/runs.csv`) that `tools/pacer_capture_report.py` works out from the zip.

## Adding a capture

One zip per session, named for its date and what it is about, with a `README.txt` inside that says what was run, on what, and what
is known about it. Check the files for hardware models, user names and local directories before packing them. Upload the zip to the
release (`gh release upload pacer-captures <zip>`) and add its row to the table above. Git ignores a zip in this folder, so one can
be downloaded here to work with it.

Its numbers come from its logs, not from a summary someone wrote: `python tools/pacer_capture_report.py <zip>` writes the row per
run and the charts into a folder named as the zip, `--update-doc` fills the tables of the session's document (the zip's name with
`.md`), and `--check` fails when any of them is not what the zip gives. The charts and tables it makes are for the runs of the
session it was written for; a session with other runs needs its own selections there.
