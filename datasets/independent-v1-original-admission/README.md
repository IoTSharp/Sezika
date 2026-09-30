# Independent v1 candidate admission

This directory is a candidate data package for the S4-05 admission contract. It is
not an accepted training set and it does not contain a quality, calibration, or
sealed-test result.

`records.jsonl` is a small project-authored contract fixture. It has six training,
six development, two calibration, and two sealed-test rows so the split contract
can be exercised without a teacher service. Every row is deliberately marked
`human_review: Pending`; the source license review and test seal are also pending.
The audit must therefore return `blocked` until an authorized reviewer supplies
license evidence, row-level review, and an independently controlled seal.

Run from the repository root after replacing both hash placeholders in
`manifest.json` with the SHA-256 of `records.jsonl`:

```powershell
dotnet tools/Sezika.DatasetTool/bin/Release/net10.0/Sezika.DatasetTool.dll audit-splits `
  datasets/independent-v1-original-admission/manifest.json `
  datasets/independent-v1-original-admission/audit-report.json
```

The report is a diagnostic artifact. A blocked report is expected for this
candidate and must not be used as a model-quality or calibration result.
