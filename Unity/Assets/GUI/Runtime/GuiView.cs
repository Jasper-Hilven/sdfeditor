using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// A part of the GUI drawn from the <see cref="FakeMatch"/> it is under (or the scene's): it only reads the match, every frame,
    /// in the editor as well as at runtime, so the scene shows what the game will. Without a match (a prefab opened on its own) it
    /// leaves what it shows as it is.
    /// </summary>
    [ExecuteAlways]
    public abstract class GuiView : MonoBehaviour
    {
        private FakeMatch match;

        protected FakeMatch Match
        {
            get
            {
                if (match == null)
                    match = GetComponentInParent<FakeMatch>(true);
                if (match == null)
                    match = FindAnyObjectByType<FakeMatch>(FindObjectsInactive.Include);
                return match;
            }
        }

        protected virtual void OnEnable() => Refresh();

        protected virtual void Update() => Refresh();

        public void Refresh()
        {
            var m = Match;
            if (m != null)
                Render(m);
        }

        protected abstract void Render(FakeMatch m);
    }
}
