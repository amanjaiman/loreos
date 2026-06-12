"""Guard the constitution's zero-telemetry rule (§1.2): importing lore_memoryd
must disable mem0's default PostHog telemetry before mem0 initializes."""

from __future__ import annotations

import os

import lore_memoryd  # noqa: F401 — import runs the telemetry kill-switch


def test_env_flag_is_forced_off() -> None:
    assert os.environ["MEM0_TELEMETRY"] == "False"


def test_mem0_reads_telemetry_as_disabled() -> None:
    from mem0.memory import telemetry

    # mem0 resolves MEM0_TELEMETRY to a bool at import; our guard ran first, so the
    # value mem0 itself gates all PostHog capture on is False.
    assert telemetry.MEM0_TELEMETRY is False
