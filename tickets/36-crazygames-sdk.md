# 36 — CrazyGames SDK

The WebGL build is published on CrazyGames. Everything the portal wants goes through one wrapper, `SdfPlatform.WebPortal`
(`Unity/Assets/Platform/Runtime/WebPortal.cs`); with no SDK (desktop build, WebGL off the portal, editor batch mode) every call does nothing.

## SDK

- **CrazyGames Unity SDK 5.35.0** (`CrazySDK.Version`; the SDK v3 of their HTML5 SDK is loaded by its jslib from `sdk.crazygames.com`),
  from `https://sdk.crazygames.com/UnityCrazySDK.unitypackage` (changelog: `.../UnityChangelog.md`). It is a `.unitypackage` only (no UPM
  package, no git URL) and ships no WebGL template. Installed by unpacking it into `Assets/CrazySDK` and `Assets/Plugins/crazySDK.jslib`
  with its metas (Unity was open, so no import); the `Demo` folder was left out. Version pinned by what is committed; to update, delete
  `Assets/CrazySDK`, import the new package, and re-apply the whitelist below (a reimport resets `CrazyGamesSettings`).
- The SDK has no asmdef, so it lives in Assembly-CSharp; the pure logic is in `Assets/Platform/Core` (`SdfPlatform.Core`, tests in
  `Assets/Platform/Tests`), the wrapper in Assembly-CSharp next to the SDK.

## Decisions

- **Init**: `CampaignRunner.Awake` (Boot) calls `WebPortal.Init`, once, not from a RuntimeInitializeOnLoad (the SDK's own sitelock uses one).
  The game never waits for it. The SDK is used only when `CrazySDK.IsAvailable` (CrazyGames, localhost, editor outside batch mode).
- **Gameplay start/stop**: `GuiFlow.Update` reports `playing` = screen is Hud, Scoreboard or Death, no confirm card, no match-end sequence.
  So Pause (also from focus or pointer-lock loss), Settings, menus, comics/cutscenes, Loading, MatchEnd, Victory, Defeat and Credits all stop.
  `GameplayTracker` sends one event per change. A scene load and a running ad also force stop.
- **Loading**: the Unity SDK has no loadingStart/Stop (the docs say loading is the Unity loader's, before the game starts), so
  `SceneLoader` calls `WebPortal.LoadingStart/Stop` which only stop gameplay. The first gameplay start is the portal's initial-download mark.
- **Ads are off for the initial launch** (CrazyGames allows none there): `WebPortal.AdsAllowed = false`, so `MidgameBreak` goes straight on, in builds and in the editor alike. Set it true once ads are allowed; the wiring below stays in place.
- **Midgame ad** (`WebPortal.MidgameBreak`): just after a finished level, won or lost: after the result banner (and a win's beat,
  the boss's victory), before the Victory or Defeat screen (`GuiFlow.AfterLevel`, set by `CampaignRunner`; never in a match, never over a
  comic, never on load). `AdBreakPolicy`:
  at least 45 s of real battle played since the last ask (the warm-up does not count) and 150 s since it; the SDK adds its own 3 min spacing
  and no ad near the start, and drops early requests. The SDK pauses time and mutes `AudioListener` while the ad is up and restores
  both; the game's buttons are swallowed meanwhile (`WebPortal.AdPlaying`) and the action runs after the ad ends, finished or not (no fill,
  adblock, error). No rewarded ads, no banners (Basic Launch has no ads at all; the game keeps working).
- **Happy time**: the boss beaten and the credits done (the story finished) only; the portal asks for sparing use, not per level.
- **Save**: on the web with the SDK up (CrazyGames or localhost) the campaign save goes through the Data module
  (`PortalCampaignStorage`, behind `LayeredCampaignStorage`: read the portal, else the old PlayerPrefs save; write the portal only, nothing is
  deleted). The save is first read from PlayerPrefs at Awake (the SDK is not up yet) and re-read from the portal when it comes up, if no
  step has started. Settings (volume, gore, ...) stay in PlayerPrefs. In the editor and desktop builds nothing changes.
- **Mute**: the game is submitted as Unity, so the portal mutes the page itself (`muteAudio` is not handled).
- **Sitelock**: the SDK freezes the game on any domain that is not CrazyGames, localhost or listed in `CrazySDK/Resources/CrazyGamesSettings`
  `whitelistedDomains`. Listed: `itch.io`, `itch.zone`, `github.io`. Add any other domain the plain WebGL build is hosted on.
- **Editor**: SDK logs show; ads are off unless Tools > Portal > Simulate ads in play mode is ticked (then the SDK's 3 s overlay plays).

## WebGL settings

None changed. Required by the portal: compressed build (Brotli or Gzip, no development build) - the project is on Gzip with the
decompression fallback; the Default template works (the jslib adds the SDK script). Not set, for the user: the portal's limits (initial
download <= 50 MB until the first gameplay start, total <= 250 MB, <= 1500 files) and desktop Chrome/Edge, 4 GB Chromebook.

## Build and test on CrazyGames

1. Build WebGL (release, not development) with the project's build entry, `Tools > SDF Mushrooms > Build Web` or
   `Unity -batchmode -quit -projectPath Unity -buildTarget WebGL -executeMethod SdfMushrooms.Editor.WebBuild.Build`. It writes the player
   (the initial download) and the streamed arenas and comics into `Unity/Builds/Web/Bundles` (ticket 38); upload the whole `Builds/Web`
   folder, `Bundles` included, with relative paths. (The CrazySDK build window builds every scene into the player, one big download.)
2. Local: serve the build folder from `localhost` (e.g. `python3 -m http.server`): the SDK runs in local mode (ads show a text overlay,
   events are logged in the browser console; `?sdk_debug=true` for more). `?useLocalSdk=true` forces it on another host.
3. Developer Portal (developer.crazygames.com): create the game, upload the build folder, tick the Progress Save toggle (without it the Data
   module is off), set Unity as the engine, and open the preview (crazygames.com/preview) for the realistic check: gameplay events, an ad
   after a won battle, the save across a reload and a login.

## Status

Wrapper, call sites, storage and tests written; code compiled against the project's assemblies (editor was open, so no EditMode run yet).
Left: run EditMode tests (`SdfPlatform.Tests`, `LayeredStorageTests`), a WebGL build, a run in the preview tool, the portal checklist.
