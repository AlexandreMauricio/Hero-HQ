using NUnit.Framework;
using UnityEditor;

namespace HeroHQ.Tests
{
    // Checks the project settings that 03 Architecture section 2 fixes (OWNER, 2026-11-15).
    public class ProjectSetupTests
    {
        [Test]
        public void Screen_IsLandscapeOnly()
        {
            Assert.AreEqual(UIOrientation.AutoRotation, PlayerSettings.defaultInterfaceOrientation,
                "The phone should turn between the two landscape sides.");
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeLeft, "Landscape left must be allowed.");
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeRight, "Landscape right must be allowed.");
            Assert.IsFalse(PlayerSettings.allowedAutorotateToPortrait, "Portrait must be off.");
            Assert.IsFalse(PlayerSettings.allowedAutorotateToPortraitUpsideDown, "Upside-down portrait must be off.");
        }
    }
}
