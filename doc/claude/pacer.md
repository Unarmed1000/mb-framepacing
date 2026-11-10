# Pacer: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **Pacer module (`sdk/cpp/pacer`, `sdk/doc/pacer.md`): EXPERIMENTAL.** The pacer of the redesign of 2026-10 (`TierPacer`), which
  replaced the first one (`FramePacer`, retired on 2026-10-09 with its refresh clock and its present feedback). It is checked
  against its own simulation and, on one machine, against the display times a graphics driver reports (the first integration: the
  user's unofficial gtec-demo-framework, three FramePacing samples, linked from the guide's Status and from `integrating.md`), but
  no capture of it has been analysed with the tools. Off by default (`MB_FRAMEPACING_BUILD_PACER`, Conan's `with_pacer`); build it
  with `-DMB_FRAMEPACING_BUILD_PACER=ON`.
  - **Two documents.** `sdk/doc/pacer.md` is the guide: how to use it, with a Status table (checked / not checked). **`sdk/doc/pacer-design.md`
    is the record of the redesign**: what was wrong, every measurement with its conditions, each decision with what decided it, and
    "What is still missing". A measurement goes into the record as numbers with their conditions; nothing from single runs goes
    into the guide as advice. Keep the guide's Status table and the record's "What is still missing" current with every pacer change.
  - **Off for users, on where we check:** the `windows` and `linux-sanitize` presets, CI's and the release workflow's C++ build and
    tests (Windows, Ubuntu, macOS) build it. `check_consumers.py` runs twice more with it (`subdirectory-pacer`, `package-pacer`) and
    `check_conan.py` once more (the consumer and the Conan test package pace one frame with `TierPacer` and fill a `Payload` from the
    schedule), and the consumer check fails when a default install holds anything of the pacer.
  - **Every place users meet it says "experimental":** the guide's notice and its Status table, the READMEs, `integrating.md`, the
    CMake and Conan option descriptions, the release notes, and the first line of every public type's comment. Keep it that way
    until it has been measured on real swap chains.
  - **The rule it is built to (the user's): the pacer holds every pacing rule and every time calculation; the application supplies
    information and carries out the waits and the present.** Logic about pacing that an application has to write itself is a fault
    of the pacer. Values in, values out: no platform API, no callbacks, no clock reads, no waits inside the library; made once (the
    rule's frame windows), no allocation per frame after that (only `SetSettings` with a larger frame window allocates).
  - **Capabilities and tiers** (`pacer/capability/`): an application says what it has and what of it is active (`PacerCapabilities`;
    the active set is how it controls the pacer). A tier is the capabilities a set needs (`PacerTierUtil::Rate`, usable without a
    pacer). **A tier is a way of placing a frame; a capability combines with every way** (the user's ruling, 2026-10-14,
    decision 19, reversing decision 16). **Three major tiers of four sub tiers, written "3.1"**: 1 = a time on the present
    (`PresentAtTime`, the display places against a clock; built against the simulation only); 2 = a duration on the present
    (`PresentAfterDuration`, or `PresentSwapInterval` up to 2 or more; the display places after the frame before) **with a wait
    for a present or display times** (without either the latency creeps after a late frame: the set falls to 3.x), **paced
    as a tier of its own since decision 20** (2026-10-15, the simulation only; `HasPacer` true and `PacedAs` the identity
    for every tier, kept for applications): the loop holds no present (`DisplayPlacementUtil::HoldsUpTo`); **smoothness
    takes the hit** of a frame shown late (the user: "taking the hit ... as retiming would cause stutter"), **low latency
    with display times and no wait** (2.2, 2.4) starts the frames later by the display's slip (`hold/DisplaySlipRule`,
    `DisplaySlipRefreshes`; never the animation time); still to be measured against 3.1; 3 = the frame loop. Sub tiers from `WaitForPresent` and `VBlankTimes`, **the same rank in
    every major tier** (the user, 2026-10-15, decision 21: a different order per major tier only with proof, and there is a
    measurement for the loop's order only): .1 both, .2 vertical blank times, .3 a wait, .4 neither (major 2: `DisplayTimes`
    with .2 and .4, which have no wait); proposals until measured. **Marks:** "+" display times, **"s"** `PresentSkipsOverdue`
    (`PacerRating::SkipsOverdue`, `PacerTierText::SkipsOverdueMark`: describes the system, a rating only until a pacer relies
    on it), **"c"** a frame callback holds the loop (`PacerRating::HoldsByFrameCallback`, the user's choices of 2026-10-30,
    decision 22: "3.3c"; the plan's wait for a present is that present's callback, `PresentWaitReport::Taken` says it came, no call
    per callback and no event time; on a grid on the clock only, one present nearer than a wait for "shown", unused with
    `WaitForPresent` or `VBlankTimes`, no tier of major tier 2: `PacerTierUtil::FrameCallbackCanHold`). The GPU wait and GPU work times: capabilities, no tier, no mark. `DisplaySideHolds` stays beside the tier. No
    capability that changes how good the pacing is may hide inside a place. `PacerTierText` has the words and `NumberOf(tier)`; an
    application shows no texts of its own. **Every tier has both aims** (`PacerAim::Smoothness`, the default, and `LowLatency`): an
    aim is never built in as a tier's only behaviour, and the pacer does not pick it (the user: not now).
  - **Built from parts** (the user: no restart, four whole pacers taken apart under their tests): `TierPacer` (`pacer/tier/`) holds
    two ways of placing frames, `ClockGridLoopPacer` (a grid on the clock) and `VBlankLoopPacer` (the display's vertical blanks),
    and hands a run over between them (`PacerHandover`) when the active set changes; the parts are `hold/PresentWaitRule`,
    `hold/GpuWaitRule`, `hold/DisplaySlipRule`, `timeline/VBlankTimeline`, `placement/DisplayPlacementUtil`, `display/DisplayErrorCounter`, `tier/FreeRunLoop`,
    `rule/SwapIntervalRule` and `rule/FrameWorkRule`. The four single-tier classes (`TimerPeriodOnlyPacer`, …) are test code
    (`pacer/tests/simulation`): `TierPacer` with its capabilities fixed, kept for their tests.
  - **A frame's calls:** `AddVBlank`, `PlanFrame` (a wait for a present or for the GPU's work, then a start time; planned again
    after each wait), `AddPresentWait` / `AddGpuWait`, `BeginFrame` → `FrameSchedule`, `EndFrame` → `PresentPlan` (a time to wait
    until, `NotBeforeTime`, `MinimumDuration`, `SwapInterval`, `CpuBusy`), `AddPresent`; and where the application has them
    `AddGpuWork`, `AddSystemWait`, `AddDisplayReport`. `EndFrame` comes before the present and before any wait, which would count
    as work. A wait may take a few swap intervals and 50 ms at the least (`MinWaitTimeout`, the user's choice).
  - **Not pacing** (the user, 2026-10-09: an application that stops pacing goes on reporting): `TierPacer::SetPacing(false)`
    makes every plan empty and takes the reports (`tier/FreeRunLoop`, a third part beside the two that place frames); the
    animation step is then the time since the frame before started, and the swap interval rule gets no frame. On again it is a
    handover. It is the state a variable refresh rate needs; nothing of variable refresh is built.
  - **The wait for a present counts the frames in flight** (the user, 2026-10-09): `PacerSettings::PresentWaitDepth`, at least
    one more than `MaxFramesInFlight`. An application sets neither it nor `WaitingPresents`.
  - **No catching up** (the user): after a refresh that was lost the animation step stays the swap interval, and the animation
    time is that refresh behind the clock (`RefreshesBehindClock`); a constant offset does not show, correcting it does. A loss
    that repeats is followed. A start the display's side held is not stepped over (decision 17).
  - **Nothing a pacer uses about a display unless it is certain to be the window's display** (the user: 100 % certain; a
    consistency check is no substitute). The refresh period is the application's to give: a swap chain's reported refresh was the
    fastest display's of the desktop.
  - **Display times are statistics only** (the user, 2026-10-04: "stats only, to simplify"): `AddDisplayReport` →
    `DisplayErrors()` (the animation error by the tools' rules, and the time from a frame's start to its display). Never let them
    change a swap interval or an animation time without the user's word; pacing by them is the guide's "Not used yet". The
    one exception, on the user's word (decision 20): major tier 2 with low latency and no wait moves the frames' start by
    the display's slip, and nothing else.
  - **Fixed refresh rates only** (the user: fixed refresh first, variable refresh once this works, as more tier pacers behind the
    same calls). The test display is a G-SYNC one: ask for its state, and what displays were on at what rates, before reading a
    capture, and what else ran on the machine.
  - **Target frame rate:** `PacerSettings::SetPreferredFrameRate` / `SetPreferredFrameTime` → `PreferredSwapIntervalAt(RefreshPeriod)`
    with the tools' rounding (`FrameTimeRounding.WholeRefreshes`: up, a twentieth of a refresh of slack, at least 1); with
    `PreferredSwapInterval` the slower of the two counts. It is the fastest rate: the rule only goes slower.
    **The frame rates a display can show** (the user, 2026-10-07: for a menu, "nothing lower than 20"): `FrameRateStep` and
    `FrameRateStepUtil` (`StepCount`, `StepAt`, `StepFor`/`StepForRate`, `IsStep`/`IsStepRate`; functions of the refresh period
    alone), judged with the same slack through the settings' own rounding, so the two never disagree.
  - **Typed, in nanoseconds** (the user, 2026-10-07; the marker's payload takes the same types, so a schedule's values go into
    one as they are): points in time `NanosecondTickCount`, spans `NanosecondTimeSpan`, lengths that can not be negative
    `NanosecondTimeDuration`. The pacer caps nothing: what a marker's four bytes do not hold is capped by the payload.
    `RefreshPeriod`'s 2⁻³² nanoseconds are private. **Always valid:** `RefreshPeriod` (100 µs to 1 s, no default) and
    `PacerSettings` (constructed from the period; setters assert, then clamp). `SetRefreshPeriod` with another period and
    `SetSettings` with other settings start the frames again and keep the animation time; the same ones change nothing.
  - **Names:** the rule's stretch of frames is the **frame window** (`FrameWindowLength`, `FrameWindowState`), never "window" alone
    (in graphics that is the window system's). The pacer's animation time is the display's, in refreshes: the application's game
    time is its own (`sdk/doc/marker-fields.md`). The default `FrameMargin` is the smaller of 1 ms and an eighth of the refresh
    period (`PacerSettings::FrameMarginAt`, agreed with the user); a margin that was set stays as set.
  - **How it is checked.** Integer arithmetic only: the golden data must come out byte for byte, and does from MSVC and Clang.
    100 % test coverage as the core and the marker (llvm-cov on a Clang build, `NDEBUG`); a header that is on the disk and not in
    its library's `FILE_SET HEADERS` stops the configure. There is no C# port (the roadmap lists one as a possible upgrade).
    - Test code, built with the tests only and never part of the library: `pacer/tests/simulation` (the sister repository's frame
      model, `PacerSimulation`; a frame loop on a display model that queues presents, `SimulateTierLoop` with `LoopSettings`; the
      golden runs, `TierLoopGolden`) and `pacer-sim` (`--golden`, `--loop`).
    - Golden data: `python tools/update_pacer_test_data.py` writes the scenarios' frames from the test clips and runs
      `pacer-sim --golden`: the scenarios' results (`60-busy`: the full-window rule reproduces the sister repo's swap intervals and
      refreshes; `60-busy-full-rate`: at a fixed swap interval every frame is on the clip's refresh), and the tier loops
      (`tier-loops.csv`: a line per run with the length and CRC-32 of its frames as text; `tier-loop-<run>.csv`: eight runs whole).
      Run it after a change to a rule or to how a frame is paced and review the difference.
    - `TierMonitorRateTests.cpp`: 23 rates from 50 to 540 Hz; `WorkloadLoopTests.cpp`: the loads of another pacer's own tests.
    - **Run the pacer's tests in a build with asserts before a push** (`cmake --build sdk/cpp/build/windows --config Debug`, then
      the test executable in `pacer/Debug`): the `windows` preset's Release and a coverage build have `NDEBUG`, and CI's
      `cpp-analysis` is the only job with asserts. Never with `--gtest_catch_exceptions=0`, which lets an assert open a dialog
      on the desktop.
  - **Capture sessions** of the first integration: a session is one zip, kept as an asset of the GitHub release `pacer-captures`
    (the user's choice: never in git, so no clone carries them; `gh release upload pacer-captures <zip>`), with a row in
    `pacer-captures/README.md`. Git ignores a zip in `pacer-captures/`: download one there to work with it. Before packing, check the
    files for hardware models (the user's graphics card must never be named: vendor and driver version only), user names and
    local directories. **A session's numbers come from its logs through `tools/pacer_capture_report.py`** (standard library;
    reads the zip in place): `<session>/runs.csv`, the charts and the tables of `<session>.md` between `pacer-capture:<name>`
    comments (`--update-doc`); `--check` fails when a file is not what the zip gives. The two sessions of 2026-10-04 are of the
    first pacer and of a sample that did not hold its frame start at a swap interval of one: the guide links them for what they
    say of variable refresh only. Driver display times are not a measurement by the tools: say so. The guide **never embeds a
    chart**: the SDK archive ships `doc/pacer.md` without images.
  - **Wait for more data before drawing conclusions** (the user): three explanations of one finding were written into the guide
    and withdrawn within a day. A finding from one run, or one that another session has not confirmed, goes into the record as a
    number and not into the guide as advice. Try a diagnostic on stored runs before shipping it.
  - **Android's frame pacing library** may be read (the user, 2026-10-09) and its behaviour described; its code name does not go
    into code, comments, documents or commit messages (cite it by its developer page), and none of its code is copied.
