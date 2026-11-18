using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    /// <summary>
    /// The ultimate's cut-in on screen (03 Architecture 8.1 A1; 01 Vision <i>Cut-in direction</i>), built by code like
    /// every other box: six layers under the Canvas (the dim, the slanted colour band with its glow, speed lines and
    /// behind-layer, the letterbox bars, the hero, the shout, the white flash), set every frame from
    /// <see cref="CutInTiming"/>. A tap anywhere skips. It counts game time (<c>Time.deltaTime</c>), so a pause stops it,
    /// and destroys itself when nothing of it is left. Only the picture, the behind-layer, the shout, the colours and the
    /// RIVAL tag come from the hero (acceptance 2); everything else is the one template.
    /// </summary>
    public sealed class CutIn : MonoBehaviour
    {
        // The mock-up's placement (its renderer: FX, FY, HS, BX, BY, BAND; Kal's from make_mockup_test.py), on a
        // 1920 x 1080 screen with y down. Reading R28: one placement for every hero, Kal's.
        const float Width = 1920, Height = 1080;
        const float FX = 720, FY = 560;                       // the hero's hips: the camera push grows from here
        const float FigureScale = 0.62f;                      // the placeholder figure's size (HS)
        const float BandAngle = 9;                            // the band rises 9 degrees to the right
        const float BandHalfWidth = 1700, BandHalfHeight = 330, EdgeWidth = 12, BarHeight = 120;
        // make_mockup_test.py: Kal's figure 615 px tall from y 235; the PNG's figure box starts 160 px down and is
        // 1600 px tall, so the whole PNG is 1920 * 615 / 1600 = 738 px tall with its top at y 173.5, centred on FX.
        const float DrawingHeight = 738, DrawingTop = 173.5f;
        // make_mockup_test.py TAL: the behind-layer's top-left at (376, 75), 2508 px * 0.395 = 990.66 px wide.
        const float BehindLeft = 376, BehindTop = 75, BehindWidth = 990.66f;
        const float ShoutX = 1385, ShoutY = 450, ShoutAngle = 8, ShoutSize = 170, ShoutMaxWidth = 800, NameSize = 46;

        public CutInTiming.Clock Clock { get; private set; }
        public Fighter Who { get; private set; }
        /// <summary>The enemy's cut-in wears the RIVAL tag beside the name (02 GDD 17.7).</summary>
        public bool Rival => Who.Side == Side.Enemy;
        /// <summary>No drawing for this hero: the generic figure drawn by code (reading R29).</summary>
        public bool Placeholder { get; private set; }

        public Image Dim { get; private set; }
        public RectTransform Band { get; private set; }
        public Image BandColour { get; private set; }
        public Image Glow { get; private set; }
        public RectTransform TopBar { get; private set; }
        public RectTransform BottomBar { get; private set; }
        public RectTransform HeroCamera { get; private set; }
        public RectTransform HeroOffset { get; private set; }
        /// <summary>The hero's drawing, or the placeholder figure.</summary>
        public Image Hero { get; private set; }
        /// <summary>The behind-layer, or null when the hero has none.</summary>
        public Image Behind { get; private set; }
        public RectTransform Shout { get; private set; }
        public IReadOnlyList<Text> ShoutLines => shoutLines;
        public Text NameLine { get; private set; }
        public Text RivalTag { get; private set; }
        public Image Flash { get; private set; }

        public bool BattleShows => Clock.BattleShows;
        public bool Finished => Clock.Finished;

        readonly List<Text> shoutLines = new List<Text>();
        readonly List<(RectTransform rect, float angle, float phase, float speed, float length)> speedLines =
            new List<(RectTransform, float, float, float, float)>();
        GameObject stageLayers;
        Image tapCatcher;
        RectTransform behindCamera;
        Vector2 behindAt;

        /// <summary>
        /// Builds a cut-in over the whole <paramref name="canvas"/> and shows its first frame. <paramref name="drawing"/>
        /// null gives the placeholder; <paramref name="behind"/> null leaves the behind-layer out.
        /// </summary>
        public static CutIn Show(Transform canvas, Fighter who, CutInVersion version, Color background, Color glow,
                                 Sprite drawing, Sprite behind)
        {
            var root = BattleView.NewRect("Cut-in", canvas);
            root.Inset(0);
            var cutIn = root.gameObject.AddComponent<CutIn>();
            cutIn.Build(root, who, version, background, glow, drawing, behind);
            cutIn.Step(0);
            return cutIn;
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>A tap anywhere (public so a test can tap).</summary>
        public void Tap() => Clock.Skip();

        /// <summary>Moves the clock on and redraws every layer; destroys the cut-in once it is over. Public for tests.</summary>
        public void Step(float seconds)
        {
            Clock.Advance(seconds);
            Draw(Clock.State);
            if (!Clock.Finished) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // ---- building --------------------------------------------------------------------------------------------

        void Build(RectTransform root, Fighter who, CutInVersion version, Color background, Color glow, Sprite drawing,
                   Sprite behind)
        {
            Clock = new CutInTiming.Clock(version);
            Who = who;

            // Catches a tap anywhere while the cut-in shows; nothing under it is touched.
            tapCatcher = root.gameObject.AddComponent<Image>();
            tapCatcher.color = Color.clear;
            var button = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = tapCatcher;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Tap);

            // 1. The dim over the battle, the whole screen.
            Dim = BattleView.Panel("Dim", root, Color.black);

            // The mock-up's 1920 x 1080 frame, in the middle: a wider phone shows more of the band and the bars.
            var stage = BattleView.NewRect("Stage", root);
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(Width, Height);
            stageLayers = stage.gameObject;

            // 2. The slanted band: the pantheon colour, cut to the band by a Mask, with the glow, the speed lines and the
            // behind-layer inside it (drawn straight, so the band turns and they don't), and a white edge on each side.
            Band = Centred("Band", stage, Mock(Width / 2, Height / 2));
            Band.sizeDelta = new Vector2(2 * BandHalfWidth, 0);
            Band.localRotation = Quaternion.Euler(0, 0, BandAngle);
            BandColour = BattleView.Panel("Colour", Band, background);
            BandColour.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var inside = Centred("Inside", BandColour.rectTransform, Vector2.zero);
            inside.sizeDelta = new Vector2(Width, Height);
            inside.localRotation = Quaternion.Euler(0, 0, -BandAngle);
            // The glow: the mock-up's radial gradient around (FX, FY - 120), 760 px out.
            Glow = Picture("Glow", inside, BandGlow(background, glow), Color.white);
            Glow.rectTransform.anchoredPosition = Mock(FX, FY - 120);
            Glow.rectTransform.sizeDelta = new Vector2(1520, 1520);
            BuildSpeedLines(inside);
            if (behind != null) BuildBehind(inside, behind);
            foreach (var top in new[] { true, false })
            {
                var edge = BattleView.Panel(top ? "Top edge" : "Bottom edge", Band, Color.white).rectTransform;
                edge.anchorMin = new Vector2(0, top ? 1 : 0);
                edge.anchorMax = new Vector2(1, top ? 1 : 0);
                edge.sizeDelta = new Vector2(0, EdgeWidth);
                edge.anchoredPosition = Vector2.zero;
            }

            // 3. The letterbox bars, the whole width.
            TopBar = Bar("Top bar", root, top: true);
            BottomBar = Bar("Bottom bar", root, top: false);

            // 4. The hero, pushed by the camera from the hips.
            HeroCamera = Centred("Hero camera", stage, Mock(FX, FY));
            HeroOffset = Centred("Hero", HeroCamera, Vector2.zero);
            if (drawing != null)
            {
                Hero = Picture("Drawing", HeroOffset, drawing, Color.white);
                var width = DrawingHeight * drawing.rect.width / drawing.rect.height;
                TopLeft(Hero.rectTransform, new Vector2(-width / 2, FY - DrawingTop), new Vector2(width, DrawingHeight));
            }
            else BuildPlaceholder(background, glow);

            // 5. The shout, slanted, with the name (and RIVAL) under it.
            BuildShout(stage, who, glow);

            // 6. The white flash over everything.
            Flash = BattleView.Panel("Flash", root, Color.white);
        }

        void BuildSpeedLines(RectTransform inside)
        {
            // The mock-up's 80 lines from its own little random generator with seed 7 (mulberry32), so they match it.
            var seed = 7u;
            float Next()
            {
                seed += 0x6D2B79F5u;
                var t = (seed ^ (seed >> 15)) * (1u | seed);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (float)((t ^ (t >> 14)) / 4294967296.0);
            }
            var lines = BattleView.NewRect("Speed lines", inside);
            lines.Inset(0);
            for (int i = 0; i < 80; i++)
            {
                var angle = Next() * Mathf.PI * 2;
                var phase = Next();
                var speed = 1.6f + Next() * 1.4f;
                var length = 180 + Next() * 320;
                var width = 4 + Next() * 10;
                var line = Picture("Line", lines, Wedge, new Color(1, 1, 1, 0.9f)).rectTransform;
                line.pivot = new Vector2(0, 0.5f);
                line.sizeDelta = new Vector2(length, 2 * width);
                line.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);   // the mock-up's angles turn with y down
                speedLines.Add((line, angle, phase, speed, length));
            }
        }

        void BuildBehind(RectTransform inside, Sprite behind)
        {
            behindCamera = Centred("Behind camera", inside, Mock(FX, FY));
            Behind = Picture("Behind", behindCamera, behind, Color.white);
            var height = BehindWidth * behind.rect.height / behind.rect.width;
            behindAt = new Vector2(BehindLeft - FX, FY - BehindTop);
            TopLeft(Behind.rectTransform, behindAt, new Vector2(BehindWidth, height));
        }

        void BuildPlaceholder(Color background, Color glow)
        {
            // Reading R29: one generic figure (the mock-up's ENEMY shape) in a dark shade of the pantheon colour, black
            // outline, two glowing eyes; the shout and the name say who it is.
            Placeholder = true;
            var sprite = PlaceholderFigure;
            Hero = Picture("Placeholder", HeroOffset, sprite, Color.Lerp(Color.black, background, 0.3f));
            var size = new Vector2(sprite.rect.width, sprite.rect.height) * (FigureScale / PlaceholderPixelsPerUnit);
            TopLeft(Hero.rectTransform, new Vector2(PlaceholderBounds.xMin, -PlaceholderBounds.yMin) * FigureScale, size);
            foreach (var x in new[] { -35f, 35f })
            {
                var at = new Vector2(x, 400) * FigureScale;
                var halo = Picture("Eye glow", HeroOffset, RadialGlow, glow).rectTransform;
                halo.anchoredPosition = at;
                halo.sizeDelta = new Vector2(70, 70);
                var eye = Picture("Eye", HeroOffset, BattleView.Rounded, glow).rectTransform;
                eye.anchoredPosition = at;
                eye.sizeDelta = new Vector2(18, 18);
            }
        }

        void BuildShout(RectTransform stage, Fighter who, Color glow)
        {
            Shout = Centred("Shout", stage, Mock(ShoutX, ShoutY));
            Shout.localRotation = Quaternion.Euler(0, 0, ShoutAngle);
            var lines = ShoutLinesOf(who.Hero.Cutin.Shout);

            // Unity's built-in font for now (OWNER D3), bold and slanted, white, thick dark outline, pantheon-colour
            // shadow (01 Vision lettering), shrunk if the widest line is over 800 px, as the mock-up does.
            var size = ShoutSize;
            var texts = lines.Select(l => Lettering("Shout line", l, (int)size, Color.white, glow)).ToList();
            var widest = texts.Max(t => t.preferredWidth);
            if (widest > ShoutMaxWidth)
            {
                size *= ShoutMaxWidth / widest;
                foreach (var t in texts) t.fontSize = Mathf.FloorToInt(size);
            }
            var lineHeight = size * 0.98f;
            var firstY = -(lines.Count - 1) * lineHeight / 2;            // y down, as the mock-up
            for (int i = 0; i < texts.Count; i++)
                texts[i].rectTransform.anchoredPosition = new Vector2(0, -(firstY + i * lineHeight));
            shoutLines.AddRange(texts);

            var nameY = -(firstY + (lines.Count - 1) * lineHeight + size * 0.75f);
            NameLine = Lettering("Name", $"— {who.Hero.Name.ToUpperInvariant()} —", (int)NameSize, Color.white, null);
            NameLine.rectTransform.anchoredPosition = new Vector2(0, nameY);
            if (!Rival) return;
            // The RIVAL tag beside the name (02 GDD 17.7): the one piece that differs on the enemy's side.
            RivalTag = Lettering("Rival", "RIVAL", (int)NameSize, BattleView.Red, null);
            RivalTag.rectTransform.anchoredPosition =
                new Vector2(NameLine.preferredWidth / 2 + 24 + RivalTag.preferredWidth / 2, nameY);
        }

        Text Lettering(string name, string text, int size, Color color, Color? shadow)
        {
            var label = BattleView.Label(name, Shout, text, size, color, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.BoldAndItalic;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(2000, size * 1.4f);
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(size * 0.05f, -size * 0.05f);
            if (shadow.HasValue)
            {
                var drop = label.gameObject.AddComponent<Shadow>();
                drop.effectColor = shadow.Value;
                drop.effectDistance = new Vector2(size * 0.07f, -size * 0.08f);
            }
            return label;
        }

        /// <summary>The mock-up's <c>shoutLines</c>: a shout over 12 letters with two or more words is split in two,
        /// at the space that makes the longer line shortest.</summary>
        public static List<string> ShoutLinesOf(string shout)
        {
            var words = shout.Split(' ');
            if (shout.Length <= 12 || words.Length < 2) return new List<string> { shout };
            (int longest, string a, string b) best = (int.MaxValue, null, null);
            for (int i = 1; i < words.Length; i++)
            {
                var a = string.Join(" ", words.Take(i));
                var b = string.Join(" ", words.Skip(i));
                var longest = Math.Max(a.Length, b.Length);
                if (longest < best.longest) best = (longest, a, b);
            }
            return new List<string> { best.a, best.b };
        }

        // ---- drawing one frame (the mock-up's drawCutin) ---------------------------------------------------------

        void Draw(CutInState s)
        {
            tapCatcher.raycastTarget = s.ShowingCutIn;               // once the battle shows, taps go through again
            Flash.color = new Color(1, 1, 1, Mathf.Min(1, s.Flash));
            var showing = s.ShowingCutIn;
            Dim.enabled = showing;
            stageLayers.SetActive(showing);
            TopBar.gameObject.SetActive(showing);
            BottomBar.gameObject.SetActive(showing);
            if (!showing) return;

            Dim.color = new Color(0, 0, 0, s.Dim);
            Band.sizeDelta = new Vector2(2 * BandHalfWidth, 2 * BandHalfHeight * s.Band);
            Band.gameObject.SetActive(s.Band > 0);
            TopBar.sizeDelta = BottomBar.sizeDelta = new Vector2(0, BarHeight * s.Bars);

            // Speed lines fly in toward the hero's hips and start over (the mock-up's speedLines).
            foreach (var (rect, angle, phase, speed, _) in speedLines)
            {
                var k = (s.SpeedTime * speed + phase) % 1;
                var r = 1500 - k * 1250;
                rect.gameObject.SetActive(r >= 260);
                rect.anchoredPosition = Mock(FX + Mathf.Cos(angle) * r, FY + Mathf.Sin(angle) * r);
            }

            if (Behind != null)
            {
                Behind.color = new Color(1, 1, 1, s.Behind);
                var push = 1 + (s.Push - 1) * 0.5f;
                behindCamera.localScale = new Vector3(push, push, 1);
                // It rises 350 px into place and shakes half as much as the hero (the parallax), inside the camera push.
                Behind.rectTransform.anchoredPosition = behindAt + new Vector2(s.ShakeX * 0.5f, -((1 - s.Behind) * 350 + s.ShakeY * 0.5f));
            }

            HeroCamera.gameObject.SetActive(s.HeroVisible);
            HeroCamera.localScale = new Vector3(s.Push, s.Push, 1);
            var off = (1 - s.HeroIn) * 1500;                          // it slides in along the band from the lower left
            var angleRad = BandAngle * Mathf.Deg2Rad;
            HeroOffset.anchoredPosition = new Vector2(-Mathf.Cos(angleRad) * off + s.ShakeX, -(Mathf.Sin(angleRad) * off + s.ShakeY));

            Shout.gameObject.SetActive(s.ShoutVisible);
            Shout.anchoredPosition = Mock(ShoutX + s.ShakeX, ShoutY + s.ShakeY);
            Shout.localScale = new Vector3(s.ShoutScale, s.ShoutScale, 1);
        }

        // ---- small helpers ---------------------------------------------------------------------------------------

        /// <summary>A point of the mock-up's screen (y down from the top-left) as a position from the stage's middle.</summary>
        static Vector2 Mock(float x, float y) => new Vector2(x - Width / 2, Height / 2 - y);

        static RectTransform Centred(string name, Transform parent, Vector2 at)
        {
            var rect = BattleView.NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = at;
            return rect;
        }

        static void TopLeft(RectTransform rect, Vector2 at, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
        }

        static RectTransform Bar(string name, Transform root, bool top)
        {
            var bar = BattleView.Panel(name, root, Color.black).rectTransform;
            bar.anchorMin = new Vector2(0, top ? 1 : 0);
            bar.anchorMax = new Vector2(1, top ? 1 : 0);
            bar.pivot = new Vector2(0.5f, top ? 1 : 0);
            bar.anchoredPosition = Vector2.zero;
            return bar;
        }

        static Image Picture(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = BattleView.Panel(name, parent, color, sprite);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.sizeDelta = Vector2.zero;
            return image;
        }

        // ---- three shapes drawn by code (no art file) ------------------------------------------------------------

        static Sprite MadeByCode(string name, int width, int height, Func<int, int, Color32> pixel, Vector2? pivot = null)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name + " (made by code)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), pivot ?? new Vector2(0.5f, 0.5f), 100);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        static Sprite wedge, radialGlow, placeholderFigure;

        /// <summary>A white wedge, its point on the left, for a speed line.</summary>
        static Sprite Wedge => wedge != null ? wedge : (wedge = MadeByCode("Wedge", 64, 32, (x, y) =>
        {
            var across = Mathf.Abs(y + 0.5f - 16) / 16;                 // 0 in the middle, 1 at the edges
            var along = (x + 0.5f) / 64;                                // 0 at the point, 1 at the base
            return new Color32(255, 255, 255, (byte)(Mathf.Clamp01((along - across) * 32 + 0.5f) * 255));
        }));

        /// <summary>How much glow at a distance from the middle (0 to 1 of the radius): the mock-up's gradient, full to
        /// 40/760, two-thirds at 35 %, none at the edge.</summary>
        static float GlowAmount(int x, int y)
        {
            var f = new Vector2(x + 0.5f - 64, y + 0.5f - 64).magnitude / 64;
            const float inner = 40f / 760;
            var a = f <= inner ? 1 : f <= 0.35f ? Mathf.Lerp(1, 0.667f, (f - inner) / (0.35f - inner)) : Mathf.Lerp(0.667f, 0, (f - 0.35f) / 0.65f);
            return Mathf.Clamp01(a);
        }

        /// <summary>A white glow fading out from the middle (the placeholder's eyes).</summary>
        static Sprite RadialGlow => radialGlow != null ? radialGlow : (radialGlow = MadeByCode("Glow", 128, 128,
            (x, y) => new Color32(255, 255, 255, (byte)(GlowAmount(x, y) * 255))));

        static readonly Dictionary<(Color, Color), Sprite> bandGlows = new Dictionary<(Color, Color), Sprite>();

        /// <summary>
        /// The band's glow already mixed with the band's colour, opaque. Unity mixes see-through colours in linear
        /// colour space, which turned the mock-up's yellow-to-blue glow grey; mixing them here, the way the web page
        /// does, gives the approved look. One per pair of pantheon colours.
        /// </summary>
        static Sprite BandGlow(Color background, Color glow)
        {
            if (bandGlows.TryGetValue((background, glow), out var sprite) && sprite != null) return sprite;
            return bandGlows[(background, glow)] = MadeByCode("Band glow", 128, 128, (x, y) =>
            {
                var c = Color.Lerp(background, glow, GlowAmount(x, y));
                c.a = 1;
                return c;
            });
        }

        // The mock-up's ENEMY figure (units: hips at 0, about 1000 tall, y down): body, head, two ears, two arms, two legs.
        static readonly (Vector2[] points, float width)[] FigureLines =
        {
            (new[] { new Vector2(0, 0), new Vector2(0, -260) }, 150),
            (new[] { new Vector2(-60, -240), new Vector2(-200, -120), new Vector2(-170, 0) }, 60),
            (new[] { new Vector2(60, -240), new Vector2(200, -120), new Vector2(170, 0) }, 60),
            (new[] { new Vector2(-40, 0), new Vector2(-80, 240), new Vector2(-110, 470) }, 75),
            (new[] { new Vector2(40, 0), new Vector2(80, 240), new Vector2(110, 470) }, 75),
        };
        static readonly Vector2[][] FigureTriangles =
        {
            new[] { new Vector2(-70, -460), new Vector2(-120, -560), new Vector2(-30, -470) },
            new[] { new Vector2(70, -460), new Vector2(120, -560), new Vector2(30, -470) },
        };
        static readonly Vector2 FigureHead = new Vector2(0, -390);
        const float FigureHeadRadius = 95, FigureOutline = 14;
        /// <summary>The figure's box in units (y down), outline included.</summary>
        static readonly Rect PlaceholderBounds = Rect.MinMaxRect(-250, -580, 250, 530);
        /// <summary>Texture pixels per figure unit: drawn at the size it shows (0.62), so nothing is stretched.</summary>
        const float PlaceholderPixelsPerUnit = FigureScale;

        /// <summary>
        /// The figure as one merged shape: white inside (tinted by the Image), black outline 14 units wide, made once.
        /// Each pixel asks how far it is from the nearest part (inside counts as negative), the mock-up's two passes in one.
        /// </summary>
        static Sprite PlaceholderFigure
        {
            get
            {
                if (placeholderFigure != null) return placeholderFigure;
                var k = PlaceholderPixelsPerUnit;
                var w = Mathf.CeilToInt(PlaceholderBounds.width * k);
                var h = Mathf.CeilToInt(PlaceholderBounds.height * k);
                placeholderFigure = MadeByCode("Placeholder figure", w, h, (x, y) =>
                {
                    // Pixel row 0 is the bottom: turn it into units, y down.
                    var p = new Vector2(PlaceholderBounds.xMin + (x + 0.5f) / k, PlaceholderBounds.yMax - (y + 0.5f) / k);
                    var d = Vector2.Distance(p, FigureHead) - FigureHeadRadius;
                    foreach (var (points, width) in FigureLines)
                        for (int i = 1; i < points.Length; i++)
                            d = Mathf.Min(d, SegmentDistance(p, points[i - 1], points[i]) - width / 2);
                    foreach (var triangle in FigureTriangles) d = Mathf.Min(d, TriangleDistance(p, triangle));
                    var px = d * k;                                           // in pixels, for one-pixel soft edges
                    var alpha = Mathf.Clamp01(FigureOutline * k - px + 0.5f);
                    var white = (byte)(Mathf.Clamp01(-px + 0.5f) * 255);
                    return new Color32(white, white, white, (byte)(alpha * 255));
                });
                return placeholderFigure;
            }
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        static float TriangleDistance(Vector2 p, Vector2[] t)
        {
            var edge = Mathf.Min(SegmentDistance(p, t[0], t[1]), Mathf.Min(SegmentDistance(p, t[1], t[2]), SegmentDistance(p, t[2], t[0])));
            float Side(Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            var s0 = Side(t[0], t[1]);
            var s1 = Side(t[1], t[2]);
            var s2 = Side(t[2], t[0]);
            var inside = (s0 >= 0 && s1 >= 0 && s2 >= 0) || (s0 <= 0 && s1 <= 0 && s2 <= 0);
            return inside ? -edge : edge;
        }
    }
}
