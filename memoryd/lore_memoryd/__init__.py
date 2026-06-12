"""Lore memory sidecar (memoryd).

A FastAPI service the Lore agent supervises, wrapping mem0 behind localhost-only
HTTP routes. Spec 001 shipped `/health`; spec 002 adds the mem0-backed memory
service.
"""

import os

# Constitution §1.2 (zero telemetry) and §4.3 (closed, documented egress list):
# mem0 ships anonymous PostHog telemetry to us.i.posthog.com, ENABLED BY DEFAULT.
# Force it off here — before mem0 is imported anywhere in this package — so Lore
# never phones home through its memory dependency. As a side effect this also stops
# mem0 from creating a second, *global* on-disk Qdrant under ~/.mem0
# (its "mem0migrations" telemetry store), which otherwise locks across Memory
# rebuilds. This assignment is unconditional and trust-critical: do not weaken it
# to setdefault. See docs/privacy.md.
os.environ["MEM0_TELEMETRY"] = "False"

__version__ = "0.1.0"
