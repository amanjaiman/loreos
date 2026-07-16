# 002 spike harness — reproducibility runbook

> **Throwaway spike code (tasks.md T001).** None of this is part of the shipped
> product (`agent/`, `memoryd/`). It exists so the go/no-go in
> [`../spike-findings.md`](../spike-findings.md) is reproducible. Do not import it.

## What it does

Feeds a representative corpus of distilled observations through the candidate
production stack and scores it:

| Piece | Role under test |
|---|---|
| `gemma3n:e2b` / `qwen2.5:7b-instruct` (Ollama) | extraction + conflict-resolution LLM (the user's BYO capture model) |
| `nomic-embed-text` (Ollama) | the **local embedder default** candidate (768-dim) |
| Qdrant on-disk | mem0's vector store, no external service |
| PyInstaller one-file exe | the packaging proof for the bundled sidecar |

## Prerequisites

```sh
pip install mem0ai qdrant-client pyinstaller
ollama serve &                         # background daemon
ollama pull nomic-embed-text           # embedder (~274 MB)
ollama pull qwen2.5:7b-instruct        # capable extraction model (~4.7 GB)
ollama pull gemma3n:e2b                # weak-model contrast (optional)
```

## Run the evaluation

```sh
python synth_observations.py           # -> observations.json (200 obs + ground truth)
python run_eval.py --out results.json  # full corpus
python run_eval.py --limit 20          # anchors-only fast pass (dedup/update/search)
```

`run_eval.py` records, per observation, mem0's `event` (ADD / UPDATE / DELETE /
NONE) — the direct dedup/conflict signal — plus add latency, then runs the
ground-truth search queries and reports a keyword hit-rate and search latency.
The extraction LLM is selected by `LLM_MODEL` at the top of `run_eval.py`.

## Run the packaging proof

```sh
python -m PyInstaller --onefile --name lore-memoryd-proto \
  --collect-all mem0 --collect-submodules qdrant_client sidecar_proto.py
dist/lore-memoryd-proto.exe --selftest      # prints frozen dep versions, exits
dist/lore-memoryd-proto.exe                 # serves /health on 127.0.0.1:7843
```

A clean-VM run (the real acceptance bar) means copying just
`dist/lore-memoryd-proto.exe` to a machine with **no Python and no Ollama**
installed and confirming `--selftest` still prints versions — that proves the
dependency tree is self-contained.
