# macOS

Written for macOS 14 and newer on Apple Silicon or Intel, using [Homebrew](https://brew.sh).

## 1. Install ffmpeg

```sh
brew install ffmpeg
ffmpeg -version        # 5.1 or newer
```

## 2. Get mb-framepacing

### Build from source

Needs the .NET 10 SDK, Git and Python 3:

```sh
xcode-select --install                    # Git (and the compilers for step 6)
brew install --cask dotnet-sdk
brew install python
git clone https://github.com/Unarmed1000/mb-framepacing.git
cd mb-framepacing
python3 dotnet/build_standalone.py        # self-contained executables in dotnet/publish/osx-arm64/cli and /gui
```

Install both into one folder and put it on your PATH (once):

```sh
mkdir -p ~/Applications/mb-framepacing
cp -R dotnet/publish/osx-arm64/cli/ dotnet/publish/osx-arm64/gui/ ~/Applications/mb-framepacing/
echo 'export PATH="$HOME/Applications/mb-framepacing:$PATH"' >> ~/.zprofile
```

Open a new terminal; `mb-framepacing` and `mb-framepacing-gui` now work from anywhere. To update later, `git pull`, build and
copy again. On Intel Macs the folders are called `osx-x64`. The GUI is a plain executable for now (no `.app` bundle): start it
from the terminal.

Just trying it? Run it straight from the source instead of installing:

```sh
dotnet run --project dotnet/app/FramePacing.Gui                  # the GUI
dotnet run --project dotnet/app/FramePacing -- selftest          # the command line: everything after -- goes to mb-framepacing
```

### Prebuilt

When a release is published, unpack its `osx-arm64` (Apple Silicon) or `osx-x64` (Intel) archive. The executables are not
notarized, so remove the download quarantine once:

```sh
xattr -dr com.apple.quarantine mb-framepacing mb-framepacing-gui
./mb-framepacing-gui
```

## 3. Check it works without hardware

```sh
mb-framepacing selftest
```

Prints `PASS` when a synthetic 500 fps capture comes back frame-exact. If it reports dropped frames, the disk is too slow for that
rate: use a smaller `--size`. Then start `mb-framepacing-gui`: the first time, a short setup dialog finds ffmpeg and asks where
captures go.

## 4. Capture cards

macOS exposes capture cards as **AVFoundation** video devices (Elgato, Blackmagic UltraStudio, Magewell USB and UVC dongles).

```sh
mb-framepacing devices
```

lists them by index. Capture with the index:

```sh
mb-framepacing capture -d 0 --mode 1920x1080@60 --scale 960x540 -t 30s --analyze
```

**Camera permission:** macOS asks before any program reads a video device. The first capture from Terminal (or iTerm, or the GUI)
triggers the prompt. If you declined it, enable the app under **System Settings → Privacy & Security → Camera** and start it
again. Without permission ffmpeg reports that no frames arrive.

Tips:

- AVFoundation does not report the supported modes; use the card vendor's documentation or try `--mode` values.
- Keep the Mac awake during long captures (`caffeinate -dims mb-framepacing capture ...`).

## 5. Take a measurement

Continue with **[Using mb-framepacing](../usage.md)**: the test game, a capture card, importing video files or image folders,
and reading the results, in the GUI and on the command line.

## 6. Build the C++ marker library (for your application)

```sh
xcode-select --install     # AppleClang 15+
brew install cmake         # 4.0 or newer
cd cpp
cmake --preset macos
cmake --build --preset macos
ctest --preset macos
```

See [Integrating the marker](../integrating.md) for using it from your application.
