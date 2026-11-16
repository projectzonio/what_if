# AL-01 — Source and License Research

- **Research date:** 2026-11-17
- **Branch:** `agent/asset-library/foundation`
- **Scope:** source-policy audit and representative individual discovery candidates for a public Unity game.
- **Decision rule:** CC0 1.0 Universal only. This report does not approve downloaded files. No candidate below has been downloaded, archive-inspected, hashed, or Unity-tested in this research pass.

## Executive result

The strongest acquisition sources for the current project are Poly Haven and ambientCG for PBR surfaces, and Kenney for supplementary game-ready models. OpenGameArt is a mixed-license discovery site and must be filtered item by item. Smithsonian 3D is useful for selected cultural-heritage and aerospace models, but its own Open Access guidance says only items marked CC0 are open access; other items remain subject to usage conditions.

The eight individual items below are **candidate leads**, not approved registry records. Publisher policy and item-page license declarations are documented, but the binary-level requirements in `policy/LICENSING.md` remain outstanding.

## Source audit

| Source | Official discovery / policy | License scope established by source | Acquisition and automation | AL-01 disposition |
|---|---|---|---|---|
| Poly Haven | https://polyhaven.com/ ; https://polyhaven.com/license | Poly Haven states that its HDRIs, textures and 3D models are CC0 and may be redistributed, including in commercial products. The CC0 asset license is distinct from website terms and live API terms. | Public API: https://polyhaven.com/our-api . API supports asset listing, metadata, download URLs, file sizes, hashes and dependency file lists. API is free for commercial use; a product built on the live API must visibly credit Poly Haven and send a unique User-Agent. Use API, not unapproved site scraping. | **Approved discovery source.** Individual item and exact downloaded variant still need intake evidence. |
| ambientCG | https://ambientcg.com/ ; https://docs.ambientcg.com/license/ | Official documentation says all ambientCG assets are CC0 1.0 Universal, including downloadable files and material preview renders; raw files may be included in games and commercial projects. | Official API v3: https://docs.ambientcg.com/api/v3/ . GET endpoints expose asset, category, collection and RSS metadata. API v2 is no longer updated; use v3 for new tooling. | **Approved discovery source.** Individual item, exact variant and archive contents still need intake evidence. |
| Kenney | https://kenney.nl/assets ; https://kenney.nl/support | Kenney support states that game assets on asset pages are public-domain licensed (CC0); it directs users to the included license file or item page for details. | Individual pack pages and downloadable ZIPs; no general public catalog API verified in this pass. | **Approved discovery source with pack-level verification.** Check exact item page and included LICENSE file. |
| OpenGameArt | https://opengameart.org/ | Mixed licenses. The exact item page must state CC0; category or collection membership is not sufficient. | Item pages expose license and download links; archive contents still require inspection. | **Restricted discovery only.** |
| Smithsonian 3D | https://3d.si.edu/ ; https://www.si.edu/openaccess/faq | Smithsonian says to look for the CC0 icon; items not designated CC0 are subject to usage conditions. 3D download formats documented by Smithsonian include glTF, GLB and OBJ. Individual item rights must be checked. | Individual object pages expose model downloads and metadata; no reliable bulk-download workflow was verified in this pass. | **Restricted discovery only.** Accept only item-level CC0 evidence and inspect model package contents. |

## Individually inspected candidates

All entries below remain `candidate`. Exact format/resolution notes are limited to what the publisher page exposes; no downloadable archive was inspected and no checksum exists yet.

