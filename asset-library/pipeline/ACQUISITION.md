# Asset acquisition and validation pipeline

## Intake stages

1. **Discover** — identify a candidate and its exact publisher/item page. Record category, intended use and source.
2. **Rights review** — inspect item-specific license, publisher terms, included third-party content and restrictions. If uncertain, reject or leave pending; do not infer.
3. **Variant selection** — choose the highest useful source quality within the project budget. For general PBR surfaces target 2K (2048 px) as the default; retain 4K/8K only where measured visual benefit justifies memory and storage.
4. **Download** — use the official download route. Record exact variant, filenames, byte sizes and SHA-256 hashes. Do not commit credentials, cookies or private download tokens.
5. **Technical validation** — inspect archive integrity, file types, dimensions, UV/material completeness, mesh scale/orientation, normals, topology and texture channel semantics.
6. **Normalization** — create a reproducible Unity import profile or generated derivatives. Preserve originals unchanged and keep derivatives linked to the source record.
7. **Catalog** — create an accepted registry record conforming to the schema only after all legal and technical gates pass.
8. **Integration** — import into a sample Unity project or project test scene, verify rendering and performance, then expose the record to the world generator.
9. **Audit** — periodically recheck source terms and checksums; quarantine changed or disputed material.

## Separation of concerns

- Candidate leads are stored in `candidates/candidates.json`; they are not approved and must not be included in builds.
- Accepted records are stored only in `registry/assets.json`.
- Binary downloads are kept outside Git by default. The manifest records immutable checksums and canonical source/download references.
- Generated LODs, atlases, compressed textures and converted meshes are derivatives. They must retain a parent asset ID, tool/version and generation settings.
- The engine/world generator references stable asset IDs, not mutable filenames or third-party URLs.

## Technical checks

### Textures

- Check dimensions, aspect ratio, bit depth, color space, alpha, mipmaps and channel packing.
- Base color/albedo: sRGB. Normal, roughness, metallic, AO and height: linear data.
- Confirm normal-map convention (OpenGL +Y vs DirectX -Y) and convert explicitly if required.
- Check tile seams and physical texel density. Do not label a texture tileable without a seam test.
- Preserve individual source maps even when generating ORM/packed variants.
- For the 2K baseline, record exact pixel dimensions; do not assume a filename suffix guarantees resolution.

### Models

- Preferred exchange format: FBX for conventional Unity import; glTF/GLB is acceptable with a pinned importer; OBJ is a fallback where materials/UVs are simple.
- Inspect units, forward/up axes, pivot, transforms, UV sets, normals, tangents, material slots and embedded textures.
- Check mesh scale and silhouette at intended camera distance.
- Record whether LODs exist. Generate LODs only as derived content with settings and source linkage.
- Separate render mesh, collision proxy and simulation metadata. Do not use high-detail render meshes as authoritative collision by default.
- Check mesh topology, non-manifold geometry and duplicate geometry before accepting.

### Runtime suitability

- Tag assets by intended distance/role: close, mid, far, impostor, background.
- Record approximate triangles, material slots, texture memory and collider complexity when measurable.
- Planet-scale distribution must use instancing, batching, streaming and procedural placement policies; do not assume every catalogued object is resident.
- Avoid per-instance unique materials where GPU instancing or shared materials are possible.

## Validation output

Each intake run should produce:
- candidate ID and exact source page;
- license verdict and evidence URLs;
- downloaded file list and SHA-256;
- technical inspection results;
- conversion/import settings and tool versions;
- Unity test result and tested render pipeline;
- final status: approved, pending_review or rejected;
- reason and reviewer.

No numerical performance claim is accepted without a reproducible measurement and hardware/software context.
