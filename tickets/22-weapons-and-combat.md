# 22 — Weapons and combat

Health, damage, the inventory and the seven weapons.

Depends on: 20 (players), 16/17 (the pods); 21 for dropping the flag on death.

## Decisions (from the user)

- **4 weapon slots**, and a scene holds at most 4 kinds of weapon: the pistol, the minigun, its long-range pod (sniper or
  electro-gun) and its short-range pod (flamethrower, poison-gun or blob-gun). So each kind has its slot and slots never run over.
- Everyone starts with a **single pistol**. Picking up a pistol pod makes it **dual pistols** (same slot).
- **Unlimited ammo** (no reloading; a weapon may still have a cooldown or overheat as part of its feel).
- On death you **lose everything** except the single pistol.
- Walking onto a pod whose weapon you already hold does not take it (it stays for others).
- Every weapon is **good at a specific range or situation**; the single pistol is **weaker than all the rest**, since nobody has to
  walk to a pod for it.
- Health 100; the health pod gives 40 points: one health a point up to 100, half a health a point above 100, up to 200 (80 → 110,
  100 → 120; changed from "heals 50, up to 100"). Friendly fire off. Spawn protection (ticket 20).

## First balance (to tune in play)

| Weapon | Good for | Fire | Damage | Range / notes |
|---|---|---|---|---|
| Pistol (single) | nothing in particular: the fallback | hitscan, 2.5 shots/s | 12 | falls off past 20 m |
| Dual pistols | close to mid fights | hitscan, 5 shots/s | 12 each | falls off past 20 m, a little spread |
| Minigun | mid range, holding a choke point | hitscan, 0.6 s spin-up, then 14 shots/s for at most 3 s, then a 0.6 s spin-down (user, after the first table) | 7 | spread grows the longer it fires; 15-35 m best |
| Sniper | long range, picking off the carrier | hitscan, 1 shot / 1.4 s | 85, head 200 | right mouse zooms; unzoomed wide spread |
| Electro-gun | long range, groups | hitscan bolt, 1.2 shots/s | 40 | 60 m; jumps to a second enemy within 6 m for 20 |
| Flamethrower | very close, rooms and stairs | cone, continuous | 70/s | 8 m; burning 10/s for 2 s after |
| Poison-gun | area denial, defending the flag | lobbed cloud | 18/s in the cloud | 3 m cloud for 5 s; arcs up to 30 m; afterpoison and a slow for 10 s after the cloud (user, after the first table) |
| Blob-gun | stopping the flag carrier | lobbed blob | 35 | hit slows 40% for 2 s; a splat on the ground slows who walks in |

The numbers live in one data asset (per weapon ScriptableObject or a table), so tuning is not a code change.

## Unity

- `Health` on every player: damage with the attacker and weapon, death (raises the event ticket 20/21 use: drop the flag, start the
  5 s respawn, kill credit), healing by `HealthPickup`.
- `Inventory`: the four slots, 1-4 and the scroll wheel to switch, the dual-pistol state; filled by `WeaponPickup`'s event.
- Weapons fire from the camera for you and from the eyes for bots; hitscan by raycast against player capsules and the level's
  colliders; projectiles/areas as simple objects. The showcase effects (`TracerGun`, `LightningGun`, `GoopGun`, `FxParticles`) are
  reused for the look; a flame cone and poison cloud are made in the same way.
- A first-person view model per weapon (the weapon's prefab, in the camera's corner; two for dual pistols) and the weapon in the
  scout's hand for other players.
- HUD: health bar, the four slots with the current one marked, hit marker, damage direction, kill feed ("Red 2 [sniper] Blue 3").

## Done when

EditMode tests for health, inventory (start, pickup, dual pistols, death keeps only the single pistol) and the damage falloffs. In
Play mode: you can pick up each weapon, switch, damage and kill an idle bot, heal at the health pod, die and respawn with a single
pistol.

## Status

**Done in code; left: the Unity side checked in the open editor** (the scripts compile, the weapon table asset appears, the EditMode
tests run in the Test Runner, the Play-mode check of "Done when"). Built:

