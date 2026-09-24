# Windows

Tested on Windows 11 (x64). Everything below uses [winget](https://learn.microsoft.com/windows/package-manager/winget/), which is
part of Windows 10/11, and PowerShell.

## 1. Install ffmpeg

```powershell
winget install Gyan.FFmpeg
```

Open a **new** terminal afterwards so the updated PATH is picked up, then check it with `ffmpeg -version` (5.1 or newer). You can
also download a build from [ffmpeg.org](https://ffmpeg.org/download.html#build-windows) and unzip it anywhere: the GUI's setup
dialog (**Find automatically** or **Browse...**) or `mb-framepacing config --set-ffmpeg C:\path\to\ffmpeg.exe` tells the tools
where it is.

## 2. Get mb-framepacing

### Build from source

Needs the .NET 10 SDK, Git and Python 3:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Git.Git
winget install Python.Python.3.13
```

In a new terminal:

```powershell
git clone https://github.com/Unarmed1000/mb-framepacing.git
cd mb-framepacing
python measure/build_standalone.py        # self-contained executables in measure\publish\win-x64\cli and \gui
```

Install both into one folder and put it on your PATH (once):

```powershell
$dest = "$env:LOCALAPPDATA\Programs\mb-framepacing"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Recurse -Force measure\publish\win-x64\cli\*, measure\publish\win-x64\gui\* $dest
[Environment]::SetEnvironmentVariable("Path", [Environment]::GetEnvironmentVariable("Path", "User") + ";$dest", "User")
```

Open a new terminal; `mb-framepacing` and `mb-framepacing-gui` now work from anywhere. To update later, `git pull`, build and
copy again. On an ARM PC the folders are called `win-arm64`.

Just trying it? Run it straight from the source instead of installing:

```powershell
dotnet run --project measure/app/FramePacing.Gui                  # the GUI
dotnet run --project measure/app/FramePacing -- selftest          # the command line: everything after -- goes to mb-framepacing
```

### Prebuilt

When a release is published, unzip its `win-x64` archive and run `mb-framepacing-gui.exe` (or `mb-framepacing.exe` in a
terminal). Nothing else is required: the executables are self-contained.

## 3. Check it works without hardware

```powershell
mb-framepacing selftest
```

It captures a synthetic game at 500 fps through the real recorder, analyses it and prints `PASS` when every frame matches the
known answer. If it reports dropped frames, the disk is too slow for that rate: use a faster SSD or a smaller `--size`.
Then start `mb-framepacing-gui`: the first time, a short setup dialog finds ffmpeg and asks where captures go.

## 4. Capture cards

Windows exposes capture cards as **DirectShow** devices (Elgato, AVerMedia, Magewell, Blackmagic and most USB/HDMI dongles).

```powershell
mb-framepacing devices --modes
```

lists them with their modes. Use the name as shown, for example:

```powershell
mb-framepacing capture -d "Cam Link 4K" --mode 1920x1080@60 --input-format nv12 --scale 960x540 -t 30s --analyze
```

Tips:

- Close the vendor's own capture software first: most cards can only be opened by one program at a time.
- Prefer an uncompressed format (`nv12`, `yuyv422`) over `mjpeg` when the card offers the rate you need; MJPEG blurs the
  marker (see the "Sizing" section of [marker-format.md](../marker-format.md#sizing)).
- Set the application's output to the card's native mode and capture at the same refresh rate (for example 1920×1080 at 240 Hz,
  captured at 240 fps). Turn off HDR for the capture.
- `dshow` buffers frames in memory (`-rtbufsize`, 1 GB by default); dropped frames are reported in the results.

## 5. Take a measurement

Continue with **[Using mb-framepacing](../usage.md)**: the test game, a capture card, importing video files or image folders,
and reading the results, in the GUI and on the command line.

## 6. Build the C++ marker library (for your application)

Install Visual Studio 2026 (or 2022) with the "Desktop development with C++" workload and CMake 4.0 or newer
(`winget install Kitware.CMake`), then:

```powershell
cd marker/cpp
cmake --preset windows
cmake --build --preset windows
ctest --preset windows
```

See [Integrating the marker](../integrating.md) for using it from your application.
