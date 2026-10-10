using System.Collections.Generic;
using NUnit.Framework;

namespace MoveIt.Games.Tests
{
    public class GameAccessTests
    {
        const long Now = 1_760_000_000;

        static List<GameRule> Catalog(params GameRule[] rules) => new List<GameRule>(rules);

        [Test]
        public void WithoutACatalogEveryGameUsesTheBuiltInDefault()
        {
            Assert.AreEqual(GameTier.Free, GameAccess.DefaultTier("runner"));
            Assert.AreEqual(Access.Play, GameAccess.For("runner", null, 10203, Now, false));
            Assert.AreEqual(Access.Play, GameAccess.For("fly", Catalog(), 10203, Now, false));
        }

        [Test]
        public void PassGamesAreLockedUntilGamePassIsActive()
        {
            var catalog = Catalog(new GameRule { Id = "fly", Tier = GameTier.Pass }, new GameRule { Id = "runner", Tier = GameTier.Free });
            Assert.AreEqual(Access.Locked, GameAccess.For("fly", catalog, 10203, Now, passActive: false));
            Assert.AreEqual(Access.Play, GameAccess.For("fly", catalog, 10203, Now, passActive: true));
            Assert.AreEqual(Access.Play, GameAccess.For("runner", catalog, 10203, Now, passActive: false));
        }

        [Test]
        public void TheCatalogCanHideAGameEverywhereOnOldVersionsOrUntilItsLaunch()
        {
            var hidden = Catalog(new GameRule { Id = "sword", Tier = GameTier.Free, Hidden = true });
            Assert.AreEqual(Access.Hidden, GameAccess.For("sword", hidden, 10203, Now, true));
            var minApp = Catalog(new GameRule { Id = "sword", Tier = GameTier.Free, MinApp = 10300 });
            Assert.AreEqual(Access.Hidden, GameAccess.For("sword", minApp, 10203, Now, true), "version where it crashes");
            Assert.AreEqual(Access.Play, GameAccess.For("sword", minApp, 10300, Now, true));
            var scheduled = Catalog(new GameRule { Id = "sword", Tier = GameTier.Pass, From = Now + 3600 });
            Assert.AreEqual(Access.Hidden, GameAccess.For("sword", scheduled, 10203, Now, true));
            Assert.AreEqual(Access.Locked, GameAccess.For("sword", scheduled, 10203, Now + 3600, false));
        }

        [Test]
        public void SceneNamesMapToCatalogIds()
        {
            Assert.AreEqual("runner", GameAccess.IdOf("Runner"));
            Assert.AreEqual("fly", GameAccess.IdOf("Fly"));
        }
    }
}
