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

A `tools-v*` tag builds the self-contained executables as an artifact of its CI run (`mb-framepacing-Windows`, kept 30 days).
Download it and run `win-x64/gui/mb-framepacing-gui.exe` (or `win-x64/cli/mb-framepacing.exe` in a terminal). Nothing else is
required. `python measure/build_standalone.py` builds the same from source.

## 3. Check it works without hardware

```powershell
mb-framepacing selftest
```

It captures a synthetic game at 500 fps through the real recorder, analyses it and prints `PASS` when every frame matches the
known answer. If it reports dropped frames, this machine cannot decode that rate: try a lower `--fps` or a smaller `--size` (the disk only matters
with `--keep-frames`).
Then start `mb-framepacing-gui`: if ffmpeg is not found, a short setup dialog helps you get it and asks where captures go.

## 4. Take a measurement

Continue with **[Using mb-framepacing](../usage.md)**: recording a capture card with OBS, importing the recording, and
reading the results, in the GUI and on the command line. Recording a capture card live with mb-framepacing itself is
experimental: [Live capture](../live-capture.md) has the DirectShow details.

## 5. Build the C++ marker library (for your application)

Install Visual Studio 2026 with the "Desktop development with C++" workload and CMake 4.0 or newer
(`winget install Kitware.CMake`), then:

```powershell
cd sdk/cpp
cmake --preset windows
cmake --build --preset windows
ctest --preset windows
```

See [Integrating the marker](../../../sdk/doc/integrating.md) for using it from your application.
