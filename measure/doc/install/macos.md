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
python3 measure/build_standalone.py        # self-contained executables in measure/publish/osx-arm64/cli and /gui
```

Install both into one folder and put it on your PATH (once):

```sh
mkdir -p ~/Applications/mb-framepacing
cp -R measure/publish/osx-arm64/cli/ measure/publish/osx-arm64/gui/ ~/Applications/mb-framepacing/
echo 'export PATH="$HOME/Applications/mb-framepacing:$PATH"' >> ~/.zprofile
```

Open a new terminal; `mb-framepacing` and `mb-framepacing-gui` now work from anywhere. To update later, `git pull`, build and
copy again. On Intel Macs the folders are called `osx-x64`. The GUI is a plain executable for now (no `.app` bundle): start it
from the terminal.

Just trying it? Run it straight from the source instead of installing:

```sh
dotnet run --project measure/app/FramePacing.Gui                  # the GUI
dotnet run --project measure/app/FramePacing -- selftest          # the command line: everything after -- goes to mb-framepacing
```

### Prebuilt

A `tools-v*` tag builds the self-contained executables as an artifact of its CI run (`mb-framepacing-macOS`, osx-arm64 for Apple
Silicon, kept 30 days); for an Intel Mac, build from source with `python3 measure/build_standalone.py --rid osx-x64`. The
executables are not notarized, so remove the download quarantine once:

```sh
xattr -dr com.apple.quarantine mb-framepacing mb-framepacing-gui
./mb-framepacing-gui
```

## 3. Check it works without hardware

```sh
mb-framepacing selftest
```

Prints `PASS` when a synthetic 500 fps capture comes back frame-exact. If it reports dropped frames, this machine cannot decode that rate: try a lower `--fps` or a smaller `--size` (the disk only matters
with `--keep-frames`).
Then start `mb-framepacing-gui`: if ffmpeg is not found, a short setup dialog helps you get it and asks where captures go.

## 4. Take a measurement

Continue with **[Using mb-framepacing](../usage.md)**: recording a capture card with OBS, importing the recording, and
reading the results, in the GUI and on the command line. Recording a capture card live with mb-framepacing itself is
experimental: [Live capture](../live-capture.md) has the AVFoundation details (on macOS it is probably too slow to be usable).

## 5. Build the C++ marker library (for your application)

```sh
xcode-select --install     # AppleClang 15+
brew install cmake         # 4.0 or newer
cd sdk/cpp
cmake --preset macos
cmake --build --preset macos
ctest --preset macos
```

See [Integrating the marker](../../../sdk/doc/integrating.md) for using it from your application.
