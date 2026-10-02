# Roadmap

What is planned but not done yet. Nothing here is promised for a particular release.

## HDR capture

Today the guides say to turn HDR off while capturing, and the Unity overlay warns when HDR output is active. That is only needed when
the capture path cannot carry HDR; a game that ships in HDR, captured by a card that records HDR10 or converts HDR to SDR itself,
should be measurable as it is.

- **When HDR must be off:** an SDR-only capture card. Usually the PC does not even offer HDR on that output, because the card does not
  report HDR support; a splitter can hide that (the monitor reports HDR, the card receives a signal it does not understand).
- **Why the marker should survive HDR:** the decoder needs two clearly different levels, not exact black and white: the search
  binarizes against local contrast, and a locked marker is sampled against a threshold halfway between its finder patterns' dark and
  light levels. Drawn last, after the HDR output transform, black stays black and white becomes a bright level (80 nits for 1.0 in
  scRGB, the top of the range for 1.0 in HDR10/PQ; about luma 125 of 255 for 80 nits in a PQ recording).

The work:

1. **Decoder tests at reduced contrast:** the golden markers decoded with black at 16 and white at 125 or 100, in limited range, and
   through a gamma curve: what an HDR recording or a card's HDR-to-SDR conversion delivers.
2. **Docs:** "turn HDR off" becomes "match HDR to what the capture path carries", with the splitter trap, and a section in
   [Integrating the marker](../sdk/doc/integrating.md) on which white to write in scRGB and in PQ (a set brightness, such as the BT.2408
   reference white of 203 nits).
3. **Unity:** with HDR output active, the overlay writes a white of that set brightness instead of 1.0, and its warning says the
   capture card must capture or convert HDR.
4. **Capture tool:** record the input's transfer function (ffmpeg reports it) in `capture.json`, and name an HDR signal as a likely
   cause when many captures cannot be decoded.
5. **Validate with real hardware:** an HDR game captured by an HDR capture card, and by one that converts to SDR. Until then the HDR
   path is documented as untested.

## The frame pacer

The SDK's [frame pacer](../sdk/doc/pacer.md) is a first version and experimental: a baseline that needs only a steady clock and a
`Present` that waits for vsync, so it works on any platform, and it is off by default. It has only run against its own simulation.

**Out of experimental:** measure it on real swap chains, with the marker and the tools, on each platform and for each way of applying
a schedule (the platform's swap interval, presenting a frame again, sleeping until the frame is due). Until then it stays off by
default and its API may change.

**Possible upgrades.** Each is for platforms that offer it, never a requirement of the baseline; the guide's
[Not used yet](../sdk/doc/pacer.md#not-used-yet) says where each exists:

- **Vsync times the platform reports:** the intended display time without the jitter of the frame starts.
- **Predicted display times:** the animation time the platform itself aims for.
- **Presentation feedback:** frames that were late although they were presented in time.
- **Scheduled presents and per-frame targets:** back at full rate a frame sooner after one slow frame, and no sleep that guesses.
- **The refresh period measured from the frames:** a change of rate followed without being told, 59.94 Hz taken for 60.
- **Slewing against drift:** animation that stays in step with audio or a server over hours.
- **Variable refresh and vsync off:** pacing where there is no grid of refreshes to round to.
- **A C# port** (`MB.FramePacing.Pacer`): the same pacer for .NET, giving the golden data's results byte for byte.

## Later

- **Synced playback (GUI):** click a spike in a chart to open the captured frame it came from.
- **A C++17 fallback** for the C++ marker library, for toolchains without C++20.
