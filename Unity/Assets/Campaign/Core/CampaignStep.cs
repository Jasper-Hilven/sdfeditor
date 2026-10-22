namespace SdfCampaign
{
    public enum StepKind { Cutscene, Battle, Credits }

    /// <summary>
    /// One step of the campaign (ticket 32, section 5). Ids are stable so steps can be inserted later; a later level is added by
    /// adding a step (and its scene), nothing else.
    /// </summary>
    public sealed class CampaignStep
    {
        public readonly string id;
        public readonly StepKind kind;
        /// <summary>The scene to load (a beat plays in the next battle's scene).</summary>
        public readonly string scene;
        public readonly string title;
        /// <summary>The battle's numbers; null for a cutscene or the credits.</summary>
        public readonly BattleSetup setup;

        public CampaignStep(string id, StepKind kind, string scene, string title, BattleSetup setup = null)
        {
            this.id = id;
            this.kind = kind;
            this.scene = scene;
            this.title = title;
            this.setup = setup;
        }

        public bool IsBattle => kind == StepKind.Battle;
    }
}
