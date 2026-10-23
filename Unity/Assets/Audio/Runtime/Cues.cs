namespace SdfAudio
{
    /// <summary>
    /// The names of the game's sounds, the keys of the <see cref="SoundBank"/>. Grouped by what plays them. The ones marked "empty" have no
    /// clip yet and are silent until one is put in the cue; the event that plays them is already wired.
    /// </summary>
    public static class Cues
    {
        // ---------------------------------------------------------------- Weapons

        public const string Pistol = "weapon.pistol";
        public const string DualPistols = "weapon.dual-pistols";
        public const string Sniper = "weapon.sniper";
        /// <summary>The minigun's barrels start to spin (when the trigger goes down). Empty.</summary>
        public const string MinigunSpinUp = "weapon.minigun.spin-up";
        /// <summary>The first bursts of a minigun firing, once, before the loop.</summary>
        public const string MinigunHead = "weapon.minigun.head";
        /// <summary>The minigun firing, looped for as long as it fires.</summary>
        public const string MinigunLoop = "weapon.minigun.loop";
        /// <summary>The last bursts when the minigun stops (trigger let go, or overheated).</summary>
        public const string MinigunTail = "weapon.minigun.tail";
        /// <summary>The flamethrower igniting, once when the trigger is pressed and the flame starts (the loop follows it).</summary>
        public const string FlamethrowerStart = "weapon.flamethrower.start";
        /// <summary>The flamethrower burning, looped while the trigger is held.</summary>
        public const string FlamethrowerLoop = "weapon.flamethrower.loop";
        /// <summary>The flame going out.</summary>
        public const string FlamethrowerEnd = "weapon.flamethrower.end";
        public const string ElectroGun = "weapon.electro-gun";
        /// <summary>The bolt jumping on to the next enemy.</summary>
        public const string ElectroChain = "weapon.electro-gun.chain";
        public const string PoisonGun = "weapon.poison-gun";
        /// <summary>The poison ball landing and bursting into a cloud.</summary>
        public const string PoisonImpact = "weapon.poison-gun.impact";
        public const string BlobGun = "weapon.blob-gun";
        /// <summary>The blob landing.</summary>
        public const string BlobImpact = "weapon.blob-gun.impact";
        /// <summary>A shell casing hitting the ground, after a minigun or pistol shot.</summary>
        public const string Casing = "weapon.casing";
        /// <summary>A weapon pod taken.</summary>
        public const string WeaponPickup = "pickup.weapon";
        /// <summary>A health pod taken.</summary>
        public const string HealthPickup = "pickup.health";

        // ---------------------------------------------------------------- Hits, hurt and death

        /// <summary>You hit someone (a hit marker).</summary>
        public const string HitMarker = "hit.marker";
        /// <summary>You hit someone's head.</summary>
        public const string HitMarkerHead = "hit.marker.head";
        /// <summary>You killed someone.</summary>
        public const string HitMarkerKill = "hit.marker.kill";
        /// <summary>You were hurt.</summary>
        public const string Hurt = "player.hurt";
        /// <summary>You died.</summary>
        public const string DeathYou = "player.death";
        /// <summary>A scout bursting into pieces (with gore on), where it stood.</summary>
        public const string DeathGore = "player.death.gore";
        /// <summary>You appear again after a death.</summary>
        public const string Respawn = "player.respawn";
        /// <summary>The low-health heart, looped; louder and faster as health falls.</summary>
        public const string Heartbeat = "player.heartbeat";

        // ---------------------------------------------------------------- Moving

        public const string FootstepGrass = "footstep.grass";
        public const string FootstepWood = "footstep.wood";
        public const string FootstepCarpet = "footstep.carpet";
        /// <summary>A bot's step, in the world and quiet.</summary>
        public const string FootstepEnemy = "footstep.enemy";
        /// <summary>You jump.</summary>
        public const string Jump = "player.jump";
        /// <summary>You land after a jump or a fall.</summary>
        public const string Land = "player.land";

        // ---------------------------------------------------------------- Explosions

        public const string ExplosionSmall = "explosion.small";
        public const string ExplosionMedium = "explosion.medium";
        public const string ExplosionLarge = "explosion.large";
        /// <summary>An explosion heard from far away, instead of the near one.</summary>
        public const string ExplosionDistant = "explosion.distant";
        public const string DebrisSmall = "explosion.debris.small";
        public const string DebrisMedium = "explosion.debris.medium";
        public const string DebrisLarge = "explosion.debris.large";

        // ---------------------------------------------------------------- Machines

        /// <summary>A turret's shot.</summary>
        public const string TurretFire = "machine.turret.fire";
        /// <summary>A tank's shot.</summary>
        public const string TankFire = "machine.tank.fire";
        /// <summary>A helicopter's shot.</summary>
        public const string HelicopterFire = "machine.helicopter.fire";
        /// <summary>A tank's engine, looped on each live tank.</summary>
        public const string TankEngine = "machine.tank.engine";
        /// <summary>A helicopter's rotor, looped on each live helicopter.</summary>
        public const string HelicopterRotor = "machine.helicopter.rotor";

        // ---------------------------------------------------------------- Capture the flag and the match

        /// <summary>A flag taken.</summary>
        public const string FlagPickup = "flag.pickup";
        /// <summary>A flag dropped.</summary>
        public const string FlagDrop = "flag.drop";
        /// <summary>A flag returned home.</summary>
        public const string FlagReturn = "flag.return";
        /// <summary>Your team captures a flag (the sting below plays with it; the applause is for a won match only).</summary>
        public const string FlagCapture = "flag.capture";
        /// <summary>The other team captures a flag.</summary>
        public const string FlagLost = "flag.lost";
        /// <summary>The sting under your team's capture.</summary>
        public const string CaptureSting = "flag.capture.sting";
        /// <summary>The crowd: taken out (no applause at a capture or a victory); silent, kept so an older bank still loads.</summary>
        public const string Applause = "match.applause";
        /// <summary>The sting of a victory.</summary>
        public const string VictorySting = "match.victory.sting";
        /// <summary>The match is lost.</summary>
        public const string Defeat = "match.defeat";
        /// <summary>Sudden death begins.</summary>
        public const string SuddenDeath = "match.sudden-death";

        // ---------------------------------------------------------------- Spores

        /// <summary>The spore meter is full.</summary>
        public const string SporeReady = "spore.ready";
        /// <summary>Mycelium Rush starts.</summary>
        public const string SporeRush = "spore.rush";
        /// <summary>Regrowth starts.</summary>
        public const string SporeRegrowth = "spore.regrowth";
        /// <summary>Puffball Armour starts.</summary>
        public const string SporeArmour = "spore.armour";

        // ---------------------------------------------------------------- The boss

        /// <summary>The boss fight moves to its next phase.</summary>
        public const string BossPhase = "boss.phase";
        /// <summary>Prime's shield drops.</summary>
        public const string BossShieldDown = "boss.shield-down";
        /// <summary>The Prime-copter crashes.</summary>
        public const string BossCrash = "boss.crash";

        // ---------------------------------------------------------------- Menus and music

        public const string UiOkay = "ui.okay";
        public const string UiCancel = "ui.cancel";
        /// <summary>A comic's panel turns: taken out, silent (a comic is read in quiet); kept so an older bank still loads.</summary>
        public const string ComicPanel = "comic.panel";
        /// <summary>The menus' music (its first clip, looped): the battle music's orchestral layer 1.</summary>
        public const string MusicMenu = "music.menu";
    }
}
