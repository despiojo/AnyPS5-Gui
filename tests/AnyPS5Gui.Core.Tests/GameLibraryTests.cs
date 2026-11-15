using AnyPS5Gui.Core.Launch;
using AnyPS5Gui.Core.Library;

namespace AnyPS5Gui.Core.Tests;

public class GameLibraryTests
{
    [Fact]
    public void Games_survive_a_restart()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("data", "games.json");
        var library = new GameLibrary(file);
        library.Load();
        Assert.Empty(library.Games);

        library.AddOrUpdate(new GameEntry
        {
            Title = "El Juego", TitleId = "PPSA01234", ExecutablePath = @"D:\Out\Game\PPSA01234.exe",
            ConvertedAt = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero),
            Launch = new LaunchSettings { Fullscreen = true, PresentMode = PresentMode.Mailbox, FpsLimit = 59.94 },
        });

        var reloaded = new GameLibrary(file);
        reloaded.Load();
        var game = Assert.Single(reloaded.Games);
        Assert.Equal("El Juego", game.Title);
        Assert.Equal(@"D:\Out\Game", game.Directory);
        Assert.True(game.Launch.Fullscreen);
        Assert.Equal(PresentMode.Mailbox, game.Launch.PresentMode);
        Assert.Equal(59.94, game.Launch.FpsLimit);
        Assert.Contains("\"Mailbox\"", File.ReadAllText(file));
        Assert.DoesNotContain("IconPath", File.ReadAllText(file));
    }

    [Fact]
    public void Converting_again_updates_the_entry_and_keeps_its_launch_settings()
    {
        using var temp = new TempDirectory();
        var library = new GameLibrary(temp.Combine("games.json"));
        var first = library.AddOrUpdate(new GameEntry { Title = "Old", ExecutablePath = @"D:\Out\Game\app.exe" });
        first.Launch.Fullscreen = true;

        var second = library.AddOrUpdate(new GameEntry { Title = "New", ComponentVersion = "v0.1.5", ExecutablePath = @"d:\out\game\APP.exe" });
        library.AddOrUpdate(new GameEntry { Title = "Other", ExecutablePath = @"D:\Out\Other\app.exe" });

        Assert.Same(first, second);
        Assert.Equal(2, library.Games.Count);
        Assert.Equal("New", first.Title);
        Assert.Equal("v0.1.5", first.ComponentVersion);
        Assert.True(first.Launch.Fullscreen);

        library.Remove(first);
        Assert.Equal("Other", Assert.Single(library.Games).Title);
    }

    [Fact]
    public void A_damaged_file_is_set_aside_instead_of_lost()
    {
        using var temp = new TempDirectory();
        var file = temp.Write("games.json", "{ not json");
        var library = new GameLibrary(file);

        library.Load();

        Assert.Empty(library.Games);
        Assert.Equal("{ not json", File.ReadAllText(file + ".corrupt"));
    }
}
