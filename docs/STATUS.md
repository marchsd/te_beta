# Status and resume guide

## Milestones
| # | Scope | State |
|---|-------|-------|
| 1 | Window detection, geometry, transform, tracker, mock, debug map, tests | DONE (Linux-verified; needs one run on real Windows) |
| 2 | Research + decision | DONE for the decision (Option A, see ARCHITECTURE.md); transparency/render feasibility still UNVERIFIED, needs a spike on real Windows |
| 3 | World model: stable-id platforms, occlusion, tombstones, mined-tile overlay (+tests) | TODO |
| 4 | Versioned named-pipe protocol + heartbeats/reconnect (+tests) | TODO |
| 5 | Minimal playable prototype (player physics on geometry, W/S in interiors, mining) possibly with a stand-in renderer | TODO |
| 6 | Transparent click-through overlay, gameplay toggle, input ownership, Esc | TODO |
| 7 | Terraria mod integration: real player/enemies/spawning | TODO |

## First thing to do on resume
1. On Windows run `scripts\list.ps1` and `scripts\watch.ps1`; fix any Win32 filtering/rect issues found.
2. Start Milestone 2 research (this was NOT done: no framework claims are verified).

## Sandbox note
NuGet was unreachable in the authoring sandbox; builds used `-p:RestoreSources=<empty dir>`.

## Update (checkpoint 2)
M3 (world model), M4 (protocol + pipe bridge), M5 (headless sim) are DONE and tested on Linux (65 tests).
Overlay (M6) and Terraria mod (M7) are NOT done yet at this checkpoint.

## Update (checkpoint 3: M2 decision)
Chose TerrariaModder (1.4.5.8, net48, 32-bit XNA/D3D9) with a "tile mirror" design; tModLoader is still on 1.4.4.
Nothing was run against a real Terraria. Next: write a minimal net48 mod spike that (1) writes mirrored tiles,
(2) tries the overlay transparency options in ARCHITECTURE.md, and report which works. The sandbox has no Windows or
Terraria, so that spike must be run by you; build it here, test on your machine.

## Update (checkpoint 4)
`scripts\list.ps1` verified on real Windows 11 (7 windows, correct work-area and DPI 96). OverlaySpike mod written
(terraria-mod/OverlaySpike); compile-checked only against stub types, NOT against real Terraria assemblies, and not
run. Next: user runs docs/SPIKE_RESULTS.md and reports.
