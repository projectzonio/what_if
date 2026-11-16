# Licensing and provenance policy

## Release rule

The distributable game is intended to be free and publicly available. The asset library therefore accepts **CC0 1.0 Universal only**. A free download, public-domain label, permissive-looking terms, or non-commercial permission is not sufficient.

Do not accept:
- CC-BY, CC-BY-SA, CC-BY-NC, CC-BY-ND or any other Creative Commons variant except CC0 1.0;
- QAL or other custom licenses;
- editorial, research-only, educational-only, personal-use-only or non-commercial assets;
- assets with unknown, conflicting, missing or ambiguous rights;
- assets whose license applies to the uploader's page but not to all included files;
- assets containing third-party components whose rights are not covered;
- scraped, extracted or converted assets where the source rights cannot be established.

Do not infer CC0 from an author's other uploads, a collection/category page, a search result, a filename, or the fact that the asset is downloadable without payment.

## Evidence required for every accepted asset

A record may enter `registry/assets.json` only when all fields below are established:

1. Exact asset detail page URL.
2. Exact license evidence URL, preferably the asset page's own license declaration; if it relies on publisher-wide terms, record both the asset page and the applicable terms.
3. Explicit license identifier `CC0-1.0` or an explicit public-domain dedication that the legal reviewer has determined is equivalent and acceptable. The project default remains CC0-only; public-domain status alone is not auto-converted to CC0.
4. Evidence excerpt or concise factual note identifying what the declaration covers.
5. Verification timestamp in UTC and verifier identity/role.
6. Exact downloaded variant, format, resolution and included files.
7. SHA-256 checksum for every retained source file.
8. Confirmation that no excluded third-party material, attribution requirement, trademark restriction, privacy/personality right or other separate restriction blocks the intended use.

A publisher-wide policy may support a record, but never replaces the exact asset page and exact variant identification.

## Status model

- `candidate`: discovery lead; not cleared for download/import/release.
- `pending_review`: evidence collected; human review incomplete.
- `approved`: all evidence gates passed; may enter the accepted registry.
- `rejected`: incompatible or insufficient rights; never import.
- `withdrawn`: previously accepted but rights/evidence changed; quarantine and remove from future builds.

Only `approved` records belong in `registry/assets.json`. Other states belong in the candidate/review workflow.

## Public-domain sources

A public-domain statement is not automatically the same as CC0 1.0. It may be acceptable only after an explicit rights review records the jurisdiction, source statement, object-specific scope, third-party-content caveats and the project's decision. Until then, keep it outside the accepted registry. Do not use NASA/Smithsonian/US government branding as a blanket license for all hosted content.

## Attribution and notices

CC0 does not require attribution, but retain provenance internally. Preserve creator, source, asset page, evidence URL, verification date and checksums. If the source requests voluntary credit, it may be included in an optional credits file without changing the license status. Never imply that voluntary credit is legally required by CC0.

## Change monitoring

Recheck source terms when:
- a source changes its license or terms;
- a pack is updated or replaced;
- the download variant changes;
- a takedown, rights dispute or third-party-content notice appears.

Do not silently overwrite a previously verified binary. Create a new version record and retain the old checksum/evidence trail. A rights concern moves the record to `withdrawn` immediately pending review.
