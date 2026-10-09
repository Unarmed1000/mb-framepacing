# Roadmap

Possible ways forward, not a plan: each section is an option the project could take up, written down so the thinking is not lost.
Nothing here is decided, scheduled or promised, and an option may be changed or dropped.

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

The SDK's [frame pacer](../sdk/doc/pacer.md) is experimental and off by default. It paces from what an application says its
platform can do: with a steady clock and the display's refresh period at the least, and better with a wait until a present was
shown, the display's vertical blank times or a time on the present. It is checked against its own simulation and, on one machine,
against the display times a graphics driver reports (a first integration, the author's unofficial gtec-demo-framework: see the
guide's Status), not yet measured with the tools. [Its design document](../sdk/doc/pacer-design.md) has every measurement so far
and a list of what is still missing.

**Out of experimental:** measure it on real swap chains, with the marker and the tools, on each platform and for each way of
pacing it has (its tiers). Until then it stays off by default and its API may change.

**Possible upgrades.** Each is for platforms that offer it, never a requirement of the baseline; the guide's
[Not used yet](../sdk/doc/pacer.md#not-used-yet) says where each exists:

- **A pacer for a display's side that skips a frame that is overdue:** the tier is rated, and a set that reaches it is paced as
  if every frame were shown.
- **Pacing by display times:** frames that wait seen and taken away where there is no wait for a present. Display times are
  statistics only today. An earlier pacer measured its frames by them; in the first integration's sample it made no difference
  at work of 20 % and 130 % of a refresh and was no better at 90 %.
- **Predicted display times:** the animation time the platform itself aims for.
- **An aim the pacer picks:** low latency where it costs no frame rate, without the application choosing.
- **The refresh period measured from the frames:** a change of rate followed without being told, 59.94 Hz taken for 60.
- **Slewing against drift:** animation that stays in step with audio or a server over hours.
- **Variable refresh and vsync off:** pacing where there is no grid of refreshes to round to. The pacer does not pace such a
  display today. The way thought of is more tier pacers behind the same calls, with a clock and a rule of their own: a frame
  time chosen from the display's range and held, not a multiple of the refresh, changed rarely and in steps. Open: a swap chain
  did not say that variable refresh was on, so the application would have to tell the pacer (the guide's
  [A variable refresh rate](../sdk/doc/pacer.md#a-variable-refresh-rate) lists where it can ask, and how to measure it); and a
  capture card does not see variable refresh, so the tools could not check it.
- **A C# port** (`MB.FramePacing.Pacer`): the same pacer for .NET, giving the golden data's results byte for byte.

## A capture card and a camera on the same run

Two recordings of one run, made at the same time: a capture card records what the PC sends to the display, and a camera films what
the display shows ([camera capture](../measure/doc/camera.md), very experimental). Both see the same markers, so the two analyses
can be lined up frame by frame and compared. Today each recording is analysed on its own, and nothing says how far to trust either.

What comparing them would confirm:

- **The capture card's result:** that the display shows the frames as its input carries them. A capture card records the signal,
  usually from a second output or behind a splitter; the display may add processing or run at another rate. A frame the display
  held longer or never showed would stand out as a difference between the two.
- **The camera's result:** with the capture card as the reference, the camera's display times and the refresh rate it calculates
  can be checked on real hardware, which is what camera capture needs before it can stop being very experimental
  ([status](../measure/doc/camera-status.md)).

The work:

1. **Match the frames** of the two analyses by run id and frame index, the application's own counter (each recording's capture
   index is its own and is never compared).
2. **Fit the two clocks:** the recordings have separate clocks, so a line through the matched frames' display times gives their
   offset and the difference in rate. What is left per frame is the disagreement between the two.
3. **Report it:** the difference per frame in display time and display time step, the frames one recording has and the other has
   not, and summary numbers; as a command and a card.
4. **Validate with real hardware:** a capture card and a camera on one display, at a few refresh rates.

Open question: the fit removes the constant offset between the clocks, so it shows jitter and drift but not the display's latency.
That would need a time both recordings share.

## Audio markers

The marker is in the picture only. An audio counterpart, an "audio QR code", would let the tools measure the sound against the
frames: the application writes a short machine-readable signal into its audio output that says which run it is and where its audio
clock was, the recording keeps it in its audio track, and the tools find it there. That would give the sound's offset from the
picture, how that offset drifts and jitters over a run, and the audio buffers that were dropped or repeated.

What exists (looked up in October 2026; none of it measured by this project yet):

- **A flash and a beep**, the film and television way: the [2-pop](https://en.wikipedia.org/wiki/2-pop), and
  [EBU R37](https://tech.ebu.ch/docs/r/r037.pdf)'s one white frame with a 1 kHz tone of the same length. Meters built for it
  ([Sync-One2](https://harkwood.co.uk/products/sync-one2/)) resolve 0.05 ms. It carries no payload: nothing says which frame or
  which run a beep belongs to.
- **SMPTE linear timecode** ([LTC](https://en.wikipedia.org/wiki/Linear_timecode)): 80 bits per video frame in biphase mark code
  (the time, 32 user bits, a 16-bit sync word), without error correction. One code word per frame, and it needs a channel of its
  own. [libltc](https://github.com/x42/libltc) is LGPL: a reference to compare with, not code the SDK can include.
- **Data-over-sound libraries** ([ggwave](https://github.com/ggerganov/ggwave), MIT; [Quiet](https://github.com/quiet/quiet), BSD
  with an LGPL dependency; [minimodem](https://github.com/kamalmostafa/minimodem), GPL; [amodem](https://github.com/romanz/amodem),
  MIT): made to carry data, not to mark a moment. ggwave sends 8 to 16 bytes a second.
- **A burst found by cross-correlation:** Android's
  [OboeTester](https://android.googlesource.com/platform/external/oboe/+/HEAD/apps/OboeTester/docs/Usage.md) measures audio latency
  with about a second of random bits in smoothed Manchester code, located by normalised cross-correlation, and finds glitches by
  locking onto a sine. It is the nearest thing to what is wanted here.
- **Continuous spread spectrum**, as GPS signals: a time fix every code period, and quiet enough to sit under other sound, but a
  noise-like signal under louder sound is what lossy codecs remove, and the products that solve that (audio watermarks) are
  patented.

For scale: [ITU-R BT.1359-1](https://www.itu.int/dms_pubrec/itu-r/rec/bt/R-REC-BT.1359-1-199811-I!!PDF-E.pdf) puts the offsets
people notice at sound 45 ms early to 125 ms late, and EBU R37 allows 40 ms early to 60 ms late end to end.

**The option that looks best: a sync burst and a coded payload**, the QR code's shape in sound (a finder pattern, then data):

- a preamble the decoder finds by cross-correlation, to the sample: a pseudo-random sequence or a chirp, somewhere in 1 to 8 kHz;
- then a payload far smaller than the picture's: the frame marker carries 57 bytes (the frame, its animation time, the pacing and
  the CPU's work), and the sound needs none of that again. Which run it is and where the audio clock was at the burst's first
  sample is enough, about a dozen bytes, less than the sync marker's 20; the run id joins it to everything the picture's markers
  say. It has to be that small: sound carries far fewer bits a second than a picture, so every byte makes the burst longer, easier
  to hear and easier to damage;
- a checksum and the Reed-Solomon code the QR encoder already has, over that payload;
- a few bursts a second, and perhaps a steady tone between them, whose breaks would show dropped and repeated buffers;
- played alone: a sync test need not share the output with the application's other sound. The application can mute the rest, or
  play the signal in a test mode, so the signal can be plain and loud, and the hard problem of hiding it under other sound (what
  spread spectrum and watermarks are for) may never have to be solved;
- a format document of its own, as the marker has, and generators written from it in each SDK language: integers only, no
  allocation in the audio callback, no third-party code.

Linear timecode would be the first experiment and the fallback: standard, simple, and existing decoders can check ours.

**Harmless to hear, for people and animals**, as a rule of the design and not an afterthought:

- **It never has to be loud, or heard at all.** What harms hearing is how loud a sound is and for how long, whatever the sound:
  the [WHO](https://www.who.int/news-room/questions-and-answers/item/deafness-and-hearing-loss-safe-listening) gives 80 dB as safe
  for up to 40 hours a week, and less time the louder it gets. The tools read the signal from the recording, which a capture card
  takes from the output itself, so the speakers can be turned down or off. The signal has to work at a low level, and the guide
  would say to measure that way.
- **Nothing ultrasonic or near it.** An adult's hearing falls off sharply from about 15 kHz, so the person setting the volume
  cannot tell how loud a signal up there is, while children still hear it, and so do pets: dogs to about 45 kHz and cats to
  about 79 kHz ([hearing ranges](https://en.wikipedia.org/wiki/Hearing_range)). The signal stays in the band every adult hears
  clearly (the 1 to 8 kHz above), where anyone who finds it too loud notices and turns it down. That rules out the "inaudible"
  modes some data-over-sound libraries offer (ggwave's ultrasound, Quiet's 18.5 to 19.5 kHz).
- **No sudden full-level starts:** bursts fade in and out, so nothing clicks or startles, and a mistake in the level is a loud
  tone, not a bang.

What only measurements can settle:

1. **Lossy codecs and resampling:** whether a burst survives AAC, Opus and MP3 at OBS's bitrates and a 44.1/48 kHz conversion, and
   whether they shift where it is found. No published numbers were found.
2. **The recording's own audio offset:** lossy encoders put priming samples in front of the sound (1024 samples, 21.3 ms at 48
   kHz, from ffmpeg's AAC encoder; 2048 and 2112 from others; 312, 6.5 ms, from Opus) and the file must say so. Where that is lost,
   the sound is late by that much. OBS can record PCM, FLAC or ALAC instead, which is what a measurement should use.
3. **The capture path's own offset:** a capture card delivers picture and sound separately, so an absolute offset needs a source
   known to be in sync to calibrate against. Drift, jitter and lost buffers do not.
4. **Whether it ever has to sit under the application's own sound:** not for a sync test, where the signal plays alone. Only
   measuring during normal play, with the game's sound on, would need it; whether a burst can be found there, and which preamble
   does that best, is open, and may never need an answer.

The work:

1. **An experiment before any format:** generate the candidate signals, put them through the codecs with ffmpeg, and measure how
   exactly each is found.
2. **The format document** and its golden data.
3. **The SDK's generators**, one per language, from the document.
4. **The tools:** read the recording's audio track through ffmpeg, find and decode the bursts, and report the sound against the
   frames.
5. **Validate with real hardware:** an application with both markers through a capture card, recorded by OBS.

## Long runs in less memory

A possible optimization, worth it only if runs of many hours become a real use. A run that is loaded keeps an object per presented
frame (about 320 bytes) and a row per capture (over 200 bytes), and its payload bytes: about 550 MB for an hour at 240 Hz, so ten
hours would need several gigabytes. A report made from an analysis also keeps a second set of capture rows, which only the events
panel reads, and only seven of their values.

The option: keep frames and captures as columns of values (a list of display times, a list of frame indices, and so on) instead
of an object each, and give the events panel the few columns it reads. The charts already work that way on their prepared data;
this would take it back to what the analysis produces and what a report holds.

It touches the analysis, the charts and everything that reads a frame, so it is a redesign, not a pass. What was measured and done
without it: the output files, the analysis and the report cards of an hour take seconds and allocate little beyond what they keep.

## Later

- **Synced playback in the GUI:** click a spike in the Timeline to see the captured frame it came from, inside the GUI. Today
  [the playback page](../measure/doc/usage.md#the-playback-page) does it in a browser: a run's report next to its recording, with a
  playhead on every panel.
- **A C++17 fallback** for the C++ marker library, for toolchains without C++20.
