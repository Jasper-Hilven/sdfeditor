# 59 — Initial download under 20 MB (CrazyGames' mobile homepage)

Part of **Minigun Monsters** on mobile. Last of the mobile tickets (after 55-58). Optional for playing on mobile; required for the
mobile homepage: CrazyGames counts the bytes from the start of loading to the first `gameplayStart` and wants **20 MB or less** for
the mobile homepage (50 MB for desktop).

## Today (the CrazyGames build of 2026-11-03)

- Player: 19.3 MB (`Web.data.br` 9.96, `Web.wasm.br` 9.25, framework and loader 0.1). Boot only; every level streams (ticket 52).
- A first play starts in the Fungal level (`LevelRotation.FirstPlayLevel`): its own bundles 6.5 MB plus its shared ones, about 12.8 MB
  in all, so about **32 MB** to the first gameplay start. Smallest level: Bathroom, about 8.4 MB with its shared bundles.
- What the player carries that the levels do not reference: the soldier body and its animations, the 12 heads, the weapons, the
  effects (loaded from Resources), the GUI and its fonts, the audio (ticket 52's Status).

## Work

- Measure first: the build report's largest assets in `Web.data` and the wasm's largest assemblies/modules (strip what is unused:
  managed stripping level, unused engine modules, the Input Manager vs Input System, IL2CPP code size options). Write the table.
- Then, cheapest first, until the first gameplay start is under 20 MB **on touch devices** (desktop may keep the Fungal first play):
  1. code size: managed stripping High, strip engine code, IL2CPP "Optimize Size", remove unused packages;
  2. textures and audio in the player: compression and sizes (music streamed or lower bitrate);
  3. only the heads, weapons and effects the first match needs in the player; the rest streams as bundles in the background
     (needs 45, 47's head loading to accept a head arriving later);
  4. on touch devices the first match on the smallest level, or a lighter variant of the Fungal level.
- Keep the game identical once loaded. Re-measure with the build log and a headless Chrome network count up to `gameplayStart`.

## Done when

- On a touch device (`?device=mobile`), the bytes downloaded before the first `gameplayStart` are 20 MB or less (measured in headless
  Chrome, numbers in the Status); desktop stays under 50 MB.
- EditMode tests, the five-match play test and the CrazyGames build pass.

## Status

Not started.
