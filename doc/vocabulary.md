# Vocabulary

mb-framepacing uses the vocabulary of [Intel PresentMon](https://github.com/GameTechDev/PresentMon) and the
[Gamers Nexus animation error methodology](https://gamersnexus.net/gpus-gn-extras-cpus/problem-gpu-benchmarks-reality-vs-numbers-animation-error-methodology-white).
What each term means, its other names (engines, platform libraries, Digital Foundry, older GPU reviews), where it comes from and
further reading are in **[mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained)**: its
[vocabulary](https://github.com/Unarmed1000/mb-framepacing-explained/blob/master/doc/vocabulary.md), with diagrams of the two
clocks behind animation error, and pages on
[vsync, VRR and frame rate targets](https://github.com/Unarmed1000/mb-framepacing-explained/blob/master/doc/display-sync.md),
[input latency](https://github.com/Unarmed1000/mb-framepacing-explained/blob/master/doc/input-latency.md),
[charts](https://github.com/Unarmed1000/mb-framepacing-explained/blob/master/doc/charts.md) and
[further reading](https://github.com/Unarmed1000/mb-framepacing-explained/blob/master/doc/further-reading.md).

This page only lists where each term appears in mb-framepacing.

| Term                    | In mb-framepacing                                                                              |
| ----------------------- | ---------------------------------------------------------------------------------------------- |
| **Animation error**     | `animationErrorMs` (CSV), the Analyze page's error charts. Same formula and sign as PresentMon |
| **Animation time**      | Written into the marker by the application (`animationTicks`); `animationMs` (CSV)             |
| **Animation time step** | `animationDeltaMs` (CSV)                                                                       |
| **Display time**        | `displayDeltaMs` (CSV), the display time histogram                                             |
| **On-screen time**      | `onScreenMs` (CSV)                                                                             |
| **Frametime**           | Not measured: the capture sees the display side; the animation time step stands in             |
| **Frame pacing**        | The display time histogram                                                                     |
| **Stutter**             | Large animation errors                                                                         |
| **Hitch**               | A frame with a long display time and a large negative animation error                          |
| **Short / long frame**  | Positive / negative animation error                                                            |
| **Delta time jitter**   | Animation errors while the display time is even                                                |
| **Microstutter**        | Display time spread plus alternating animation errors                                          |
| **Runt frame**          | Torn frames (the tearing check)                                                                |
| **Judder**              | Shows as a display time spread                                                                 |
| **Dropped frame**       | `skippedBefore` (CSV), counted in the report                                                   |
| **Drift**               | `driftMs` (CSV), the Drift chart                                                               |
| **Tearing**             | Torn frames (the tearing check)                                                                |
| **Input lag**           | Not measured                                                                                   |
