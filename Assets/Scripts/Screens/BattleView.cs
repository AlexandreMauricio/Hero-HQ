using UnityEngine;
using UnityEngine.UI;

namespace HeroHQ.Screens
{
    // Builds every box of the battle screen by code under one Canvas, and knows no rules (03 Architecture 7.2).
    // P2a-04: the empty screen, a canvas scaled from 1920 x 1080, the safe-area panel and the four zones of
    // 02 GDD 17.3. The figures, bar, buttons and texts go into these zones in P2a-07 to P2a-09.
    public class BattleView
    {
        const float ReferenceWidth = 1920, ReferenceHeight = 1080;
        const float SideMargin = 90; // 02 GDD 17.1: what the player must see or press stays 90 px from the sides

        public Canvas Canvas { get; private set; }
        public RectTransform SafeArea { get; private set; }
        public RectTransform TopStrip { get; private set; }
        public RectTransform Banner { get; private set; }
        public RectTransform Field { get; private set; }
        public RectTransform BottomBand { get; private set; }

        Rect fittedSafeArea;
        Vector2 fittedScreen;

        public void Build(Transform parent)
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Canvas = go.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Scale with the screen, keeping the full 1080 height: a wider phone shows more at the sides (17.1).
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1;

            SafeArea = NewRect("Safe area", Canvas.transform);
            SafeArea.anchorMin = Vector2.zero;
            SafeArea.anchorMax = Vector2.one;
            FitSafeArea();

            // Later siblings draw on top: the banner overlaps the top of the field and must be above it.
            TopStrip = Zone("Top strip", 24, 170);
            Field = Zone("Field", 190, 780);
            Banner = Zone("Banner", 170, 240);
            BottomBand = Zone("Bottom band", 800, 1050);
        }

        // Called every frame by BattleScreen; only does work when the safe area or the screen size changed.
        public void FitSafeArea() => FitSafeArea(Screen.safeArea, new Vector2(Screen.width, Screen.height));

        public void FitSafeArea(Rect safe, Vector2 screen)
        {
            if (SafeArea == null || screen.x <= 0 || screen.y <= 0) return;
            if (safe == fittedSafeArea && screen == fittedScreen) return;
            fittedSafeArea = safe;
            fittedScreen = screen;
            SafeArea.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            SafeArea.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            SafeArea.offsetMin = SafeArea.offsetMax = Vector2.zero;
        }

        // A full-width band of the safe area between two heights measured down from the top of the 1080 reference
        // screen (17.3's numbers), 90 px in from each side.
        RectTransform Zone(string name, float top, float bottom)
        {
            var zone = NewRect(name, SafeArea);
            zone.anchorMin = new Vector2(0, 1 - bottom / ReferenceHeight);
            zone.anchorMax = new Vector2(1, 1 - top / ReferenceHeight);
            zone.offsetMin = new Vector2(SideMargin, 0);
            zone.offsetMax = new Vector2(-SideMargin, 0);
            return zone;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
