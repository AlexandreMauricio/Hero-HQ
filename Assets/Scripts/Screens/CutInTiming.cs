using System;

namespace HeroHQ.Screens
{
    /// <summary>Which cut-in plays (01 Vision <i>Cut-in direction</i>; 02 GDD 17.7, 17.8).</summary>
    public enum CutInVersion
    {
        /// <summary>About 2.5 s: freeze, slash, entry, hold and shout, release, back.</summary>
        Full,
        /// <summary>About 0.8 s: freeze and slash, shout and hold, flash out.</summary>
        Short,
        /// <summary>No cut-in: the 0.3 s <i>off</i> look (BattleView.ShowOffLook).</summary>
        Off,
    }

    /// <summary>The cut-in setting (02 GDD 17.8). The panel to change it is P2b-05's; until then it is the default.</summary>
    public enum CutInSetting
    {
        FirstTimeFullThenShort,
        AlwaysFull,
        Off,
    }

    /// <summary>
    /// Where every layer of the cut-in is at one moment. Positions are in the mock-up's own frame: a 1920 x 1080 screen,
    /// x to the right, <b>y down</b> (CutIn turns them round for Unity, whose y goes up).
    /// </summary>
    public struct CutInState
    {
        /// <summary>False once the cut-in has handed the screen back to the battle (only the white flash may still be fading).</summary>
        public bool ShowingCutIn;
        /// <summary>The black dim over the battle, 0 to 0.65.</summary>
        public float Dim;
        /// <summary>The letterbox bars, 0 (gone) to 1 (120 px each).</summary>
        public float Bars;
        /// <summary>How open the slanted colour band is, 0 to 1 (660 px tall when open).</summary>
        public float Band;
        /// <summary>The white flash over everything, 0 to 1.</summary>
        public float Flash;
        public bool HeroVisible;
        /// <summary>The hero's entry, 0 (off screen to the lower left) to 1 (in place); overshoots a little past 1.</summary>
        public float HeroIn;
        /// <summary>The behind-layer's fade and rise, 0 to 1.</summary>
        public float Behind;
        public bool ShoutVisible;
        /// <summary>The shout's size: starts 2.6x and slams down to 1.</summary>
        public float ShoutScale;
        /// <summary>The screen shake, in pixels (y down).</summary>
        public float ShakeX, ShakeY;
        /// <summary>The camera push on the hero, from 1 (the behind-layer gets half of it).</summary>
        public float Push;
        /// <summary>The clock the speed lines run on.</summary>
        public float SpeedTime;
    }

    /// <summary>
    /// The cut-in's timetable (03 Architecture 8.1 A1), as plain C#: a time and a version in, every layer's state out.
    /// Ported line by line from the approved mock-up's own code (<c>_attachments/Hero HQ — 2026-11-15 Cut-In Mock-Up.html</c>:
    /// <c>VERS</c>, <c>seg</c>, <c>E</c>, <c>shake</c>, <c>getState</c>; the skip from its click handler and <c>loop</c>),
    /// so what the owner approved is what plays.
    /// </summary>
    public static class CutInTiming
    {
        /// <summary>How long each version lasts, flash-out included.</summary>
        public const float FullSeconds = 2.5f, ShortSeconds = 0.8f;
        /// <summary>When each version hands the screen back to the battle (the hits may start; the flash still fades).</summary>
        public const float FullBattleAt = 2.2f, ShortBattleAt = 0.68f;
        /// <summary>A tap: 0.05 s for the flash to rise over the frozen frame, 0.05 s for it to fade on the battle.</summary>
        public const float SkipFlashUp = 0.05f, SkipSeconds = 0.1f;

