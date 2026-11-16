# AL-07 Asset Library tooling

Python 3.10+; Windows 10 and offline-first.

```powershell
py -3 -m venv .venv
.\.venv\Scripts\Activate.ps1
py -m pip install -e .\asset-library\tooling
# Optional metadata readers:
py -m pip install -e ".\asset-library\tooling[images,archives]"
assetlib validate-registry
assetlib inspect .\asset-library\samples
assetlib hash .\asset-library\samples
assetlib scan-duplicates .\asset-library\samples
assetlib inspect-archive .\asset-library\samples\asset.zip
assetlib validate-package .\asset-library\samples\package
assetlib report .\asset-library\samples --output .\asset-report.json
py -m unittest discover -s .\asset-library\tooling\tests -v
```

Exit codes: 0 pass, 1 validation failure, 2 execution/tooling error, 3 incomplete review.

Implemented: Draft 2020-12 schema validation; duplicate asset IDs; CC0-1.0 and verified status checks; SHA-256; ZIP CRC and member checks; TAR/gzip and optional 7z listing; path traversal/link/device/size/count/ratio guards; optional Pillow dimensions/aspect/alpha/bit-depth estimate/ICC metadata; OBJ and glTF/GLB metadata; exact and filename duplicate reports; package manifest checksum/missing/unexpected checks.

No network download, extraction, execution of archive contents, or automatic deletion occurs. Reports are JSON.

Limitations: Pillow and py7zr are optional. OBJ/glTF inspection is metadata-level, not full mesh validation. FBX, BLEND, DAE, STL and PLY are metadata-only. No actual Unity Editor import test is performed. The tool never infers roughness/metallic/normal semantics from image appearance and never upgrades Unity tested status.

Legal status is not independently established by software: a valid schema plus CC0-1.0 fields does not prove the evidence is authentic/current or covers every embedded component. Human item-level license review remains mandatory. Keep candidate records outside the accepted registry.