- **Rules in plain C#** (`Assets/Battle/Core`, `SdfBattle.Core`): `Weapons.cs` (`WeaponSlot` pistol / minigun / long range / short
  range, `WeaponIds` and their slots, `FireMode` hitscan / chain / cone / cloud / blob, `WeaponStats`: one weapon's numbers with
  `DamageAt` (falloff), `SpreadAfter` (growth while firing, the zoomed spread), `WeaponTable` with `Default()`, the balance below),
  `Inventory` (4 slots, a single pistol to start, a pistol pod makes dual pistols in the same slot, a held weapon is not taken again,
  1-4 and scroll, `Reset` on death keeps only the single pistol), `Damage` (amount, weapon, attacker player or a machine's name and
  team, where from, headshot, over time; `ToDeath`), `Health` (hit points of anything that is not a player: ticket 24's machines),
  `WeaponTrigger` (draw time, rate, the minigun's spin-up, its 3 s burst and 0.6 s spin-down, spread while firing), `CombatState` (every player's inventory; `Hurt`
  through `MatchState.CanHurt`, so friendly fire off, spawn protection and the countdown hold; death at 0 to `ReportDeath` with
  killer, weapon and headshot; `Heal` with the pod's points (one for one to 100, half above, up to 200: `HealthAfter`) and `NeedsHealth` (under 200); the flamethrower's burn and the cloud's afterpoison, which wait while you stand in the
  flame and burns on after; the blob's slow; poison clouds and splats as `Area`s, acting every 0.25 s, clouds not adding up; a new
  life starts with a single pistol; `CanHurt(team, team)` for machines), `Shots.HitEllipsoid` (the head). `Death` gained `Headshot`.
- **EditMode tests** (`Assets/Battle/Tests`): `InventoryTests` (11), `CombatStateTests` (31), `WeaponTableTests` (19: every weapon's
  numbers and slot, the falloffs, dual pistols, the minigun's spread, the sniper's zoom and 120 headshot, the electro-gun's 2 jumps, the
  flame, the lobbed guns' reach, the single pistol weaker than every gun that hits directly, the head, the trigger's timing). All 112
  battle tests (with 20's and 21's) pass outside Unity against the compiled sources (net8 + NUnit, Unity's CoreModule).
- **Unity** (`Assets/Battle/Runtime`): `Combat` (made by `Match.Start` after the flag rules and before the first round, on the Battle
  object): the rules, an `Arsenal` per player, and the hits: hitscan rays from the eye against capsules and the level (triggers
  passed through) and each player's head, the scout's cap tested as an ellipsoid (0.6 m round, 0.22 high, its middle 1.41 m over the
  feet) without a collider of its own, so it never meets a flag or pod trigger; a capsule hit above 1.2 m also counts as the head; the
  bolt's jumps on to up to 2 more enemies, each the nearest not hit yet within 6 m of the last and in sight; the flame's cone (enemies in reach and in sight, 10 ticks a second);
  the lobbed poison ball and blob stepped with gravity (a swept ball of 12 cm), the cloud where the ball lands, the blob's hit and
  slow and its splat on the ground below. The pods ask `WeaponPickup.CanPickUp` / `HealthPickup.CanPickUp` first (new static gates):
  a held weapon or 200 health leaves the pod standing; taking one fills the slot (and takes it in hand) or heals by the table's
  `healthPack` (40 points; the pod prefab's own `amount` of 50 is not used). The slow is
  `FirstPersonController.slowMultiplier` (new, beside 21's `speedMultiplier`). Machines (24): an `IDamageable` behind their
  colliders (team, alive, centre, `TakeDamage`), `Combat.Register` for the jump and the cone, `Combat.Hurt(player, Damage.ByMachine(..))`
  to shoot players, `Combat.Trace` and `CombatFx.Tracer(machine, "pistol", muzzle, ...)` for their shots.
  `Arsenal`: you by mouse and keys (left fires while the pointer is locked, right zooms the sniper to 20 degrees with a slower
  mouse, 1-4, the scroll wheel), a bot through `SetTrigger` / `SetZoom` / `Select`; shots from the camera for you, from the midpoint
  of the scout's floating eyes for a bot. `WeaponView`: the weapon prefab at 0.3 scale in the camera's corner (so it stays inside the
  capsule and never reaches into a wall; no shadow; two for dual pistols, firing in turn), kicking back, the minigun shivering while
  it spins, hidden zoomed; for the others the weapon in the scout's right hand (both hands for dual pistols) pointing along the aim.
  Every weapon model is made at the start, hidden. `CombatFx`: ticket 8's `FxBuffer` (one draw) and `FxParticles`, a `TracerGun` per
  shooter and bullet weapon in its colours, two `LightningGun`s per shooter (bolt and jump); `TracerGun.ShootAt` and
  `LightningGun.Strike` added so they draw the hits found here; the flame (glowing motes and smoke along the aim, stopped by walls),
  the poison cloud (toon balls low over the ground, rising motes) and ball, the blob's goop models pooled (12 gobs, 16 splats).
  `CombatHud`: the hit marker (red on a kill, faint for your burns and clouds), the damage direction and red, the "eliminated" /
  "cap popped" pop-up, the crosshair's spread, and the spores panel turned into the four slots (icons, the one in hand gold and
  bigger, "1 x2" for dual pistols, the weapon's name); all on the runtime GUI, the prefab untouched. Kill feed: `MatchHud` now also
  lists a machine's kills (its name in its team's colour, `FakeKill.killerName`) and headshots; weapons without an icon show none.
- **The data asset**: `WeaponTableAsset` at `Assets/Battle/Resources/Weapons.asset` (the table, the looks: model, muzzle and grip
  measured from the .sdf files, first-person placement, tracer colours; the effect material; the goop models), made with the first
  balance by `Assets/Battle/Editor/WeaponTableSetup.cs` when missing and filled in when the models are built; Tools > SDF Battle >
  Select / Reset Weapon Table. Tuning is a change to the asset; without it the game plays the first balance with no models.

The balance as built (all in the asset):

| Weapon | Slot | Fire | Damage | Range, falloff, spread | Notes |
|---|---|---|---|---|---|
| Pistol (single) | 1 | hitscan, 2.5/s | 12 | full to 20 m, 6 from 40 m; 0.4 deg | 30/s: the weakest, everyone's start |
| Dual pistols | 1 | hitscan, 5/s, the two in turn | 12 | as the pistol; 1.2 deg | after a pistol pod |
| Minigun | 2 | 0.6 s spin-up, then 14/s for 3 s at most, then 0.6 s spin-down (also when let go; the 3 s come back once it stands) | 7 | full to 35 m, 2.8 from 60 m; 0.8 deg growing 1.5 deg/s to 4.5 | draw 0.4 s; the HUD shows the seconds left and "spinning down" |
| Sniper | 3 | 1 shot / 1.4 s | 85, head 120 | no falloff, 250 m; 5 deg unzoomed, 0 zoomed (right mouse, 20 deg view) | draw 0.4 s |
| Electro-gun | 3 | hitscan bolt, 1.2/s | 40 | 60 m; 0.2 deg | jumps on to 2 more enemies at most, each the nearest not hit yet within 6 m of the last, in sight, for 20 each |
| Flamethrower | 4 | cone 14 deg each side, 10 ticks/s | 70/s | 8 m, stopped by walls | burning 10/s for 2 s after leaving the flame |
| Poison-gun | 4 | lobbed ball, 0.8/s, 17 m/s, 8 deg over the aim | 18/s in the cloud | about 30 m at 45 deg; cloud 3 m for 5 s | clouds do not add up; after leaving: afterpoison 3/s for 10 s and 25% slower for 10 s |
| Blob-gun | 4 | lobbed blob, 1.25/s, 22 m/s, 5 deg over the aim, gravity 12 | 35 | about 40 m; 1 deg | a hit slows 40% for 2 s; a 1.2 m splat for 5 s slows enemies 40% in it |

Damage a second (full damage, every shot landing; "optimistic" counts every enemy it can reach at once, with 4 enemy players):

| Weapon | One enemy | Optimistic, all enemies | How |
|---|---|---|---|
| Pistol | 30 (15 past 40 m) | 30 | one target |
| Dual pistols | 60 (30 past 40 m) | 60 | one target |
| Minigun | 98 while firing (39 past 60 m); 70 kept up (3 s of every 4.2 s: spin-down and spin-up); 294 a burst | 98 | one target |
| Sniper | 61 body, 86 head | 86 | one target, all headshots |
| Electro-gun | 48 | 96 | 40 + 20 + 20 over three enemies, 1.2 a second |
| Flamethrower | 70, then 20 burn over 2 s | 280 | all 4 in the cone, each burning after |
| Poison-gun | 18 in the cloud, then 3 for 10 s (30 more) | 72, then 12 | all 4 in one cloud (a player takes one cloud at a time), each poisoned and 25% slower for 10 s after |
| Blob-gun | 44 | 44 | one target; the splat only slows |

All shots: health 100 at each spawn, the health pod 40 points (one for one to 100, half above, up to 200; overheal stays until
  damaged; not taken at 200; `healthPack` and `overheal` in the asset), friendly fire off, nothing in the countdown or
through spawn protection (firing ends it), switching takes the draw time (0.25 s unless noted). Unlimited ammo, no reload.

Left (in Unity):

- Let it compile; check `Assets/Battle/Resources/Weapons.asset` appears with the seven models, Effect.mat and the goop models filled
  in (else Tools > SDF Battle > Select Weapon Table once the Mushrooms prefabs exist). Window > General > Test Runner > EditMode >
  Run All.
- The scenes need ticket 20's rebuild (Tools > SDF Level > Rebuild) for the pods and the match; combat itself adds nothing to them.
- Play mode on `Fungal.unity` ("Done when"): click to lock the pointer, fire the pistol at an idle bot (hit marker, kill feed, its
  respawn after 5 s), walk onto the minigun, pistol (dual), long- and short-range pods and switch with 1-4 and the scroll wheel
  (slots panel), try each weapon (the minigun's spin-up, the sniper's zoom and a headshot, the bolt jumping across three bots, the flame
  and burn, the cloud, the blob's slow on a bot and its splat), get hurt (F9, editor and development builds, or the Combat
  component's context menu "Test: Hurt You 30": 30 from in front, as the bots do not shoot yet) and heal at the health pod (from 80 it
  takes you to 110; the bar's number shows health over 100), die (F9 four times, or F8 off the island) and respawn with a single pistol. Look at the view models' placement
  (`viewGrip`, `viewScale` in the asset) and the weapons in the bots' hands (`handScale`).
- Not done: damage to you from bots needs 23's brains (they fire through `Arsenal.SetTrigger`); a machine's killer name on the death
  screen (it shows "the island" there, the kill feed names it) is 24's to add.
