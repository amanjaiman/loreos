# docs/assets

Static assets for the docs and the README.

## demo.gif (README hero — spec 011 T004)

The README references `docs/assets/demo.gif`. Record it once the first-run flow is
verified on a clean VM (T003), then drop the file here and uncomment the image line
in [`../../README.md`](../../README.md).

What it should show (the "ten-minute test", sped up), end to end:

1. Run `LoreSetup.exe` → Lore opens.
2. Onboarding: privacy explainer → connect a model → green **Test**.
3. A capture happening (or `lore add "<fact>"`).
4. `lore mcp install claude-desktop`, then Claude answering *"Using my Lore memory,
   …"* from a real memory.

Keep it short (~20–30 s), no real secrets/keys on screen, and reasonably small
(≲ a few MB so the README loads fast). Any screen recorder + a GIF export works
(e.g. ScreenToGif on Windows).
