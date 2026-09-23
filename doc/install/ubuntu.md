# Ubuntu

Written for Ubuntu 24.04 and newer; other Debian based distributions work the same way.

## 1. Install ffmpeg

```sh
sudo apt update
sudo apt install ffmpeg v4l-utils
ffmpeg -version        # 5.1 or newer (24.04 ships 6.1)
```

## 2. Get mb-framepacing

### Build from source

Needs the .NET 10 SDK, Git and Python 3 (Ubuntu has Python already):

```sh
sudo apt install dotnet-sdk-10.0 git     # if dotnet-sdk-10.0 is not found: sudo add-apt-repository ppa:dotnet/backports && sudo apt update
git clone https://github.com/Unarmed1000/mb-framepacing.git
cd mb-framepacing
python3 measure/build_standalone.py        # self-contained executables in measure/publish/linux-x64/cli and /gui
```

Install both into one folder and link them into `~/.local/bin`, which Ubuntu puts on your PATH:

```sh
mkdir -p ~/.local/share/mb-framepacing ~/.local/bin
cp -r measure/publish/linux-x64/cli/. measure/publish/linux-x64/gui/. ~/.local/share/mb-framepacing/
ln -sf ~/.local/share/mb-framepacing/mb-framepacing ~/.local/bin/mb-framepacing
ln -sf ~/.local/share/mb-framepacing/mb-framepacing-gui ~/.local/bin/mb-framepacing-gui
```

If `~/.local/bin` did not exist before, log out and in once so it is added to PATH. To update later, `git pull`, build and copy
again. On ARM machines the folders are called `linux-arm64`.

Just trying it? Run it straight from the source instead of installing:

```sh
dotnet run --project measure/app/FramePacing.Gui                  # the GUI
dotnet run --project measure/app/FramePacing -- selftest          # the command line: everything after -- goes to mb-framepacing
```

The GUI needs a few desktop libraries that most desktops already have:

```sh
sudo apt install libice6 libsm6 libfontconfig1
```

### Prebuilt

When a release is published, unpack its `linux-x64` (or `linux-arm64`) archive and run `./mb-framepacing-gui` or
`./mb-framepacing`; the executables are self-contained.

## 3. Check it works without hardware

```sh
mb-framepacing selftest
```

Prints `PASS` when a synthetic 500 fps capture comes back frame-exact. If it reports dropped frames, the disk is too slow for that
rate: use a faster SSD or a smaller `--size`. Then start `mb-framepacing-gui`: the first time, a short setup dialog finds ffmpeg
and asks where captures go.

## 4. Capture cards

Linux exposes capture cards as **Video4Linux2** devices (`/dev/video0`, `/dev/video1`, ...).

```sh
sudo usermod -aG video $USER     # once, then log out and in again: access to /dev/video*
mb-framepacing devices --modes   # lists the devices and their formats
v4l2-ctl --list-formats-ext -d /dev/video0   # frame rates per size, if you need them
```

Capture with the device path (or just its number):

```sh
mb-framepacing capture -d /dev/video0 --mode 1920x1080@60 --input-format yuyv422 --scale 960x540 -t 30s --analyze
```

Tips:

- Many USB capture devices create two nodes per card; the first one usually carries the video.
- Prefer `yuyv422`/`nv12` over `mjpeg` when the card reaches the rate you need.
- v4l2 gives kernel timestamps for every frame, which the analysis uses automatically.

## 5. Take a measurement

Continue with **[Using mb-framepacing](../usage.md)**: the test game, a capture card, importing video files or image folders,
and reading the results, in the GUI and on the command line.

## 6. Build the C++ marker library (for your application)

The library needs CMake 4.0 or newer; Ubuntu's `cmake` package may be older. Get a current one from Kitware's
[apt repository](https://apt.kitware.com/), with `sudo snap install cmake --classic`, or with `pip install cmake`. Then:

```sh
sudo apt install build-essential        # GCC 12+ (or clang-16+ for the linux-clang preset)
cd marker/cpp
cmake --preset linux
cmake --build --preset linux
ctest --preset linux
```

See [Integrating the marker](../integrating.md) for using it from your application.
