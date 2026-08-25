# BrassLedger tax content

Tax content is maintained as versioned JSON and imported as an inactive draft. A package may be approved only after its official sources are captured, checksummed, independently reviewed, and its required regression examples pass in the current tax engine.

## Jurisdictions

Jurisdiction identity is separate from its name, code, type, country, and parent. Relationships are effective-dated, so annexation, dissolution, renaming, a county changing states, or a jurisdiction changing countries adds a new relationship interval without rewriting history. Proposed or disputed changes can coexist with the active relationship and do not affect payroll until approved and effective.

Use `schema/jurisdiction-catalog-v1.schema.json` for catalogs and `schema/tax-content-package-v1.schema.json` for calculation packages. State, county, city, school-district, special-district, tribal, and other jurisdiction types use the same model.

## Capture workflow

1. Capture the official publication and raw bytes; record its URL, retrieval time, revision/effective date, and SHA-256.
2. Create a new package version. Never overwrite an approved version.
3. Transcribe applicability, employee inputs, calculation parameters, brackets/tables, filing rules, exceptions, and official examples.
4. An LLM may prepare a draft, but its output must identify every source and remain inactive.
5. Independently compare the draft with the official publication.
6. Run required regression examples and boundary cases.
7. Approve and activate the package for its effective interval.

`us/state-reference-2026.json` is a coverage inventory. Its PIT classifications and SUTA wage bases are reference data, not withholding formulas. `formulaCoverage` explicitly distinguishes uncaptured, official-source-only, draft, approved, and non-applicable PIT coverage. Every `OfficialSourceCaptured` entry must point to a source-capture JSON document with activation explicitly disabled. A source capture becomes an importable draft package only after its formula, inputs, applicability rules, and regression examples are complete enough for the engine to execute.
