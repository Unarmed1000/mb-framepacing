# Analysis and charts: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **Refresh rate and pacing:**
  - A capture card captures at the display's native refresh rate: that is a fundamental assumption, not a setting. The analysis
    takes the capture period as the refresh period; display time steps are whole refreshes. `SyntheticCaptureSource` refuses a capture rate that differs
    from the refresh; `selftest --fps N` simulates an N Hz display.
  - **Charts** live in `MB.FramePacing.Charts` (no GUI): the report cards as shapes (below). The GUI draws them itself with `CardView`
    (`FramePacing.Gui`, the style from `CardStyle`, which parses the SVG's style sheet; hover texts from `CardHover`); the Timeline card
    shows five panels only (`AnalysisViewModel`'s `g_timelineItems`: animation error, display time step, frametime, late share,
    refresh strip; not the events panel or the reference lines, which the report card has), zooms (wheel), pans (drag) and resets
    (double-click), the distribution tabs follow its section, and **Save view** writes the card
    on screen as SVG or PNG (`CardImage`). DocImages checks the width, zoom, the sliding window (sideways scroll, scrollbar and drag
    move the same card; near its edge the next one is built), reset and hover with headless input.
    - **The sliding window:** zoomed, the GUI's Timeline is built for the view plus a screen either side (`TimelineWindow`;
      `ReportCard.Build(..., visible)`): what moves with time goes into `ScrollShape` layers that `CardView` clips to the plots and moves
      by `ScrollOffset`, so scrolling builds nothing and only the distribution cards follow (`SectionCards.Follow`). A window's start is
      snapped to a whole pixel column, so windows at one zoom draw the same columns and swap without a visible change. Files never
      contain a window (`SvgCardWriter` writes the layers' shapes in place), and **Save view** builds the card of exactly the view.
    - **Long runs cost per pixel column, not per frame:** `RunChartData` (cached per `ChartRun`, immutable, built on first use) holds
      each kind of value as a `FrameSequence` (`RankBits`: which frames have one; a `WaveletMatrix`: exact k-th smallest and counts below a
      bound for any range in about 20 steps) and the late share (`LateShareData`). A `RunSection` is a frame range found by binary search;
      its statistics are computed only when a tile needs them. The panels walk the pixel columns (`PixelColumns.Walk`) and query min, max,
      median, percentiles and histograms (`SectionHistograms`) exactly: the output is byte-identical to sorting. The GUI builds cards
      at the window's width (`width`), with the whole run's scales (`wholeRunScales`), in the background (`SectionCards`,
      `LatestRequest`: a newer zoom cancels an older build). `CardBenchmarks` (`--long-running`) measures 1 and 10 hours at 240 Hz: a few ms per zoom or
      window. **Preparing the data** is the cost of a run's first card (an hour at 240 Hz: about 0.14 s and 145 MB allocated to keep
      25 MB; `CardBenchmarks.FirstReportCard`): a `WaveletMatrix` collects and sorts only the different values while there are few
      (`MaxCollectedDistinct`; else all of them, `NanosecondSort` with a scratch array of its own, since a rented one would stay in the pool
      while the panels prepare at once); a run without a static frame prepares each `Animating…` sequence as its unfiltered twin, the
      same object; sequences over the same frames share their `RankBits` (`Errors`, `AbsoluteErrors`); the holds of a kind are
      prepared when a panel asks for that kind (never the holds as planned). `--charts` and "Save
      charts" write the SVG cards (`ChartFiles`: the report and every `DistributionCard`). Test chart changes with
      `ChartVideoClipTests`: every series of every chart is compared exactly with the test clips' manifests (`ClipManifest`, shared
      with `VideoClipTests` by linked source files).
    - **The report card's header and notes** (the user, 2026-10-23, approved OLD vs NEW): under the title only the line that says
      what the card shows and `ReportCard.NotesHint`; the description's other lines are the NOTES at the end of the card. When the
      display and the capture disagree (`RunHeadline.RefreshWarning`, the analysis's rule from `RefreshAgreement`: the markers'
      refresh period or `--display-hz` more than 1 % from the refresh measured with) an amber band with a sign and words goes under
      the header (`warn-band`, item `warning`): amber, not red or yellow, which are verdict colours on the panels (explained's advice).
      The DISPLAY box shows the rate in the warning colour and "markers N Hz". The timeline and distribution cards keep their
      description under the title: explained's diagram slides rely on it.
    - The report card (`ReportCard`, `mb-framepacing render`) is a port of mb-framepacing-explained's `generate_charts.py`: its style sheet
      and `text()`/`ms()` helpers are verbatim in `SvgMarkup` (Python's half-to-even rounding included; `ReportSvgTests` pins their
      output). It draws from the analysis output (`AnalysisOutput` reads `summary.json` and the frames CSV back to the nanosecond), any section
      (`RunSection`); more frames than pixels draw per column. PNG goes through a headless Edge/Chrome (`HeadlessBrowser`, `MB_BROWSER`):
      a run that fails gets one more (CI's browsers abort or hang now and then), Linux drops the sandbox when the browser says it has
      none, and an image already at the target is deleted first (the browser is ended as soon as the file is whole).
    - `ReportOptions` picks the items (`ReportItem` ids: `--hide`/`--only`); overlays (`ReportItem.Overlays`, now the animation time
      step on the display time step panel) are opt-in (`Show`, `--show`) and need their panel. The tiles frames dropped and out of order
      (`ReportItem.AutoTiles`) show only when the run or section has either (`RunHeadline.Shown`, which the GUI uses too), unless shown
      on purpose (`Show`); the tiles go in two rows, half of them each (`TilesPerRowFor`: 8 → 4, 10 → 5). The layout options are for a card in a
      document (explained's charts page): `Title`, `StripSeconds` (the strip over the first seconds, its own axis), `HideEmpty` (tiles
      without a value, `HeadlineTile.HasValue`, and the late share without late frames), `TilesPerRow`. The defaults are the standard
      card: `--charts`, Save charts, the GUI and DocImages never set them. `ClampStatic` (on by default) is the one the GUI switches:
      its "Clamp static" check box, which Save view and Save charts follow (`ChartFiles.Write(report, options)`).
    - Cards are shapes (`CardDrawing`: `RectShape`, `LineShape`, `PathShape`, `TextShape`, `GroupShape`, plus `CardPlot` per plot
      area with its data range, for the GUI's zoom and hover and for tests that read values back); `SvgCardWriter` writes them.
      `DistributionCard` builds the error and display time step histograms, the error percentiles and the drift (`render --cards`).
    - The frame timeline (`FrameTimelineCard`, `render --timeline`, at most 40 frames) is explained's timing diagram from the data: CPU
      boxes (CPU start time + CPU busy, overlapping ones in further lanes) placed on the capture's clock by `PacerToCapture` (display time
      - intended display time - lateness; without a schedule, no frame presented after it appears), present arrows, display cells. A
        static frame's cells are violet (`strip-static-a`/`-b`) and the error row says "static" on the static step after it; a frame is
        "held longer" (amber) for the refreshes beyond the time a late next frame was animated for: the `Late` flag, never a test of
        the card's own. A frame's first refresh is coloured as the report's refresh strip (red only
        for `Late`, yellow for `DeltaTimeJitter`, green otherwise) and its error pill as the error bars (red beyond the threshold,
        yellow for jitter): the user's rule that our charts follow explained's fixed look with our colour rules (2026-10-15, after
        asking the explained session how its diagrams draw). Held refreshes and bright vsyncs follow the
        late verdict's intended period (`IntendedPeriods.Of`, shared with `PacingAnalyzer`: the marker's target frame time first):
        amber beyond the late next frame's period, as the diagrams mean it. A card for a document (`FrameTimelineOptions`,
        `render --timeline` `--title` with `--no-title-range` (also the report card's), `--description` per line in place of the
        card's own two, `--frame-labels A,B,...` one per frame, `--cpu-row-label`/`--cpu-row-sublabel`/`--cpu-key`, `--refresh-pixels 120` the
        diagrams' scale, `--no-threshold-in-key` for a card analysed with `--error-threshold-ms 0.001`), and its key wraps on a
        narrow card.
    - The headline numbers come from `RunHeadline` only: the GUI's tiles and the report card's show the same tiles, and the GUI's
      Display card the report's display box (`RunHeadline.Display`, `DisplaySummary`), so add or change a number there, not in the GUI.
      A tile without a number shows "-" with `HasValue` false.
    - The animation error panel draws a refresh line (`error-refresh`, dashed amber, labelled "+1 refresh (16.7 ms)") at every whole
      refresh an error reaches within a tenth of a refresh, over the frames the scale covers, and only inside the scale
      (`ChartScale.ErrorRefreshTicks`, at most four per side).
    - The error and display time step charts cover every value unless a few are more than 8 times beyond the 99th percentile (a hitch);
      those get a mark with their value at the edge (`ChartScale` decides the scales, `ReportCard.ClipMarks` draws the marks).
    - **Keys:** every panel's title says what it shows; its key (`ReportCard.Key`, right aligned) names each colour with one swatch per
      entry and lists only what the section shows. A key, a tile's value with its detail and the display box's rate line are one
      `TextRunsShape` (pieces of text, each in its own classes) that the renderer lays out end to end (a `<text>` with `<tspan>`s in SVG,
      measured pieces in `CardView`), so no width is guessed. Swatches are coloured characters in the `key-*` classes (■ fill, ━ line,
      ┅ refresh and reference lines, ▐▌ for colours that alternate per frame).
    - **Holds and strip cells by kind** (`RunChartData.HoldKinds`, `HoldKind`): unknown (a capture gap, dashed grey), an older frame
      came back (out of order, pink; it outranks late, as the frame after it is late by its step and the older frame is why), late
      (red), frames dropped by the target (orange), as planned (green). Dropped = skipped frame
      indices over a gap-free capture that never came back out of order (`DroppedBeforeFrame`); the strip colours the refreshes where
      they were due orange and the out-of-order refreshes (`PresentedFrame.OlderFrames`, CSV `olderFrames`) pink (anywhere in a frame's cells).
    - **Reference lines** (`frame-time-lines`, shown by default): the target (light grey) and preferred frame time (amber, only where it
      differs) as dashed stepped lines on the display time step panel (whole refreshes, `FrameTimeRounding.WholeRefreshes`, the
      analysis's rule) and the frametime panel (as written). Per hold, the values of the frame that ends it (`FrameReference`: the
      marker's, else the analysis's fallbacks, without the schedule's step); `RunChartData.StepReferences`
      / `FrameTimeReferences` are stretches of equal values, and the scales take the animating frames' values (static ones stay out).
    - **Events panel** (`events`, under the strip, at every zoom): two lanes never mixed up, **frames** (dropped by the target, out of
      order, torn) and **capture** (not recorded, dropped by the source, missed, not decoded), one mark per pixel column and lane, the
      first kind of `RunEvents.FrameKinds` / `CaptureKinds` that the column has. `RunEvents` (`RunChartData.Events`) holds each kind's
      events sorted with running totals; the capture's come from `ChartRun.Captures` (the capture rows as captures.csv has them: live from
      the report, `ChartRun.CapturesOf` once per capture; from files, `AnalysisOutput` reads captures.csv), so the report from the files
      is the live one (`ChartVideoClipTests`). The key counts each kind in the section, the description names the capture gaps by kind,
      and `CardHover` lists a column's events with the frames they name (on a card that has the panel: the GUI's Timeline has not).
      The strip has no marks.
    - **Static stretches:** a violet band (`static-band`) behind every time panel from the first static frame's display time to the next
      frame's (`RunChartData.StaticStretches`), violet strip cells (`strip-static-a`/`-b`). With `ReportOptions.ClampStatic` (the
      default; `render --no-static-clamp`, the GUI's "Clamp static" check box, `GuiSettings.ClampStatic`) the display time step and
      frametime scales leave static frames' values out: holds, the animation time step overlay, frametimes, CPU busy and the reference
      lines of holds ending at a static frame (`RunChartData.AnimatingHolds`, `AnimatingAnimationHolds`, `AnimatingFrameTimes`,
      `AnimatingCpuBusy`, `FrameTimesAndCpuBusy`, `AnimatingStepReferences`); they get edge marks with their value. Off, the `All…`
      and unfiltered sequences set the scales.
  - **The report is the same for every capture source** (it only pairs decoded markers with display times): histograms in fixed
    0.1 ms bins (`Histogram.DefaultBinWidth`), one error threshold (1 ms, `analyze --error-threshold-ms`,
    `TimelineOptions.ErrorThreshold`), and a display time step is off its target from half a refresh on. A source's precision (a
    camera's period) goes into warnings, never into the binning or the thresholds.
  - Camera captures film faster and calculate the refresh from the frames (`Capture/source/Camera/RefreshEstimator.cs`:
    `EstimatePeriodNanoseconds` finds which period from the intervals, `GridPeriodNanoseconds` asks the first-seen times which grid of refreshes
    they are on (a search of the periods with `GridFit`, the periodogram of the times; the intervals mislead a camera that sees a
    refresh in two or three frames, and the times are on no grid at exactly twice the refresh rate with half of the sightings late:
    the analysis then warns that the rate is unreliable), `RefinePeriodNanoseconds` measures it with a line through every
    first-seen time, the maximum likelihood estimate once the refresh numbers are known; also used
    by the calibration). After a change to it run `RefreshGridTests` and `selftest --experimental --camera --fps <2 x refresh> --refresh <rate>`
    for a few monitor rates. The user's expected display rate (`--display-hz`, capture.json `expectedRefreshHz`) settles an ambiguous
    estimate and is compared with the calculated rate (a capture card: with its capture rate); more than 1 %
    (`TimelineAnalyzer.RefreshTolerance`) is a warning.
  - **Capture gaps** (a capture card's captures not decoded, not recorded, dropped by the source (`CaptureRow.SourceDrops`, a count), or
    missed without a word: `MissedCaptures`, a step of 1.5 periods or more on the device clock, never the host clock): the next
    frame's first sighting is uncertain (`UncertainStart`), so the steps into and out of it are not judged (`UncertainStep`: no animation
    error, no late verdict, not in the frame rates; `RunStatistics.UncertainSteps`), and frame indices skipped across the gap are never
    called dropped. A camera decides uncertain starts itself. Out-of-order captures are kept with the newest frame
    (`PresentedFrame.OlderFrames`).
  - **A long capture's analysis sorts nanosecond counts, not doubles, and copies no rows:** the statistics gather each kind of value as
    whole nanoseconds (`NanosecondList`), sort it once with a radix sort (`NanosecondSort`) and take every number from that (`Statistics.FromSortedNanoseconds`,
    `RunStatistics.From`: to the bit what the formulas over sorted milliseconds give, which `StatisticsTests` keeps as the reference);
    never `OrderBy` or a sorted array per statistic. A run's rows are stretches of the capture's row list (`RowRanges`), not a copy (a
    row is over 200 bytes). The nanosecond lists own their arrays: rented ones would stay in the pool after the analysis.
    `AnalysisBenchmarks` (`--long-running`): the timeline of an hour at 240 Hz takes 0.35 s. Each frame is still made twice (the
    pacing pass copies it to add its values): measured as about 0.05 s and 269 MB of short-lived garbage, left as it is.
  - Late frames, the 2 s late share and the "which cause" verdict: `PacingAnalyzer` → `RunPacing` (`runs[].pacing` in
    summary.json).
    - **Late is one rule** (the user, 2026-10-14, option 1: "I never said refresh period I said INTENDED period"): a frame is late
      when its display time step is longer than both its animation time step and its **intended period**, each by half a refresh
      or more (`AnimationError <= -half` and `display >= period + half`). Half a refresh is the tolerance only. The intended
      period (`PacingAnalyzer`, `IntendedPeriods`) is the frame's target; on demand there is none and the frame is never late.
      **The `1 + dropped` of 2026-10-14 is gone** (the user, 2026-11-02): a drop stretched the target, which excused it. **Never a
      frame's position** against a clock or a plan: a pacer's animation timer and the display diverge for good after a lost
      refresh, and only the frame whose step was long is late. `Lateness` (with a schedule: how long after its intended time a
      frame appeared) is that position: a value in the CSV and the hover that no flag or colour follows from. A static or an
      uncertain step has no late frame. On the 22 clips before the stress clips: 231 late frames, `60-naive-5ms` none (it had 64
      by the rule of 2026-10-13, which only compared the two steps).
    - **What counts as an animation error** (`AnimationErrorRule`; the user, 2026-11-02: "a pacer that works properly can only
      target a FIXED number of refresh period intervals"): `animationErrorNs` stays PresentMon's sum (checked in its source: a dropped
      frame has no value, the next frame is measured from the last shown one), but the sum cancels a stop against a jump: a, a, c is
      2 - 2 = 0. So a frame counts when the sum or either part against its target is beyond the threshold (a part by more than
      `RoundingNanoseconds`, 1 µs: a target of 16 666 667 ns against a display step of 16 666 666 ns put a step of exactly the threshold 1 ns
      past it, 15 frames of the clips with no drop at all, and every real part on the clips is over 100 µs past): **stop or rush** (target
      - display step, `pacingErrorNs` with its sign turned) and **step** (animation step - target, `predictionErrorNs`). **There is no
        grid and nothing is re-aligned:** the target is a length and each frame is measured from where the last frame appeared, so a
        pacer that re-aligns after a loss and one that returns to its grid are both read as shown; a fixed grid would call every frame
        after a re-align off. `framesWithAnimationError`, the cause and the error tiles (p99, p99.9, worst) use the parts;
        `AnimationErrorMs`, error per frame and percent error stay the sum. On demand, static steps and runs with only the one-refresh
        default keep the sum. The panel draws a stop as a bar of its own on the refresh the frame should have changed
        (`RunChartData.Stops`, `ErrorsOnArrival`). **`StressClipTests` holds a hand-written table per scenario** of 11 stress clips:
        never fill it in from what the tools print.
    - **A target the display can not show** (`InvalidTargetCheck`; the user, 2026-11-02): a marker's target frame time (else the
      plan's step per frame index, never the preferred frame time) that is not a whole number of refreshes, or under one. Judged by
      the real refreshes, as `FrameTimeRounding.WholeRefreshes` rounds it, with no allowance for its cadence; a warning at the top
      of the report and a band behind the panels (`invalid-band`). **Frames out of order** get the same (`OutOfOrderCheck`,
      `older-band`; the text says what was seen and names the compositor and the capture chain, as either can reorder frames). The
      warning band holds the bands' key. The target's band is grey and hatched, as cyan is torn on the same card and no colour alone
      tells the bands apart for every reader (explained's advice, the user's choice); the card says "not whole refreshes", never
      "invalid", as a target within the rounding's slack is legal. A key that does not fit beside its panel's title stays where it is
      and the title goes a line up (the user: the legend below the title, "two lines is fine").
    - **The frame timeline** follows (`render --timeline`; for explained's slides): cells for a frame never shown (`strip-dropped`,
      `strip-failed`, named in the cell) and an older frame shown again (`strip-older`, drawn as that frame), the two bands, rows for
      stop or rush and step where the sum hides anything or with `--parts`, and `--compact` (no CPU rows, no key).
    - The pacer's `DisplayErrorCounter` counts by the same rule (the user's go, 2026-10-14): `TierPacer` gives it each frame's
      intended period in refreshes (the swap interval while pacing, the preferred swap interval while not; 0 = on demand), and
      since 2026-11-02 (the user: "fix all three") it judges in two parts and from the last frame shown, across frames reported as never
      shown, never across a frame without a report (`PartRounding`, the tools' 1 µs). One golden run moved by one frame, the pause
      after start-up. While not pacing the target is the preferred frame time, so a loop steadily slower than that has every frame
      counted, as the tools count the same marker.
    - **Delta time jitter** (`PresentedFrameFlags.DeltaTimeJitter`, `DeltaTimeJitter` in the frames CSV, the vocabulary's term: never bare "jitter", which reads as an uneven display, the user, 2026-10-14: "jitter animation timer off by less than a intended refresh
      period"): an error frame that the cause split (`PacingAnalyzer.Split`) counts as delta time jitter, off by less than its
      intended period, and not late. Yellow (`#e3b341`) on the error bars (`bar-jitter`; a pixel column of many frames when every
      error frame in it has jitter: `RunChartData.DeltaTimeJitter`, `OtherErrors`) and the refresh strip (`strip-jitter`, after late,
      before static); nowhere else. `ClipManifest.IsDeltaTimeJitter` mirrors it for the clip tests.
    - The pacer's frame window count (`FrameWindowState::LateFrames`) is another thing, "did not make its time": open as decision
      18 of `sdk/doc/pacer-design.md`.
    - Every frame's **target** (what the display time steps, the cause and the late share's amber are measured against; not late)
      comes from, in order: the pacer's target frame time in the markers, the plan's step per frame index (`PacingSource.Schedule`;
      on demand the step to the frame's intended time as it is), the preferred frame time, the target frame rate (`--target-fps`, GUI
      "Target frame rate", stored in capture.json, overridable at analysis time), and otherwise one refresh (the native rate). A drop
      never changes it. The synthetic game writes a schedule. **The run's target** (`RunPacing.TargetFrameTime`,
      `pacing.targetFrameNs`, "measured against a ... target") is the median of what the frames aimed at, and
      the DISPLAY box's "target N refreshes" is `FrameReference.Target`'s.
    - **Vsync probably off** (the user, 2026-10-28, skips only, not tearing): `VsyncOffCheck`, at least 90 % of the judged frames
      (and 30 of them) with frames dropped before them: a run warning and a line in the card's band (`RunHeadline.Warning`,
      `RunChartData.VsyncOff`, the run's frames on any section). The skipped frames keep their "dropped by the target" labelling.
- **The frame stages charts: EXPERIMENTAL** (`frame-stages`, `measure/libs/MB.FramePacing.Charts/source/Stages`; the user,
  2026-10-09: the script that drew them converted to C#). From a frame log of the pacer's first integration (its sample's own
  CSV, alone or in a capture session's zip; `pacer-sim --loop` writes the same columns): a row per frame or a lane per resource,
  and the whole run as a Chrome trace event file. The log's times stay whole ticks of 100 ns, as recorded: it is the sample's
  file, not ours.
  - A port of `tools/frame_stages_chart.py`, which is gone. `measure/test-data/frame-logs/expected` holds what the script gave
    for the test logs (made up or simulated: **no log of a capture session is stored**), and `FrameStagesTests` holds the port
    to it: the trace file byte for byte, the charts shape for shape. On the two sessions of 2026-10-04 (601 runs) the two gave
    the same trace files and the same shapes.
  - A shape may have a tip (the SVG's title) and a drawing its own style rules and fill patterns (`CardDrawing.Style`,
    `Definitions`), for this card; the GUI does not draw it.
  - Display times in a log are the graphics driver's report, not a measurement by the tools: the chart says so, and a frame
    reported without a display time is never called not shown.
