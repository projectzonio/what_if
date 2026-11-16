# Intake: ChatGPT planet research (EXOLUNE phases 1.0 and 1.5, SFSIM phase 2)

- **Batch:** 3 packages, 49 files, 149 kB, produced 2026-11-06 by external workers (ChatGPT, run by the user, ADR-036). The files are in `raw/` unchanged; checksums are in `SHA256SUMS`. Source: `C:\Users\Lukas\Downloads\planet_research`.
- **Purpose:** research for **What If** (planet-scale world, separate project). It is not input for NightRacer tickets. NightRacer's WorldGen/Streaming libraries remain the base that a future world kernel builds on (memory `what-if-vision`).
- **Intake by:** orchestrator (Claude Opus 5.5), 2026-11-09. Spot-check per `research-rules.md` §8 and §9: 8 primary pages fetched, all HTTP 200.

## Verdict: usable as class E (leads plus architecture reasoning); locators are not usable
- **Substance is confirmed.** Every fact checked against the primary source matched: licences, versions, the FP64 gate, NOTICE components, FAQ figures and the FAQ licence statements. No invented source.
- **The packages are honest about their limits.** They use IMPLEMENTED / DOCUMENTED / INFERRED / UNKNOWN tags, say plainly that no clone, build or benchmark was done, and contain no invented measurement. This is better discipline than the earlier GPT drafts (What If concept, BeamNG claim).
- **SFSIM line numbers are invalid.** They are offsets in the rendered GitHub page, not file lines. Examples: `deps.edn` "278–338" in a 54-line file; the `globe.clj` header cited at "360–368" is really on lines 1–5 of a 94-line file. Treat every SFSIM `file:line` as a pointer to the file only.
- **EXOLUNE line numbers drift.** The FP64 gate is cited at `D3DApp.cpp` 520–524 and is now at line 567. The links point to mutable `main`, not a pinned SHA.
- **The SFSIM snapshot is stale.** The package says latest version 0.34-3 (2026-11-01). The ChangeLog now has 0.38-2 (2026-11-08): four releases in a week. Re-check anything version-bound.
- **The framing of our world is wrong in both packages.** They speak of "persistent 10×10 km authored gameplay tiles". WorldGen generates the world from rules and seeds. Its streaming units are `SectorStreamer` sectors, and its terrain tiles are 65×65 vertices at 4 m. The 10 km figure is the test spec `terrain-10km.json`. The architecture advice (global render cache vs authoritative local world) still holds if "authored tile" is read as "WorldGen sector".

## Spot-check table (V = verified 2026-11-09 by fetching the raw primary file)
| # | Claim in the package | Primary source result | Status |
|---|---|---|---|
| 1 | Exolune licence: PolyForm Shield 1.0.0, competitors excluded even when free | `LICENSE.md`: "# PolyForm Shield License 1.0.0"; "Goods and services compete even when provided free of charge." | V |
| 2 | `D3DApp.cpp` throws without `DoublePrecisionFloatShaderOps` | Present, `throw std::runtime_error("This GPU has no double precision shader support, needed by the terrain noise.")` at line 567, not 520–524 | V content, locator drifted |
| 3 | NOTICE: web-ocean, Wicked Engine, PCG3D, TileableVolumeNoise (MIT), Poly Haven CC0, CC BY trees | All present; NOTICE also lists audio credits (CC BY 3.0 / CC0) that the package omits | V (incomplete list) |
| 4 | SFSIM licence EPL-1.0 | `LICENSE` line 1: "Eclipse Public License - v 1.0" | V |
| 5 | Clojure 1.12.6, LWJGL 3.4.3 | `deps.edn`: both strings exact | V content; cited lines 278–338 do not exist (54 lines) |
| 6 | `globe.clj` dual-licence header | Lines 1–5: "SPDX-License-Identifier: LGPL-3.0-or-later OR EPL-1.0+", then "licensed under the Eclipse Public License v1.0" | V content; cited lines 360–368 wrong |
| 7 | Latest SFSIM 0.34-3, 2026-11-01 | ChangeLog top: 0.38-2, 8 Nov 2026 | Stale |
| 8 | FAQ: Steam Deck 25 fps, RX 6700 XT / RX 7600 50 fps, clouds main bottleneck, study + reimplementation need no licence | All verbatim on `wedesoft.github.io/sfsim/faq/`; the FAQ also says Steam Deck compatibility "has not been tested recently" | V |

## What to take into What If (class E until a What If ticket re-derives it from a pinned commit)
1. **Clean-room only for Exolune.** PolyForm Shield forbids "any product that competes", free ones included. A planet-scale game is close to that category. Read for ideas; never copy or adapt code. Its upstream MIT parts (web-ocean, Wicked Engine clouds, PCG3D, TileableVolumeNoise) come from their own upstream repos under MIT, with notices, never from Exolune's modified files.
2. **Exolune does not fit the target hardware.** It requires GPU FP64 shader ops (hard throw), and its published reference is an RTX 4070 Ti at about 46–57 ms/frame. Not usable on Intel UHD as it is (O: not measured here).
3. **SFSIM is an open study source.** EPL-1.0, and the FAQ explicitly allows study and independent reimplementation. Valuable patterns: six-face cube-sphere with quadtree LOD decided off the render thread; tile-centre-relative float vertices with double precision on the CPU; offline raster → cube-face pyramid pipeline; separate surface vs orbit physics domains.
4. **Shared architecture advice, which matches our own direction:**
   - double-precision authority on the CPU, camera- or tile-relative floats on the GPU;
   - the global planet layer is a render/cache layer, while the local world (WorldGen sectors) stays authoritative;
   - WGS84 instead of a spherical Earth when real geography is used;
   - clouds and volumetrics as optional tiers with a UHD fallback.
5. **Questions for a future What If ADR** (from both packages):
   - frame transport at the poles;
   - the handoff planet patch → region → WorldGen sector;
   - a precision strategy for procedural noise without GPU FP64;
   - VRAM and streaming budgets on the i3-1215U.

## Next external brief (if the user continues this line)
Ask for `git clone`-level evidence:
- pinned commit SHAs, and line numbers from raw files (`raw.githubusercontent.com/<owner>/<repo>/<sha>/...`), never from rendered pages;
- the UNKNOWN items listed in `EXOLUNE_WORKER01_PHASE1_5/09_UNRESOLVED_QUESTIONS.md` and `SFSIM_WORKER02_PHASE2/13_OPEN_QUESTIONS.md`.
