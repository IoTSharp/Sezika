# Sezika.CalibrationTool

`Sezika.CalibrationTool evaluate` is the bounded S4-08 quality/calibration
runner. It consumes already materialized probabilities; it never downloads a
model, calls a teacher, or turns a fixture into quality evidence.

The JSONL prediction artifact contains one row per example:

```json
{"id":"c-1","split":"calibration","primitive":"boolean","label":1,"probabilities":[0.2,0.8],"language":"en","domain":"general"}
```

The manifest must declare `schema_version: 1`, `status` (`fixture_only` or
`accepted_for_quality`), `represents_model_quality`, fixed `record_count`,
`calibration_count`, `sealed_test_count`, frozen `abstain_below` threshold, and SHA-256 values for the complete
artifact plus canonical split streams (each original JSON line followed by
LF). `manifest_sha256` is optional because a file cannot contain a digest of
its own bytes; the report always records the observed manifest digest.

Temperature is fitted only on `calibration` rows. The frozen `sealed_test`
rows are evaluated once with calibrated and uncalibrated probabilities. Full
accuracy, macro F1, NLL, Brier and ECE retain the complete sealed-test
denominator; selective metrics expose their answered count separately.
Unaccepted fixtures return `gate.status = not_eligible`, even when synthetic
numbers happen to satisfy thresholds, and the CLI returns exit code 3 for both
`not_eligible` and `failed` gates. A `passed` gate is the only zero exit code.

The parser rejects duplicate JSON properties in both the manifest and every
prediction row before materializing fields. A bounded parser self-check is
available as `Sezika.CalibrationTool self-test`; this only checks input
mechanics and does not execute inference or establish quality.
