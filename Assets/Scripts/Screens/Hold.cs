using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HeroHQ.Screens
{
    /// <summary>
    /// Press-and-hold on a box (02 GDD 17.1: keep a finger down for 0.4 s to read details without doing anything;
    /// P2b-03, 03 Architecture 8.1 A3). Put on any box that already has a Button: Unity's EventSystem tells this script
    /// when the finger goes down, up, or slides off (the three <c>IPointer…Handler</c> interfaces), and <c>Update</c>
    /// counts the time, like <see cref="Floater"/>. After 0.4 s <see cref="Started"/> fires (the card opens); when the
    /// finger lifts <see cref="Ended"/> fires (the card closes). <b>A hold is never also a tap</b>: the Button still sends
    /// its click when the finger lifts, so the box's click listener asks <see cref="ConsumeClick"/> first and does
    /// nothing when a hold just happened. A short press never fires a hold, and its click goes through as always.
    ///
    /// <see cref="Press"/>, <see cref="Release"/> and <see cref="Tick"/> are public so a test can hold a box without a
    /// mouse or a clock, as the tap tests call BattleScreen's tap methods.
    /// </summary>
    [ExecuteAlways]   // so OnDestroy also runs in the editor's edit mode, where the tests tear boxes down
    public sealed class Hold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>How long a finger must stay down (17.1).</summary>
        public const float Seconds = 0.4f;

        /// <summary>The finger has been down 0.4 s: show what this box is about. Set by whoever builds the box.</summary>
        public Action Started;
        /// <summary>The finger lifted after a hold started (or the box was destroyed mid-hold): put everything back.</summary>
        public Action Ended;

        float held;
        bool pressed;

        /// <summary>The finger is down and the hold has not started yet.</summary>
        public bool Waiting => pressed && !Fired;
        /// <summary>A hold has started and not ended: the card is open.</summary>
        public bool Fired { get; private set; }

        // The click that follows a hold's release belongs to the hold. Cleared by the next press and by ConsumeClick.
        bool swallowClick;

        public void OnPointerDown(PointerEventData e) => Press();
        public void OnPointerUp(PointerEventData e) => Release();

        /// <summary>
        /// The finger slid off before 0.4 s: no hold (and no click either, in Unity's Button). After the card is open it
        /// stays open until the finger lifts: a thumb drifting a little must not close it.
        /// </summary>
        public void OnPointerExit(PointerEventData e)
        {
            if (Waiting) pressed = false;
        }

        void Update() => Tick(Time.deltaTime);

        public void Press()
        {
            pressed = true;
            held = 0;
            swallowClick = false;
        }

        /// <summary>Counts game time while the finger is down; at 0.4 s the hold starts, once.</summary>
        public void Tick(float seconds)
        {
            if (!Waiting) return;
            held += seconds;
            if (held < Seconds) return;
            Fired = true;
            Started?.Invoke();
        }

        public void Release()
        {
            pressed = false;
            if (!Fired) return;
            Fired = false;
            swallowClick = true;
            Ended?.Invoke();
        }

        /// <summary>
        /// Asked by the box's click listener: true when this click is the end of a hold, so it must do nothing.
        /// A click that is not the end of a hold gets false and goes through.
        /// </summary>
        public bool ConsumeClick()
        {
            var was = swallowClick || Fired;
            swallowClick = false;
            return was;
        }

        // A box destroyed while held (the bar is redrawn, a new fight starts) never gets its finger-up: close the card now.
        void OnDestroy()
        {
            if (Fired) Ended?.Invoke();
        }
    }
}