        /// <summary>The beats with their start and end times (the mock-up's <c>VERS</c>).</summary>
        public static readonly (float from, float to, string name)[] FullBeats =
        {
            (0f, 0.15f, "Freeze"), (0.15f, 0.35f, "Slash"), (0.35f, 0.65f, "Entry"),
            (0.65f, 1.85f, "Hold and shout"), (1.85f, 2.2f, "Release"), (2.2f, 2.5f, "Back"),
        };
        public static readonly (float from, float to, string name)[] ShortBeats =
        {
            (0f, 0.15f, "Freeze + slash"), (0.15f, 0.6f, "Shout and hold"), (0.6f, 0.8f, "Flash out"),
        };

        public static float Seconds(CutInVersion version) =>
            version == CutInVersion.Full ? FullSeconds : version == CutInVersion.Short ? ShortSeconds : 0f;

        /// <summary>
        /// Full, short or off (02 GDD 17.7, 17.8): <i>first time full, then short</i> plays the full one for a hero
        /// nobody has seen yet, on either side; <i>always full</i>; <i>off</i>.
        /// </summary>
        public static CutInVersion Pick(CutInSetting setting, bool seen)
        {
            switch (setting)
            {
                case CutInSetting.AlwaysFull: return CutInVersion.Full;
                case CutInSetting.Off: return CutInVersion.Off;
                default: return seen ? CutInVersion.Short : CutInVersion.Full;
            }
        }

        // ---- the mock-up's helpers -----------------------------------------------------------------------------

        /// <summary>0 before <paramref name="a"/>, 1 after <paramref name="b"/>, a straight line between.</summary>
        static float Seg(float t, float a, float b) => Math.Min(1, Math.Max(0, (t - a) / (b - a)));

        /// <summary>Fast start, slow end.</summary>
        static float Out(float t) => 1 - (float)Math.Pow(1 - t, 3);
        /// <summary>Slow start, fast end.</summary>
        static float In(float t) => t * t * t;
        /// <summary>Goes a little past 1, then settles (the hero's overshoot).</summary>
        static float Back(float t)
        {
            const float c1 = 2.2f, c3 = c1 + 1;
            return 1 + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2);
        }

        /// <summary>A shake that dies away between <paramref name="a"/> and <paramref name="b"/>.</summary>
        static (float x, float y) Shake(float t, float a, float b, float amp)
        {
            if (t < a || t > b) return (0, 0);
            var k = 1 - Seg(t, a, b);
            return ((float)Math.Sin(t * 95) * amp * k, (float)Math.Cos(t * 130) * amp * k * 0.6f);
        }

