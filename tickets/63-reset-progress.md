# 63 — Reset all progress

Part of **Minigun Monsters**. CrazyGames QA (and any player) needs a way to start completely fresh: the next match is a true first
play, without clearing the browser's site data or the portal's saved data by hand.

## What

A **RESET ALL PROGRESS** button in **Settings**, bottom right, beside BACK, in danger red. It asks first with the Confirm card
("Reset all progress? / Your heads and history are lost." YES / NO). On YES the save is wiped and a fresh first play starts at once:
the Fungal level with the warm-up, Super Easy, the default head, no saved last levels.

## Decisions

- **Settings, not the pause menu**: players look for it there, and the pause menu is where an accidental press mid-match would hurt
  most. Settings is reachable from the pause menu, so it is also available in a match. It sits at the bottom next to BACK, outside the
  scrolling rows, so it is always visible (touch: a 154-unit thumb-sized button, text 34 pt, over `GuiFactory.MinReadable`).
- **Reset**: the heads (unlocked, months seen, picked), the difficulty, the last levels. That is the whole `GameSave` (`minigun.save`).
- **Kept**: the settings (volume, music, sound, quality, gore, sensitivity, touch look, auto-fire, screen shake, FPS, quality hints).
  They are separate `gui.*` PlayerPrefs keys, are not progress, and are not touched.
- **Every storage layer**: `ICampaignStorage.Remove` (new). `GameStorage` (what the game uses) deletes from PlayerPrefs and the
  portal's Data module (`WebPortal.RemoveData`, `CrazySDK.Data.DeleteKey`); `LayeredCampaignStorage` removes from both layers (a
  normal write goes to the primary only). `GameSave.Reset(storage)` removes the key, then writes the fresh default save back, so a
  reload (or a crash before the first level is live) cannot find the old save in either layer. Before the portal is up the portal
  write is a no-op, and `OnPortalReady` writes the in-memory (fresh) save when it is.
- **Leaving the match**: `GameLoop.ResetProgress` unhooks the current match's result (no difficulty change, no head unlock, no level
  recorded), disarms a pending end-card countdown, forgets `upcoming`, drops the queued next-level prefetch
  (`ContentStreamer.ForgetPrefetch`; what already arrived is released when the new level is live) and launches as Boot does
  (`LaunchFirst`). Ignored while a load is under way.

## Done when

- EditMode: a reset save is a first play (Fungal, warm-up, Super Easy, no heads) and the layered storage reads the fresh save from
  both layers (`GameSaveTests`).
- `GameLoopPlayTest.RunReset`: from a played save (Hard, three last levels, two heads) a match is live; Settings, the button, NO
  changes nothing, the button, YES; the save on disk is the fresh default at once and the next match is the Fungal first play with
  the warm-up on Super Easy with a different upcoming level.
- GUI snapshots at 1920x1080 and touch 800x450 in `Media/reset-63/`; the CrazyGames build passes.

## Status

Done in code and tests. Left: a run on the portal itself (the Data module's `DeleteKey` is untested off the portal), by the user.
