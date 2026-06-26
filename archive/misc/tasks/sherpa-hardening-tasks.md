# Sherpa Integration Task List

## Completed in this patch
- Unify local model root to `%LOCALAPPDATA%\\NaturalVoiceSAPIAdapter\\models`.
- Improve Sherpa model metadata extraction and validation in native discovery.
- Add per-model scan error collection in model discovery.
- Improve Sherpa token registration behavior for unknown language metadata (skip instead of silently forcing English).
- Set Sherpa token gender default to `Neutral` unless name hints are explicit.
- Add `Rescan Models` action in SherpaOnnxConfig GUI.
- Add `rescan` CLI command and `rescan-gui` startup mode.
- Add installer `Rescan` button that launches `SherpaOnnxConfig.exe rescan-gui`.
- Persist last scan errors to `%LOCALAPPDATA%\\NaturalVoiceSAPIAdapter\\sherpa_model_scan_errors.json`.
- Document plain-text-only Sherpa SSML behavior in `README.md`.
- Add `scripts/verify-sherpa-integration.ps1`.

## Follow-up hardening
- Replace Sherpa engine static config locals with instance-owned storage in `SherpaOnnxEngine.cpp`.
- Add sample-rate-aware output/resampling path for non-24kHz Sherpa models.
- Add stronger locale extraction from model metadata sidecars when available.
- Add automated regression test for SAPI token attributes (language/gender/name/locale).