        /// <summary>Every layer's state <paramref name="t"/> seconds into <paramref name="version"/> (the mock-up's <c>getState</c>).</summary>
        public static CutInState At(CutInVersion version, float t)
        {
            var s = new CutInState { ShowingCutIn = true, ShoutScale = 1, Push = 1, HeroIn = 1, SpeedTime = t };
            if (version == CutInVersion.Full)
            {
                if (t >= FullBattleAt)
                {
                    s.ShowingCutIn = false;
                    s.Flash = 1 - Out(Seg(t, FullBattleAt, FullSeconds));
                    return s;
                }
                s.Dim = 0.65f * Out(Seg(t, 0, 0.15f));
                s.Bars = Out(Seg(t, 0, 0.12f));
                s.Band = Out(Seg(t, 0.15f, 0.3f));
                s.Flash = t < 0.15f ? 0 : t < 0.18f ? 0.9f * Seg(t, 0.15f, 0.18f) : 0.9f * (1 - Out(Seg(t, 0.18f, 0.35f)));
                s.Flash = Math.Max(s.Flash, (float)Math.Pow(Seg(t, 1.95f, FullBattleAt), 2));
                s.HeroVisible = t >= 0.35f;
                s.HeroIn = Back(Seg(t, 0.35f, 0.65f));
                s.Behind = Out(Seg(t, 0.4f, 0.75f));
                s.ShoutVisible = t >= 0.65f;
                s.ShoutScale = 1 + 1.6f * (1 - Out(Seg(t, 0.65f, 0.75f)));
                var (ax, ay) = Shake(t, 0.75f, 0.92f, 16);
                var (cx, cy) = Shake(t, 1.85f, 2.05f, 10);
                s.ShakeX = ax + cx;
                s.ShakeY = ay + cy;
                s.Push = 1 + 0.08f * Seg(t, 0.65f, 1.85f) + 0.10f * In(Seg(t, 1.85f, FullBattleAt));
            }
            else if (version == CutInVersion.Short)
            {
                if (t >= ShortBattleAt)
                {
                    s.ShowingCutIn = false;
                    s.Flash = 1 - Out(Seg(t, ShortBattleAt, ShortSeconds));
                    return s;
                }
                s.Dim = 0.65f * Out(Seg(t, 0, 0.1f));
                s.Bars = Out(Seg(t, 0, 0.1f));
                s.Band = Out(Seg(t, 0, 0.12f));
                s.Flash = t < 0.04f ? 0.9f * Seg(t, 0, 0.04f) : 0.9f * (1 - Out(Seg(t, 0.04f, 0.18f)));
                s.Flash = Math.Max(s.Flash, (float)Math.Pow(Seg(t, 0.6f, ShortBattleAt), 2));
                s.HeroVisible = true;
                s.Behind = 1;
                s.ShoutVisible = t >= 0.15f;
                s.ShoutScale = 1 + 1.6f * (1 - Out(Seg(t, 0.15f, 0.23f)));
                var (ax, ay) = Shake(t, 0.23f, 0.36f, 16);
                s.ShakeX = ax;
                s.ShakeY = ay;
                s.Push = 1 + 0.06f * Seg(t, 0.1f, 0.6f) + 0.08f * In(Seg(t, 0.6f, ShortBattleAt));
                s.SpeedTime = t + 1;
            }
            else throw new ArgumentException("The off look is BattleView's, not the cut-in's.", nameof(version));
            return s;
        }

        /// <summary>
        /// One cut-in's clock: <see cref="Advance"/> moves it on, <see cref="Skip"/> is a tap. A tap while the cut-in
        /// shows freezes the frame and raises the flash for 0.05 s, then the battle shows under a flash that fades in
        /// another 0.05 s (the mock-up's skip). A tap after the cut-in has handed back does nothing, as in the mock-up.
        /// </summary>
        public sealed class Clock
        {
            public Clock(CutInVersion version)
            {
                if (version == CutInVersion.Off) throw new ArgumentException("No clock for the off look.", nameof(version));
                Version = version;
            }

            public CutInVersion Version { get; }
            /// <summary>Seconds since the cut-in started.</summary>
            public float Time { get; private set; }
            /// <summary>When the tap came, or null.</summary>
            public float? SkippedAt { get; private set; }

            public void Advance(float seconds) => Time += Math.Max(0, seconds);

            public void Skip()
            {
                if (SkippedAt.HasValue || !At(Version, Time).ShowingCutIn) return;
                SkippedAt = Time;
            }

            public CutInState State
            {
                get
                {
                    if (!SkippedAt.HasValue) return At(Version, Math.Min(Time, Seconds(Version)));
                    var since = Time - SkippedAt.Value;
                    if (since < SkipFlashUp)
                    {
                        var frozen = At(Version, SkippedAt.Value);
                        frozen.Flash = Math.Max(frozen.Flash, since / SkipFlashUp);
                        return frozen;
                    }
                    return new CutInState
                    {
                        ShowingCutIn = false, ShoutScale = 1, Push = 1, HeroIn = 1,
                        Flash = 1 - Seg(since, SkipFlashUp, SkipSeconds),
                    };
                }
            }

            /// <summary>The battle shows again: the hits may play (acceptance 3).</summary>
            public bool BattleShows => !State.ShowingCutIn;

            /// <summary>Nothing of the cut-in is left on screen, the flash included.</summary>
            public bool Finished => SkippedAt.HasValue ? Time - SkippedAt.Value >= SkipSeconds : Time >= Seconds(Version);
        }
    }
}
