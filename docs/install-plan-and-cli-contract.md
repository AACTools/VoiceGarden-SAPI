# Installer Plan and CLI Contract (v1)

## Goal

Use one machine-readable install contract for:

- interactive installer flow
- silent installs (`--silent`)
- enterprise/IT deployment

## Installer modes

- `Installer.exe` (interactive UI)
- `Installer.exe --silent ...` (non-interactive)
- `Installer.exe --plan <path-to-json>` (declarative execution)
- `Installer.exe <path-to-json>` (positional plan path, drag/drop friendly)
- `InstallPlanRunner.exe` (plan-runner mode; auto-loads adjacent `install-plan.json` and runs silently)

## CLI options (implemented)

- `--silent`
- `--json`
- `--dry-run`
- `--plan <file>`
- `--scope current-user|all-users`
- `--arch x64|x86|x64,x86`
- `--engine azure|edge|sherpa|narrator` (repeatable)
- `--azure-key <value>`
- `--azure-region <value>`
- `--azure-validate`
- `--msix <path>`
- `--msix-install`
- `--msix-extract-only`
- `--narrator-path <path>`
- `--sherpa-model <id>` (repeatable)
- `--sherpa-rescan`
- `--sherpa-promote-hklm`
- `--sherpa-compat-alias none|en-us|dual`
- `--sherpa-compat-model <id>` (repeatable)
- `--sherpa-test-voice <id>`

## InstallPlan JSON shape (v1)

```json
{
  "version": 1,
  "policy": {
    "allowed_engines": ["azure_online", "embedded_msix", "sherpa_offline", "narrator"],
    "ui_visibility": {
      "azure_online": "show",
      "embedded_msix": "show",
      "sherpa_offline": "show",
      "narrator": "show"
    },
    "require_explicit_enable": false
  },
  "scope": "current-user",
  "architectures": ["x64"],
  "engines": {
    "azure_online": {
      "enabled": false,
      "key": "",
      "region": "",
      "validate": true
    },
    "embedded_msix": {
      "enabled": false,
      "package_path": "",
      "install": true
    },
    "sherpa_offline": {
      "enabled": true,
      "download": [],
      "rescan": true,
      "promote_hklm": false,
      "compat_alias": {
        "mode": "none",
        "model_ids": []
      },
      "test_voice_id": ""
    }
  },
  "post_install": {
    "register_com": true,
    "verify_registration": true,
    "run_self_test": true
  }
}
```

## Example plan

```json
{
  "version": 1,
  "policy": {
    "allowed_engines": ["azure_online", "sherpa_offline"],
    "ui_visibility": {
      "azure_online": "show",
      "embedded_msix": "hide",
      "sherpa_offline": "show",
      "narrator": "hide"
    },
    "require_explicit_enable": true
  },
  "scope": "all-users",
  "architectures": ["x64", "x86"],
  "engines": {
    "azure_online": {
      "enabled": true,
      "key": "YOUR_KEY",
      "region": "westeurope",
      "validate": true
    },
    "embedded_msix": {
      "enabled": false
    },
    "sherpa_offline": {
      "enabled": true,
      "download": ["piper-en-alan-medium"],
      "rescan": true,
      "promote_hklm": true,
      "compat_alias": {
        "mode": "en-us",
        "model_ids": ["piper-en-alan-medium"]
      },
      "test_voice_id": "piper-en-alan-medium"
    }
  },
  "post_install": {
    "register_com": true,
    "verify_registration": true,
    "run_self_test": true
  }
}
```

## Exit codes

- `0` success
- `1` invalid args/runtime failure
- `2` plan validation failure
- `5` engine configuration failure
- `6` sherpa model operation failure
- `7` self-test failure

## Notes

- Embedded MSIX supports install attempt via `Add-AppxPackage` and extraction fallback.
- Sherpa steps are delegated to `SherpaOnnxConfig.exe`.
- For web-generated plans, prefer redacting secrets in logs and handling Azure keys carefully.
- Fixture plans for web/installer contract checks live in `samples/install-plans/`.
 - Installer UI should honor `policy.ui_visibility` and hide elements marked `hide`.
 - Plan validation should reject any engine enabled outside `policy.allowed_engines`.
 - If `policy.require_explicit_enable` is true, all engine defaults are forced to `enabled=false` unless explicitly set by the plan.
