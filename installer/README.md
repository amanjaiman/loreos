# installer/

Windows packaging for the agent, the bundled memoryd sidecar, and the app
(spec [011-packaging](../specs/011-packaging)).

## memoryd sidecar (T001)

`memoryd.spec` freezes the Python `lore_memoryd` service into a Windows binary so
end users never install Python. Build it from a Python 3.11 environment:

```powershell
./build-memoryd.ps1
```

This installs `lore-memoryd` + a pinned PyInstaller, runs the spec, and gates the
result with `lore-memoryd.exe --selftest` (the clean-VM check from the spec 002
spike: import the whole dependency tree, print bundled versions, exit 0). Output
lands in `installer/dist/lore-memoryd/` (`build/` and `dist/` are gitignored).

Notes:

- **`--onedir`, not `--onefile`** — `--onefile` re-extracts to a temp dir on every
  launch; the supervisor restarts the sidecar on crash, so that cost would recur.
  `--onedir` extracts once at install, so each launch is fast (~0.5 s cold) and
  stays inside the supervisor's health gate.
- **~175 MB** — `memoryd.spec` excludes the unused ML stack (torch/transformers)
  and mem0's unused optional integrations (playwright/googleapiclient/faiss/…),
  which mem0 imports lazily by provider name and Lore never configures. Without the
  excludes the payload is ~430 MB.

The agent's supervisor (spec 002) auto-discovers this exe in production: when a
build is laid out with `<agent dir>/memoryd/lore-memoryd.exe` it launches that;
otherwise it falls back to `python -m lore_memoryd` for from-source dev. The
installer (T002) lays the frozen folder into that conventional location.

## Installer assembly (T002, pending)

electron-forge make (Squirrel) bundling the published agent, this memoryd folder,
and the `lore` CLI on PATH, with code-signing — see the spec.