| Candidate ID | Item | Category | Item-level evidence and technical notes | Remaining gate |
|---|---|---|---|---|
| AL01-PH-001 | [Rock Ground](https://polyhaven.com/a/rock_ground) | surface_material / rocky ground | Item page identifies CC0 and exposes 1K, 2K, 4K and 8K choices; Blend, glTF, MaterialX and ZIP options; AO, ARM, diffuse, displacement, normal and roughness maps. 2K is available for the project baseline. | Select exact 2K package, download, inspect files, hash every retained file; verify tile seams and map conventions. |
| AL01-PH-002 | [Asphalt 01](https://polyhaven.com/a/asphalt_01) | surface_material / asphalt | Item page identifies CC0 and exposes 1K–8K choices; Blend, glTF, MaterialX and ZIP options; AO, packed AO/roughness/metal, diffuse, displacement, DirectX/OpenGL normal and roughness maps. | Select exact 2K package, download, inspect files, hash every retained file; verify tiling and physical scale. |
| AL01-PH-003 | [Fir Tree 01](https://polyhaven.com/a/fir_tree_01) | vegetation / conifer | Item page identifies CC0. Page reports approximately 8 million triangles and 956.57 MB download payload; detailed bark, trunk and twig maps are exposed. | Do not treat as runtime-ready. Inspect exact model variants, LODs, geometry nodes, texture payload and archive; benchmark/decimate or derive LODs only as linked derivatives. |
| AL01-ACG-001 | [Paving Stones 036](https://ambientcg.com/view?id=PavingStones036) | surface_material / paving | Item page exposes PBR maps and 1K/2K/4K/8K JPG and PNG archives. 2K JPG is listed as 32 MB and 2K PNG as 65 MB. ambientCG policy explicitly covers downloadable files. | Select exact archive; inspect included maps and metadata; hash files; verify tiling and channel semantics. |
| AL01-ACG-002 | [Concrete 049](https://ambientcg.com/view?id=Concrete049) | surface_material / concrete | Item page exposes PBR maps and 1K/2K/4K/8K JPG and PNG archives. 2K JPG is listed as 35 MB and 2K PNG as 74 MB; page describes approximate 1.1 m × 1.1 m dimensions. | Select exact archive; inspect included maps and metadata; hash files; verify tiling and channel semantics. |
| AL01-KEN-001 | [Car Kit](https://kenney-assets.itch.io/car-kit) | vehicle_ground / cars and utility vehicles | Item page explicitly states CC0 1.0 Universal, commercial use permitted, no attribution required. It advertises 40+ models in OBJ, FBX and glTF; listed ZIP is 3.4 MB. Kenney support independently states CC0 for game assets on asset pages. | Download exact pack and inspect included LICENSE, model count, mesh quality, materials, pivots, scale and third-party content. Style is supplementary rather than a verified realistic fleet. |
| AL01-OGA-001 | [3D Car](https://opengameart.org/content/3d-car-0) | vehicle_ground / car | Item page explicitly displays CC0. It reports 4,391 vertices and 8,906 triangles; download is a 517.1 KB ZIP. | Download and inspect archive, creator/source provenance, actual model format, embedded content, mesh/material quality and scale. |
| AL01-SI-001 | [Command Module, Apollo 11](https://3d.si.edu/object/nasm_A19700102000) | spacecraft / crew capsule | Object page reports “Restrictions & Rights: CC0” and “Metadata Usage: CC0”. It lists full-resolution combined OBJ and low-resolution interior OBJ, glTF and GLB variants, with scale in metres. Page also includes public-domain usage wording and Smithsonian recommendations to credit the institution and not misrepresent source data. | Treat as candidate only. Inspect actual downloadable package, third-party/embedded content, data license notices and Smithsonian 3D disclaimer before any approval. Do not reinterpret public-domain wording alone as CC0; the item-specific CC0 field is the relevant evidence. |

## Coverage assessment

| Domain | Current source coverage | Gap / next research |
|---|---|---|
| Terrain, rocky ground, soil, gravel | Strong PBR source coverage from Poly Haven and ambientCG | Expand per biome and verify tileability/physical scale. |
| Asphalt, concrete, paving | Strong representative PBR coverage | Add road markings, road damage, curbs, shoulders and bridge-deck materials. |
| Snow, ice, sand, desert | Likely covered by PBR catalogs, but not individually sampled in this pass | Search and verify exact candidates. |
| Vegetation | Poly Haven provides model candidates and PBR sources | Need broad species/biome coverage, grass/ground cover, seasonal states, low-poly/LOD-ready variants. |
| Architecture and infrastructure | Poly Haven has some models; Kenney supplements with stylized assets | Major gap for coherent realistic residential, commercial, industrial, rail, port and utility sets. |
| Ground transport | Kenney Car Kit and individual OpenGameArt entries provide leads | Major gap for realistic cars, trucks, buses, motorcycles, construction vehicles and trains. |
| Maritime | No item-level candidate verified in this pass | Ships, boats, submarines, port equipment. |
| Aviation | No item-level candidate verified in this pass | Civil, military and general aviation; helicopters and drones. |
| Space and orbital | Smithsonian Apollo 11 Command Module is a candidate | Need launch vehicles, spacecraft, satellites, stations, probes and consistent scale families. |
| Atmosphere and sky | Poly Haven / ambientCG can supply HDRIs and surface inputs | Dynamic planetary atmosphere, clouds, weather and volumetric effects remain engine/runtime work, not static asset coverage. |

## Automation recommendations

1. **Poly Haven:** build an API-backed metadata inventory. Use the documented API for asset IDs, categories, tags, authors, dimensions, file metadata, download URLs, dependency lists and source hashes. Keep the API's live-service credit and User-Agent conditions separate from the CC0 license of downloaded assets.
2. **ambientCG:** use API v3 for discovery and metadata. Do not build new tooling on API v2, which its documentation says no longer receives updates. Resolve the exact asset ID and download variant before intake.
3. **Kenney:** use a manually curated pack manifest. Store the exact item page and the included license file with the downloaded pack; do not assume every file in an arbitrary repack is covered.
4. **OpenGameArt:** crawl/search only as discovery, then require an item-specific CC0 declaration and archive inspection. Never bulk-approve a collection.
5. **Smithsonian:** use object-level metadata and CC0 designation. Public-domain wording, metadata rights and the 3D media package must be distinguished; inspect the model download and disclaimer before acceptance.

## Not done in this pass

- No asset archives or binaries downloaded.
- No archive contents or embedded third-party materials inspected.
- No SHA-256 checksums generated.
- No Unity imports or performance tests run.
- No item added to `registry/assets.json`.
- No candidate promoted to `approved`.

## Primary evidence URLs

- Poly Haven license: https://polyhaven.com/license
- Poly Haven API and API terms: https://polyhaven.com/our-api
- Poly Haven API specification: https://github.com/Poly-Haven/Public-API
- ambientCG license: https://docs.ambientcg.com/license/
- ambientCG API v3: https://docs.ambientcg.com/api/v3/
- Kenney support: https://kenney.nl/support
- Kenney Car Kit item page: https://kenney-assets.itch.io/car-kit
- OpenGameArt 3D Car item page: https://opengameart.org/content/3d-car-0
- Smithsonian Open Access FAQ: https://www.si.edu/openaccess/faq
- Smithsonian Apollo 11 Command Module: https://3d.si.edu/object/nasm_A19700102000
