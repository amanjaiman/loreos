"""Tests for the ``--selftest`` packaging gate (spec 011 T001).

``--selftest`` is what proves the frozen exe imported its whole dependency tree on
a clean VM. These tests run the same code path against the source package.
"""

from __future__ import annotations

import json

from lore_memoryd import __version__
from lore_memoryd.__main__ import main, selftest


def test_selftest_reports_bundled_versions() -> None:
    report = selftest()
    assert report["lore_memoryd"] == __version__
    assert report["mem0"] == "2.0.5"
    assert report["app"] == "lore-memoryd"
    # Not frozen when run from source; the frozen exe reports "True".
    assert report["frozen"] == "False"


def test_main_selftest_prints_json_and_returns(capsys) -> None:  # type: ignore[no-untyped-def]
    main(["--selftest"])
    out = capsys.readouterr().out
    parsed = json.loads(out)
    assert parsed["mem0"] == "2.0.5"
