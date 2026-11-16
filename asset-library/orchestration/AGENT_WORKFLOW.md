# Asset Library worker workflow

## Worker contract

Each worker owns one bounded category or pipeline task. Before work, inspect current registry state and the exact ownership brief. Do not overlap write paths with another worker.

## Required output

A worker returns:
1. concise scope and source list;
2. one candidate record per discovered asset, including exact item URL and proposed variant;
3. license evidence URL, quoted/precisely summarized scope, and a clear verdict;
4. format/resolution/file inventory;
5. technical suitability notes for Unity and planetary scale;
6. unresolved questions and explicit exclusions;
7. machine-readable records only for assets that pass all gates.

## No-go conditions

Stop an individual asset intake and mark it pending/rejected when:
- the license is not explicitly CC0 1.0;
- a pack includes mixed or unclear rights;
- a download mirror is the only available source;
- the asset includes third-party content not covered by the declaration;
- the file cannot be inspected or hashed;
- the item is corrupted, incomplete or unsuitable for intended use.

## Review

The orchestrator independently checks every license claim and a sample of technical claims against the source. A worker's assertion is not evidence. At least one exact source page and one exact license evidence location must be checked for each approved record; if a source policy covers an entire publisher catalog, verify that policy remains current and that the item belongs to the covered catalog.

## Performance discipline

Do not download entire catalogs by default. Start with representative samples per category, evaluate quality and Unity import cost, then scale acquisition. Preserve original files and measure actual storage, memory and rendering impact before setting budgets.
