using UnityEngine;

namespace HeroHQ.Screens
{
    /// <summary>
    /// A short-lived thing on screen (a floating number, the banner, the off-look flash): it lives for
    /// <see cref="seconds"/>, drifts up by <see cref="rise"/> reference pixels while it does, fades out if
    /// <see cref="fade"/> is on, then destroys itself. A MonoBehaviour (a C# script Unity runs on an object each frame:
    /// see the Glossary) because something has to count the time; everything else on the screen is plain C#.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class Floater : MonoBehaviour
    {
        public float seconds = 1f;
        public float rise;
        public bool fade = true;

        float age;
        Vector2 start;
        RectTransform rect;
        CanvasGroup group;

        void Update()
        {
            if (rect == null)
            {
                rect = (RectTransform)transform;
                group = GetComponent<CanvasGroup>();
                start = rect.anchoredPosition;
            }
            age += Time.deltaTime;
            var t = seconds > 0 ? Mathf.Clamp01(age / seconds) : 1;
            rect.anchoredPosition = start + new Vector2(0, rise * t);
            if (fade) group.alpha = 1 - t * t;       // stays readable most of the time, then goes quickly
            if (age >= seconds) Destroy(gameObject);
        }
    }
}
