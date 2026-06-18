# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for the frozen memoryd sidecar (spec 011 T001).

Freezes ``lore_memoryd`` (FastAPI wrapper around mem0) into a Windows binary the
agent's supervisor launches in production instead of ``python -m lore_memoryd``
(spec 002 T005). Built by ``installer/build-memoryd.ps1`` and gated on a clean VM
via ``lore-memoryd.exe --selftest``.

Decisions (see specs/011-packaging/plan.md and spec 002 spike Finding 4):

* ``--onedir`` (COLLECT below), not ``--onefile``. ``--onefile`` re-extracts the
  whole tree to a temp dir on *every* launch; because the supervisor restarts the
  sidecar on crash, that cold-start cost would be paid repeatedly. ``--onedir``
  extracts once at install time, so each launch is fast and stays well inside the
  supervisor's health-gate budget. The cost — a folder of files instead of one exe
  — is irrelevant: the installer (T002) bundles the folder either way.
* The unused ML stack (torch/transformers/scipy/sklearn) is excluded. mem0 pulls
  these transitively, but Lore uses the Ollama/provider embedder at runtime, never
  them. Excluding drops the payload from ~358 MB to ~175 MB (spike Finding 4).
"""

import os

from PyInstaller.utils.hooks import collect_data_files, collect_submodules

# Make lore_memoryd importable from source even if it isn't pip-installed in the
# build environment (the build script installs it, but this keeps the spec robust).
memoryd_src = os.path.abspath(os.path.join(SPECPATH, "..", "memoryd"))  # noqa: F821 (SPECPATH is injected by PyInstaller)

hiddenimports = []
hiddenimports += collect_submodules("mem0")
hiddenimports += collect_submodules("qdrant_client")

datas = []
datas += collect_data_files("mem0")
datas += collect_data_files("qdrant_client")

# mem0 ships shims for dozens of optional integrations (vector stores, LLM/embedder
# providers, web tools) and imports the configured one lazily *by name* at runtime.
# Lore only ever configures Qdrant + Ollama/OpenAI, so every other integration's
# heavy third-party dep is dead weight: collect_submodules above keeps mem0's shim
# modules present (so any supported provider still resolves), but the unused backends
# below are never imported on Lore's path and are safe to drop.
#
#   * torch/transformers/scipy/sklearn: the local ML embedding stack Lore never uses
#     (embedder is Ollama or a provider's first-party API) — spike Finding 4.
#   * playwright/googleapiclient/botocore: mem0's web-crawl and cloud integrations.
#   * faiss/pandas/jieba: an alternative vector store + data/tokenizer extras.
#
# Excluding these drops the (uncompressed --onedir) payload from ~430 MB to ~175 MB.
excludes = [
    "torch",
    "transformers",
    "sentence_transformers",
    "scipy",
    "sklearn",
    "matplotlib",
    "playwright",
    "googleapiclient",
    "botocore",
    "boto3",
    "faiss",
    "faiss_cpu",
    "pandas",
    "jieba",
]


a = Analysis(
    [os.path.join(SPECPATH, "memoryd_entry.py")],  # noqa: F821
    pathex=[memoryd_src],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=excludes,
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="lore-memoryd",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name="lore-memoryd",
)
