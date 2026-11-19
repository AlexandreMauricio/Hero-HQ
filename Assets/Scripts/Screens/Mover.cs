using UnityEngine;

namespace HeroHQ.Screens
{
    /// <summary>
    /// Moves or shakes one box on the screen, then stops (P2b-04; 02 GDD 17.4 and 17.6; 03 Architecture 8.1 A4): a turn-bar
    /// entry sliding to its new place, a hero stepping forward, a covered hero shaking, a knocked-out box sliding off, the
    /// three buttons sliding in and out. The same shape as <see cref="Floater"/>: a MonoBehaviour (a script Unity runs on an
    /// object each frame: see the Glossary) because something has to count the time, and it counts <c>Time.deltaTime</c>,
    /// which stops when the game is paused (reading R33).
    ///
    /// A <b>slide</b> (<see cref="MoveTo"/>) goes from where the box is now to a place, eased at both ends. A new slide on a
    /// box that is still sliding starts from wherever the box is, and the box ends at the newest place. A <b>shake</b>
    /// (<see cref="Shake"/>) wobbles the box left and right around its place and is over by itself; it adds to a slide
    /// rather than replacing it, so the box still ends at <see cref="Home"/>. <see cref="Advance"/> is public so a test can
    /// move time without a frame.
    /// </summary>
    public sealed class Mover : MonoBehaviour
    {
        RectTransform rect;
        Vector2 basePosition;   // where the slide has got to, without any shake
        Vector2 home;           // where the box ends up
        Vector2 from;
        float slideAge, slideSeconds;
        bool sliding, vanish;
        float startAlpha;
        float shakeAge, shakeSeconds, shakeSize;
        bool shaking;

        /// <summary>Where the box ends up when everything has finished.</summary>
        public Vector2 Home
        {
            get
            {
                Prepare();
                return home;
            }
        }

        /// <summary>A slide or a shake is still going.</summary>
        public bool Moving => sliding || shaking;

        void Prepare()
        {
            if (rect != null) return;
            rect = (RectTransform)transform;
            basePosition = home = rect.anchoredPosition;
        }

        /// <summary>
        /// Slides the box to <paramref name="target"/> (a place in its parent, as <c>anchoredPosition</c>) in
        /// <paramref name="seconds"/>. With <paramref name="vanish"/> it also fades out over the slide and then destroys
        /// itself: a box that has left for good.
        /// </summary>
        public void MoveTo(Vector2 target, float seconds, bool vanish = false)
        {
            Prepare();
            from = basePosition;
            home = target;
            slideAge = 0;
            slideSeconds = seconds;
            sliding = true;
            this.vanish = vanish;
            if (vanish) startAlpha = Group().alpha;
            if (seconds <= 0) Advance(0);   // nothing to wait for: it is there now
        }

        /// <summary>The box is at <paramref name="target"/> now, with nothing going on: no slide, no shake.</summary>
        public void SnapTo(Vector2 target)
        {
            Prepare();
            basePosition = home = target;
            sliding = shaking = vanish = false;
            rect.anchoredPosition = target;
        }

        /// <summary>A left-and-right wobble of up to <paramref name="size"/> reference pixels, three swings, dying away.</summary>
        public void Shake(float seconds, float size)
        {
            Prepare();
            shaking = true;
            shakeAge = 0;
            shakeSeconds = seconds;
            shakeSize = size;
        }

        /// <summary>Where a slide of the given progress (0 to 1) has got to, eased at both ends.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        void Update()
        {
            if (Moving) Advance(Time.deltaTime);
        }

        /// <summary>Lets <paramref name="seconds"/> of game time pass for this box.</summary>
        public void Advance(float seconds)
        {
            Prepare();
            var finishedVanish = false;
            if (sliding)
            {
                slideAge += seconds;
                var t = slideSeconds > 0 ? Mathf.Clamp01(slideAge / slideSeconds) : 1;
                basePosition = Vector2.Lerp(from, home, Ease(t));
                if (vanish) Group().alpha = startAlpha * (1 - Ease(t));
                if (t >= 1)
                {
                    sliding = false;
                    basePosition = home;
                    finishedVanish = vanish;
                }
            }

            var wobble = Vector2.zero;
            if (shaking)
            {
                shakeAge += seconds;
                var t = shakeSeconds > 0 ? Mathf.Clamp01(shakeAge / shakeSeconds) : 1;
                if (t >= 1) shaking = false;
                else wobble = new Vector2(shakeSize * Mathf.Sin(t * 2 * Mathf.PI * 3) * (1 - t), 0);
            }

            rect.anchoredPosition = basePosition + wobble;
            if (finishedVanish) Remove(gameObject);
        }

        CanvasGroup Group()
        {
            var group = GetComponent<CanvasGroup>();
            return group != null ? group : gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>Takes a box away at once (in the editor's edit mode too, where Unity can't wait for the frame's end).</summary>
        internal static void Remove(GameObject box)
        {
            if (Application.isPlaying)
            {
                box.transform.SetParent(null, false);
                Destroy(box);
            }
            else DestroyImmediate(box);
        }
    }
}
