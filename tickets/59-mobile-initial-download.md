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

### Part 1 (splash and size audit)

**Splash.** `m_ShowUnitySplashScreen: 0` and `m_ShowUnitySplashLogo: 0` in `ProjectSettings.asset` (Show Splash Screen and Show Unity Logo both
off), and `WebBuild.Run` sets `PlayerSettings.SplashScreen.show = false` / `showUnityLogo = false` on every build, so neither can come back
from the settings window. Checked in the build report: the player no longer carries `Built-in Texture2D: Splash Screen Unity Logo` (2.7 MB
uncompressed, the largest single item), and a CrazyGames build in headless Chrome (served on `localhost`) goes from the page to the Fungal match
without a Unity splash. The WebGL page template (`index.html`, "APPLICATION:Default") still shows its own small Unity logo in the loading bar and
footer; that is the page, not the player splash, and is left (a custom template would remove it).

**Sizes** (MiB; CrazyGames build, Brotli, bytes of `Build/*.br` and of `StreamingAssets/Bundles`)

| | before | after | change |
|---|---|---|---|
| `Web.data.br` | 9.93 | 9.26 | -0.67 (splash texture) |
| `Web.wasm.br` | 9.29 | 7.05 | -2.24 (managed stripping High, IL2CPP Optimize Size) |
| framework + loader | 0.10 | 0.10 | |
| **Player** | **19.33** | **16.41** | **-2.92** |
| Bundles (27 files) | 41.8 | 42.0 | +0.2 (other tickets' GUI font atlases, not this one) |
| Total upload | 61.1 | 58.4 | -2.7 |
| Player + Fungal's bundles (first play, desktop) | 32.4 | 29.7 | -2.7 |
| Player + Bathroom's bundles (smallest level) | 28.0 | 25.3 | -2.7 |

Still over 20 MB for touch: the player alone is 16.4, so a first level at 8.9 MB (Bathroom) is 25.3. Part 2 (what the first match needs in the
player, a lighter first level) is the rest.

**Changed**: managed stripping level High for WebGL (was the default), IL2CPP code generation "Optimize Size" for WebGL (was "Faster runtime"),
splash and Unity logo off. The generated `Campaign/link.xml` (StreamedBundles) already keeps what the bundled scenes' components use, so High
needed no extra preserves. Tried and reverted: `StripUnusedMeshComponents` (Optimize Mesh Data) on, which saved 0.2 MB of 43 and risks dropping
vertex data a runtime material needs. Optimize Size makes the code a little slower; ticket 58's measurements on a phone should confirm it is
acceptable (the wasm is 2.2 MB smaller, a quarter of its size).

**What is big in the player** (uncompressed from the build report; after the change)

| Item | Size | What uses it |
|---|---|---|
| Meshes | 9.0 MB | soldier body, 12 heads x2, weapons and their LODs (loaded from Resources) |
| Textures | 7.9 MB (was 10.6) | GUI icons, URP's 10 film grain textures (2.5 MB), SMAA AreaTex 0.26, TMP EmojiOne 0.34 |
| Sounds | 1.7 MB | `music_lvl_02_orchestra_layer_01.ogg` 0.9, UI and weapon sounds (already Vorbis and mono) |
| Fonts | 1.0 + 0.34 MB | TMP's default `LiberationSans SDF` and its ttf; the GUI's Fredoka atlas is another 1 MB (and 1 MB for Lilita One) |
| Shaders | 1.2 MB | `SdfToon` 0.46, URP post-processing (UberPost 0.2) |
| Animations | 1.0 MB | soldier clips |
| wasm | 7.05 MB Brotli | IL2CPP code of the game, URP, TMP, UGUI, Input System, CrazySDK, the engine modules the scenes use (AI, Audio, Physics, UI, TextRendering kept whole by the link.xml) |

**What is big in the bundles** (42 MB, LZ4; measured from the bundle files by object type, uncompressed -> LZ4)

| Type | Uncompressed | LZ4 | What |
|---|---|---|---|
| Mesh | 65.7 MB | 28.4 MB | level props (generated `_body` meshes), 2/3 of the bundles |
| Texture2D | 30.4 MB | 2.0 MB | the `regions` masks (2.7 to 5 MB each uncompressed, 0.15 to 0.6 compressed), two font atlases 1 MB |
| NavMeshData | 2.6 MB | 1.1 MB | per level scout navmesh |
| Cubemap | 3.7 MB | 0.9 MB | `Default-Skybox-Cubemap`, one copy in each of 7 scene bundles |
| MonoBehaviour, GameObject, Transform, Renderer | 6.2 MB | 3.1 MB | scene objects |

Not changed (needs a decision, or belongs to another ticket): the level meshes (no re-meshing; mesh compression or vertex quantization would
change them); URP's film grain textures (referenced by `UniversalRenderPipelineGlobalSettings.asset`, ticket 58's file: if film grain is
unused on the web quality levels, dropping its entries saves about 2.5 MB uncompressed, perhaps 1 MB compressed); TMP Essentials' default font
(`LiberationSans SDF`, ttf, EmojiOne, about 1.7 MB uncompressed) only if no text falls back to it (ticket 57's GUI fonts; needs a run of every
screen); `Lilita One SDF Atlas` (1 MB in a shared bundle) if the GUI no longer uses it; `Test Panel.prefab` (0.14 MB) is in the shared bundles
from the GUI folder; the one cubemap copy per level; NavMeshSurface/Modifier icon textures (0.35 MB each) pulled in by the components; music
`music_lvl_02_orchestra_layer_01.ogg` is in the player and in a bundle (1.3 MB twice).

**Checks**: EditMode 777 pass, 0 fail. Desktop play test: five matches, 0 errors. Touch play test: 0 errors (one run failed "waiting for the jump"
while another agent's Unity build used every core, the next two passed). Release web build (CrazyGames, stripping High) in headless Chrome on
`http://localhost:...`: loads to the Fungal match (HUD, bots, pistol, tutorial prompt), bundles fetched, next level prefetched, no exceptions;
console has the three unsupported hidden-shader lines of SwiftShader (also in the build before this change) and the synthetic click's pointer-lock error.
Note for other browser checks: CrazySDK's SiteLock only allows `localhost`, so a page on `127.0.0.1` stops after the SDK lines and never fetches a bundle.

### Part 1b: duplicate heads

The `MonsterHeadModels` registry (in Resources) listed each head's `fastPrefab` next to its `prefab`, so every `_fast` prefab and its meshes shipped
in the player although nothing at runtime read the field (users: only the registry entry, `MonsterHeadBuilder`, a heads test, and the editor-only
`SpecialEffects` scene, which is not in the build). The field is removed (builder, test updated; `MonsterHeads.FastPrefabPath` stays for the editor
scene). Player asset list: 12 `*_fast_*_head.asset` (1.1 MB), 8 `_fast` eye meshes (0.09 MB) and 12 `_fast.prefab` gone, 1.3 MB uncompressed in all;
no `_fast` asset is left. CrazyGames build, Brotli: `Web.data.br` 9.24 to 9.19 MiB, `Web.wasm.br` 7.04 unchanged, player 16.38 to 16.23 MiB.
No other model (weapons, soldier, machines) has a `_fast` duplicate in the player.
