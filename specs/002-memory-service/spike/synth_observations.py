"""Synthesize ~200 distilled observations approximating Lore's capture output.

SPIKE CODE — throwaway, not part of the product (see tasks.md T001).

Lore's capture pipeline (spec 003) distills screen activity into short, factual,
first-person-about-the-user observations before they reach mem0. We have no v1
``lore.db`` on this machine, so this script stands in a *representative* corpus:
realistic phrasing, a spread of apps/domains, plus embedded ground truth so the
eval can score mem0 objectively:

* ``dedup_group``  — observations that restate the same fact (mem0 should NOT
  create N separate memories for them).
* ``contradicts``  — a later fact that should make mem0 UPDATE/replace an earlier
  one rather than keep both.
* search queries with the observation ids that *should* rank for them.

Output: ``observations.json`` (the corpus) next to this file. Deterministic.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass
class Obs:
    id: str
    text: str
    domain: str
    # ids that state the same underlying fact as this one (incl. itself implicitly)
    dedup_group: str | None = None
    # if set, this obs is the *new* truth that should replace obs id given here
    contradicts: str | None = None
    tags: list[str] = field(default_factory=list)


# ---------------------------------------------------------------------------
# Hand-authored "anchor" observations with explicit ground truth. These are the
# ones the eval scores against (dedup, update, targeted search).
# ---------------------------------------------------------------------------
ANCHORS: list[Obs] = [
    # --- dedup group A: editor preference, restated 3 ways ----------------
    Obs("a1", "The user's main code editor is Visual Studio Code.", "preferences",
        dedup_group="editor", tags=["editor"]),
    Obs("a2", "User spends most of their coding time inside VS Code.", "preferences",
        dedup_group="editor", tags=["editor"]),
    Obs("a3", "Observed the user editing files in Visual Studio Code again today.", "preferences",
        dedup_group="editor", tags=["editor"]),
    # --- contradiction: editor changes to Neovim --------------------------
    Obs("a4", "The user has switched their primary editor from VS Code to Neovim.", "preferences",
        contradicts="a1", tags=["editor"]),

    # --- dedup group B: home city ----------------------------------------
    Obs("b1", "The user lives in Seattle.", "personal", dedup_group="city", tags=["location"]),
    Obs("b2", "User mentioned being based in Seattle, Washington.", "personal",
        dedup_group="city", tags=["location"]),
    # --- contradiction: moved to Austin ----------------------------------
    Obs("b3", "The user is relocating to Austin, Texas next month.", "personal",
        contradicts="b1", tags=["location"]),

    # --- dedup group C: a specific project deadline -----------------------
    Obs("c1", "The Memory Service spec (002) is due for review by June 20.", "work",
        dedup_group="deadline002", tags=["deadline", "lore"]),
    Obs("c2", "User noted the 002 memory-service review deadline is June 20.", "work",
        dedup_group="deadline002", tags=["deadline", "lore"]),

    # --- dietary preference + contradiction ------------------------------
    Obs("d1", "The user is vegetarian.", "health", dedup_group="diet", tags=["diet"]),
    Obs("d2", "User selected the vegetarian option when ordering lunch.", "health",
        dedup_group="diet", tags=["diet"]),
    Obs("d3", "The user has started eating fish again and now describes themselves as pescatarian.",
        "health", contradicts="d1", tags=["diet"]),

    # --- targeted search anchors (unique facts) --------------------------
    Obs("s1", "The user booked a flight to Tokyo departing March 14 on United Airlines.", "travel",
        tags=["travel", "flight"]),
    Obs("s2", "User is allergic to penicillin.", "health", tags=["medical", "allergy"]),
    Obs("s3", "The user's manager is named Priya Natarajan.", "work", tags=["people", "manager"]),
    Obs("s4", "The user is learning to play the cello, practicing about 30 minutes a day.", "hobby",
        tags=["music", "learning"]),
    Obs("s5", "The user prefers dark mode in every application.", "preferences", tags=["ui"]),
    Obs("s6", "User's car is a 2019 Subaru Outback, due for an oil change at 60k miles.", "personal",
        tags=["car"]),
    Obs("s7", "The user is reading 'The Three-Body Problem' by Liu Cixin.", "hobby",
        tags=["reading"]),
    Obs("s8", "User pays for a Spotify Family plan shared with three relatives.", "finance",
        tags=["subscription"]),
]


# ---------------------------------------------------------------------------
# Filler observations: realistic screen-derived noise across many apps, so the
# corpus density/size resembles a real day of capture (~200 total). No ground
# truth attached — they exercise extraction volume and search precision.
# ---------------------------------------------------------------------------
_FILLER_TEMPLATES: list[tuple[str, str]] = [
    ("work", "The user is debugging a {bug} in {file} inside their IDE."),
    ("work", "User opened a pull request titled '{pr}' on GitHub."),
    ("work", "The user is reviewing the {doc} document in a browser tab."),
    ("work", "User joined a video call with the {team} team in Microsoft Teams."),
    ("work", "The user wrote a Slack message to #{channel} about {topic}."),
    ("learning", "The user is reading the {tech} documentation page on {feature}."),
    ("learning", "User watched a tutorial about {tech} on YouTube."),
    ("travel", "The user searched for hotels in {city} for the {month} trip."),
    ("shopping", "User added a {item} to their Amazon cart."),
    ("shopping", "The user compared prices for a {item} across two retailer sites."),
    ("finance", "The user reviewed their {bank} checking account balance."),
    ("personal", "User scheduled a {appt} appointment for {month}."),
    ("hobby", "The user browsed recipes for {food} on a cooking blog."),
    ("communication", "User replied to an email from {person} about {topic}."),
]

_FILL = {
    "bug": ["null reference exception", "race condition", "memory leak", "off-by-one error",
            "timeout", "serialization error"],
    "file": ["MemorydClient.cs", "app.py", "routes.py", "Program.cs", "mem0_factory.py",
             "Supervisor.cs"],
    "pr": ["fix health gate backoff", "add search route", "pin mem0 version",
           "wire DI container", "remote engine mode"],
    "doc": ["architecture", "privacy", "onboarding", "API reference", "release notes"],
    "team": ["platform", "design", "infra", "memory", "capture"],
    "channel": ["dev", "lore-eng", "random", "incidents", "design-review"],
    "topic": ["the spike results", "the release date", "a flaky test", "the new embedder",
              "Qdrant tuning"],
    "tech": ["FastAPI", "mem0", "Qdrant", "React", "ASP.NET Core", "PyInstaller", "Electron"],
    "feature": ["lifespan events", "vector search", "dependency injection", "hooks",
                "background services", "single-file builds"],
    "city": ["Portland", "Denver", "Chicago", "Vancouver", "Boston"],
    "month": ["July", "August", "September", "October"],
    "item": ["mechanical keyboard", "USB-C hub", "standing desk mat", "webcam", "noise-cancelling headphones"],
    "bank": ["Chase", "Ally", "local credit union"],
    "appt": ["dentist", "haircut", "eye exam", "car service"],
    "food": ["ramen", "sourdough bread", "thai curry", "tacos", "risotto"],
    "person": ["Priya", "Marcus", "Dana", "the recruiter", "a vendor"],
}


def _build_filler(target_total: int) -> list[Obs]:
    out: list[Obs] = []
    keys = list(_FILL.keys())
    i = 0
    while len(ANCHORS) + len(out) < target_total:
        domain, template = _FILLER_TEMPLATES[i % len(_FILLER_TEMPLATES)]
        # deterministic, varied fills
        filled = template
        for k in keys:
            if "{" + k + "}" in filled:
                choices = _FILL[k]
                filled = filled.replace("{" + k + "}", choices[i % len(choices)])
        out.append(Obs(f"f{i:03d}", filled, domain, tags=["filler"]))
        i += 1
    return out


# Search queries with the observation ids that *should* surface (ground truth
# for relevance@k). Only anchors have reliable expected matches.
SEARCH_QUERIES: list[dict] = [
    {"query": "What editor does the user use?", "expect": ["a4"], "note": "post-update truth"},
    {"query": "Where does the user live?", "expect": ["b3"], "note": "post-update truth"},
    {"query": "Does the user have any allergies?", "expect": ["s2"]},
    {"query": "What are the user's dietary restrictions?", "expect": ["d3"], "note": "pescatarian now"},
    {"query": "Who is the user's manager?", "expect": ["s3"]},
    {"query": "What flights has the user booked?", "expect": ["s1"]},
    {"query": "What instrument is the user learning?", "expect": ["s4"]},
    {"query": "What car does the user drive?", "expect": ["s6"]},
    {"query": "When is the memory service spec due?", "expect": ["c1", "c2"]},
    {"query": "What book is the user reading?", "expect": ["s7"]},
]


def main() -> None:
    target = 200
    observations = ANCHORS + _build_filler(target)
    out_path = Path(__file__).with_name("observations.json")
    payload = {
        "observations": [asdict(o) for o in observations],
        "search_queries": SEARCH_QUERIES,
        "count": len(observations),
    }
    out_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"Wrote {len(observations)} observations to {out_path}")


if __name__ == "__main__":
    main()
