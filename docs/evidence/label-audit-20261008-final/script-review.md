# Label audit batch script static review

Reviewed source: `D:\source\Sezika\.artifacts\label-audit-20261008\Invoke-LabelAuditBatch.ps1` (180 lines, original review).

Verdict: **not passed pending two P2 fixes**. This review is static evidence only; it did not execute the batch script, call an API, or access credentials, configuration, or the CC Switch database.

## Required fixes

1. **P2 — locally validate the response contract before accepting records** (lines 133–160). The request asks the gateway for strict JSON Schema, but local `ConvertFrom-Json` processing does not reject unknown/missing properties, invalid enums, or incorrect JSON value types. A fractional `answer_index` (for example 1.2) can pass the bounds check and be cast to an integer for ID comparison. An unknown `answer_status` with a null answer can enter the null-answer branch. `clarity_status`, revision boolean/type, and flag/reason/text consistency are also unchecked. Add strict local validation of the saved response; HTTP must not be repeated merely to repair validation.

2. **P2 — enforce the shared 900-second phase deadline throughout the batch** (lines 35–36 and `Check-Budget`). The deadline is checked once at entry; later checks cover batch time and cancellation only. A request started near phase second 899 can consume another 240 seconds. Include the phase deadline in every budget check and limit the request cancellation timeout to the minimum remaining phase, batch, and request time.

## Additional findings

- Lines 157–159 search quotes in fields joined by an artificial newline. A quote spanning two source fields may pass although it does not appear in any original segment. Validate against each original segment independently.
- Alternative IDs have membership/count checks, but local uniqueness and JSON type validation are incomplete.
- Sequential cleanup at lines 173–179 may skip later disposals/result writing and replace the primary failure when an earlier cleanup throws. Make cleanup steps individually protected while preserving the primary exception.
- `HttpClientHandler` enables redirects by default. The claimed `maximum_http_requests=1` bounds the application `SendAsync` call, not all wire requests; disable redirects if retaining that claim.

## Confirmed safeguards and evidence limits

The request sends record, language, request and candidate IDs without original target labels. The script uses the API key in the Authorization header, rather than the body or provenance, and redacts its exact value before persisting response/failure text. It bounds Start to 0–31 and Count to 1–8, uses one application `SendAsync` without retry, and blocks reusing an existing batch directory. It includes a 240-second HTTP timeout, a 245-iteration polling cap, a 260-second batch cap, cancellation and progress reporting. It checks record/question IDs, full count, duplicate records, answer index/ID mapping, alternative membership, quote count and substring evidence.

Returned model/effort identify gateway declarations only, not independently verified upstream identity. AI review retains `human_review=pending` and `formal=false`.

## Review process

The bounded original reader started at 2026-10-08 18:37:22 +08:00; the original identity writer started at 18:39:33 +08:00. Both original calls exited 0, with no API/network/Git/GPU or auth/config access. Their identities remain in `implementation-review-readers.json` (SHA256 `3CA3B1AFF8453387CD816E12760425A55BC0334BA37613A66F74BDCDAFD65CAD`). This report writer has a separate process manifest. Parent/root should verify these exact process identities are absent after completion.
