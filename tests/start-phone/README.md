# start-phone.sh tests

Bash-based tests for `start-phone.sh` covering hot-reload startup, logging, and error paths. No external test framework (no bats dependency) — uses simple assertions with pass/fail counters.

## Layout

```
tests/start-phone/
├── run-tests.sh           # entry point — runs all suites sequentially
├── smoke-test.sh          # priorities 1, 2, 3: bash -n syntax, full lifecycle, meta.json schema
├── error-paths-test.sh    # priority 5: api_key_missing, port_occupied fail() exit reasons
├── lib/
│   └── assertions.sh      # shared assert_* helpers (sourced)
└── README.md              # this file
```

## Run locally

```bash
# From the repo root:
bash tests/start-phone/run-tests.sh
```

Requirements:
- bash, curl, netstat, taskkill (Git Bash on Windows; native on Linux)
- `jq` for full meta.json schema assertions (falls back to grep if absent)
- `python3` or `nc` for the port_occupied test (skipped if neither available)
- Port 5123 must be free at the start of the smoke test

The smoke test invokes `start-phone.sh` end-to-end (~15–30s startup), verifies the run produced the expected log artifacts and a well-formed `meta.json`, then SIGTERMs the script and verifies the postmortem contract.

## What's covered (and what isn't)

| Path                          | Covered?      | Notes                                                                                                                |
| ----------------------------- | ------------- | -------------------------------------------------------------------------------------------------------------------- |
| `bash -n` syntax              | ✅ priority 1 | Trivial; catches typos on every PR.                                                                                   |
| Happy-path lifecycle          | ✅ priority 2 | Startup → URL provisioning → app responds → SIGTERM → port freed.                                                      |
| `meta.json` schema            | ✅ priority 3 | Required fields present, JSON-valid, correct types, `.exit_reason == "terminated"`.                                    |
| `api_key_missing`             | ✅ priority 5 | Env + Windows registry both empty. Skipped on dev machines where the real key is loadable (would produce false positives). |
| `port_occupied`               | ✅ priority 5 | Dummy listener holds 5123, script refuses with correct `exit_reason`.                                                  |
| `prereq_missing`              | ⚠ manual only | Would require PATH manipulation to hide `dotnet`/`cloudflared`.                                                        |
| `app_startup_failed`          | ⚠ manual only | Would require a project that fails to build.                                                                          |
| `app_timeout`                 | ⚠ manual only | Would require blocking the app from responding for 120s.                                                               |
| `tunnel_startup_failed`       | ⚠ manual only | Would require breaking `cloudflared`.                                                                                 |
| `tunnel_timeout`              | ⚠ manual only | Would require blocking tunnel URL provisioning.                                                                       |
| Rotation (keep last 10)       | ⚠ code review | Verified by inspection; regex matches only timestamp-prefixed dirs, LATEST immune by design.                           |
| `app_died` / `tunnel_died`    | ⚠ manual only | Triggered when a child process dies unexpectedly; hard to simulate deterministically.                                  |

## CI

`.github/workflows/ci.yml` runs `bash tests/start-phone/run-tests.sh` as a dedicated job (`start-phone-tests`) in parallel with the existing `dotnet test` job.
