# 65 - The CrazyGames username is your name

## What

Before: your name was always `Cast.You` ("Nightowl42"), or the campaign setup's; nothing read the portal's user. Now, when the player is
signed in on CrazyGames, their username is their name in the match: scoreboard row, kill feed, death screen ("by ..."), end card, loading
rosters, name tags. Signed out (or no SDK: desktop build, editor batch mode) the name is what it was.

## Decisions

- **Only through `WebPortal`**: `WebPortal.UserName` (raw username, null when signed out or no SDK) and the `UserChanged` event. After the SDK
  is up it reads `CrazySDK.User.GetUser` once and registers `AddAuthListener` (the SDK ignores the listener in the editor, whose simulated user
  `CrazyGamesSettings.getUserResponse` is read by `GetUser` only). The login prompt is never called; no new buttons.
- **`SdfPlatform.PortalUserName.Clean(raw, maxLength)`** (pure, `Platform/Core`, tested): drops every character the GUI fonts lack (Fredoka and
  Lilita One share basic Latin and Latin-1; emoji, CJK, symbols go), the rich-text characters `< > { } " \ | / ; $ % ^ `` ` ``, collapses
  spaces, cuts to `PlayerNames.MaxLength` (16, the length every bot name and every HUD row is laid out for, ticket 54/64's fit) and
  trims; a name with no letter or digit left counts as signed out. CrazyGames usernames are assumed up to about 25 characters; they are cut
  to 16, not shrunk, so nothing can overflow what bots' names already fit.
- **`Match.MakePlayers`**: your name is the cleaned portal name, else the setup's/cast's. Every reader (HUD, kill feed, tags, end card) reads
  `PlayerState.Name`, so one place decides.
- **A bot never has your name**: yours is reserved in the `NameCast` (by `PlayerNames.Key`, so case, spaces and underscores do not
  help); a setup-given bot name that clashes, or one that clashes after a mid-match sign-in, gets a fresh draw.
- **Sign-in or out mid-match**: `Match` listens to `WebPortal.UserChanged` and sets `PlayerState.Name` at once (the HUD reads it every
  frame); past kill-feed lines keep the name they had.

## Done when

- EditMode tests: the name rule (signed out, signed in, too long, odd characters, rich text) and a bot never equal to you.
- Game-loop play test and the CrazyGames build pass.
- A release build served on `http://localhost` in Chrome with the SDK's simulated user shows that name on the scoreboard.

## Status

Code done. Not checked by hand on the live portal (the account is only available on CrazyGames domains).
