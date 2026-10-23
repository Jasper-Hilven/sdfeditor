# CC0 audio sources

All files here are CC0 1.0 (public domain dedication): https://creativecommons.org/publicdomain/zero/1.0/
Kenney packs carry a License.txt stating CC0; OpenGameArt pages list the licence as CC0 on the page itself.
Credit is not required (Kenney asks politely for a mention).

Files were re-encoded to stereo Vorbis .ogg (the local ffmpeg has no libvorbis; its native encoder is stereo-only, so
sources are duplicated to two channels; set "Force To Mono" in the Unity import settings). Some were trimmed or
faded out, noted below. Source pack zips: kenney.nl/media/pages/assets/<pack>/... (links on each pack page).

Packs (Kenney, CC0, https://kenney.nl):
- Sci-Fi Sounds: https://kenney.nl/assets/sci-fi-sounds
- Digital Audio: https://kenney.nl/assets/digital-audio
- Interface Sounds: https://kenney.nl/assets/interface-sounds
- RPG Audio: https://kenney.nl/assets/rpg-audio
- Music Jingles: https://kenney.nl/assets/music-jingles

| File | Use | Original file | Source URL | Licence |
|---|---|---|---|---|
| weapons/flamethrower_start.ogg | flamethrower ignition | ignition.flac ("Flare ignition", qubodup) | https://opengameart.org/content/flare-ignition | CC0 |
| weapons/flamethrower_loop.ogg | flamethrower loop (5 s, thruster flame roar) | Sci-Fi Sounds / thrusterFire_001.ogg | https://kenney.nl/assets/sci-fi-sounds | CC0 |
| weapons/flamethrower_stop.ogg | flamethrower stop (0.5 s cut of thrusterFire_001 with fade-out) | Sci-Fi Sounds / thrusterFire_001.ogg | https://kenney.nl/assets/sci-fi-sounds | CC0 |
| weapons/electro_lightning_shot.wav | electro-gun shot (0.37 s): spark.wav snap crossfaded into continuousspark.wav | BMacZero, spark.wav + continuousspark.wav ("Electricity Sound Effects", recorded from a small Tesla generator) | https://opengameart.org/content/electricity-sound-effects-0 | CC0 |
| weapons/electro_lightning_shot_b.wav | electro-gun shot variant (0.54 s): same recordings, pitched down 0.8-0.9, three layers | BMacZero, spark.wav + continuousspark.wav | https://opengameart.org/content/electricity-sound-effects-0 | CC0 |
| weapons/electro_lightning_chain.wav | electro-gun chain crackle (0.23 s) | BMacZero, continuousspark.wav (pitch 0.95) | https://opengameart.org/content/electricity-sound-effects-0 | CC0 |
| weapons/poison_spray.ogg | poison-gun hiss (first 0.9 s, faded) | "steam hisses - Marker #1.wav" ("Steam release sounds", bart) | https://opengameart.org/content/steam-release-sounds | CC0 |
| weapons/poison_spray_b.ogg | poison-gun hiss variant | "steam hisses - Marker #3.wav" (same pack) | https://opengameart.org/content/steam-release-sounds | CC0 |
| weapons/blob_shot.ogg | blob-gun goopy shot | squish_02.mp3 ("Squish Sounds Effects", ezduzziteh) | https://opengameart.org/content/squish-sounds-effects | CC0 |
| weapons/blob_shot_b.ogg | blob-gun shot variant | squish_06.mp3 (same pack) | https://opengameart.org/content/squish-sounds-effects | CC0 |
| weapons/blob_splat_impact.ogg | blob splat impact | squishsplat_impact.mp3 (same pack) | https://opengameart.org/content/squish-sounds-effects | CC0 |
| weapons/blob_splat_impact_b.ogg | blob impact variant | Sci-Fi Sounds / slime_000.ogg | https://kenney.nl/assets/sci-fi-sounds | CC0 |
| vehicles/tank_cannon_shot.ogg | tank cannon (first 2 s, faded) | cannon_fire_0.ogg ("Cannon fire", thimras) | https://opengameart.org/content/cannon-fire | CC0 |
| vehicles/turret_shot.ogg | turret shot | Sci-Fi Sounds / laserSmall_001.ogg | https://kenney.nl/assets/sci-fi-sounds | CC0 |
| spores/mycelium_rush_whoosh.ogg | Mycelium Rush whoosh (first 1.6 s, faded) | whoosh2_0.wav ("Air whoosh", pyranostudios) | https://opengameart.org/content/air-whoosh | CC0 |
| spores/regrowth_heal_chime.ogg | Regrowth chime | Digital Audio / powerUp3.ogg | https://kenney.nl/assets/digital-audio | CC0 |
| spores/puffball_armour_puff.ogg | Puffball Armour shield-up | Sci-Fi Sounds / forceField_000.ogg | https://kenney.nl/assets/sci-fi-sounds | CC0 |
| spores/spore_meter_ready.ogg | spore meter full chime | Interface Sounds / confirmation_002.ogg | https://kenney.nl/assets/interface-sounds | CC0 |
| player/land.ogg | land | jumpland.wav ("Jump Landing Sound", qubodup) | https://opengameart.org/content/jump-landing-sound | CC0 |
| player/hit_marker_tick.ogg | hit marker tick | Interface Sounds / tick_004.ogg | https://kenney.nl/assets/interface-sounds | CC0 |
| player/hurt.ogg | you get hit | hurt_02.mp3 ("Hurt Sound Effects", ezduzziteh) | https://opengameart.org/content/hurt-sound-effects | CC0 |
| player/respawn.ogg | respawn (first 1.2 s, faded) | powerup.wav ("Electricity Game Sound Pack", faxcorp) | https://opengameart.org/content/electricity-game-sound-pack | CC0 |
| flag/flag_pickup.wav | flag pickup | Interface Sounds / confirmation_001.ogg | https://kenney.nl/assets/interface-sounds | CC0 |
| flag/flag_dropped.ogg | flag dropped | Digital Audio / lowDown.ogg | https://kenney.nl/assets/digital-audio | CC0 |
| flag/flag_returned.ogg | flag returned | Digital Audio / highUp.ogg | https://kenney.nl/assets/digital-audio | CC0 |
| flag/flag_capture_jingle.ogg | flag captured jingle | Music Jingles / Steel jingles/jingles_STEEL01.ogg | https://kenney.nl/assets/music-jingles | CC0 |

Kenney licence statement (License.txt inside each pack): http://creativecommons.org/publicdomain/zero/1.0/

The three electro_lightning files were later made lower and fuller: pitched to 0.78, with an octave-down layer low-passed at 900 Hz
underneath, compressed and limited to -1.5 dBFS (shot 0.74 s, shot_b 1.08 s, chain 0.46 s).
