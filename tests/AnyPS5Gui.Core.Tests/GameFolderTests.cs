using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;

namespace AnyPS5Gui.Core.Tests;

public class GameFolderTests
{
    private const string Param = """
        {
          "titleId": "PPSA01234",
          "contentVersion": "01.002.000",
          "localizedParameters": {
            "defaultLanguage": "es-ES",
            "en-US": { "titleName": "The Game" },
            "es-ES": { "titleName": "El Juego" }
          }
        }
        """;

    [Fact]
    public void A_complete_dump_has_no_issues()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("eboot.bin", Magic.Elf);
        temp.WriteBytes(@"sce_module\libc.prx", Magic.Elf);
        temp.Write(@"sce_sys\param.json", Param);
        temp.Write(@"sce_sys\icon0.png");
        temp.Write("relinker-patches.toml", "format = 1");

        var game = GameFolder.Inspect(temp.Path);

        Assert.True(game.CanConvert);
        Assert.Equal(temp.Combine("eboot.bin"), game.Executable);
        Assert.Equal(ExecutableKind.Elf, game.Kind);
        Assert.Equal(new ParamJson("PPSA01234", "El Juego", "01.002.000"), game.Param);
        Assert.Equal("El Juego", game.DisplayName);
        Assert.Equal(temp.Combine("sce_sys", "icon0.png"), game.IconPath);
        Assert.Equal(["sce_module"], game.ModuleDirectories);
        Assert.True(game.HasPatchFile);
        Assert.Empty(game.Issues);
    }

    [Fact]
    public void A_wrapped_executable_and_modules_are_reported()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("eboot.bin", Magic.Self);
        temp.WriteBytes(@"sce_module\a.prx", Magic.Self);
        temp.WriteBytes(@"sce_module\b.prx", Magic.Elf);
        temp.WriteBytes(@"sce_module\deeper\c.prx", Magic.Self);

        var game = GameFolder.Inspect(temp.Path);

        Assert.True(game.CanConvert);
        Assert.Equal(ExecutableKind.Self, game.Kind);
        Assert.Equal(1, game.WrappedModules);
        Assert.Equal([GameIssue.WrappedExecutable, GameIssue.WrappedModules], game.Issues);
    }

    [Fact]
    public void Module_directory_problems()
    {
        using var temp = new TempDirectory();
        temp.Write("eboot.bin", "not an executable");
        Assert.Equal([GameIssue.NotAnElf, GameIssue.NoModuleDirectory], GameFolder.Inspect(temp.Path).Issues);

        Directory.CreateDirectory(temp.Combine("sce_module"));
        Directory.CreateDirectory(temp.Combine("sce_modules"));
        Directory.CreateDirectory(temp.Combine("prx"));
        var game = GameFolder.Inspect(temp.Path);
        Assert.Equal(["sce_module", "sce_modules", "prx"], game.ModuleDirectories);
        Assert.Equal([GameIssue.NotAnElf, GameIssue.BothModuleDirectories], game.Issues);
    }

    [Fact]
    public void A_folder_without_an_executable_cannot_be_converted()
    {
        using var temp = new TempDirectory();
        temp.Write("readme.txt");

        foreach (var path in new[] { temp.Path, temp.Combine("missing") })
        {
            var game = GameFolder.Inspect(path);
            Assert.False(game.CanConvert);
            Assert.Equal([GameIssue.NoExecutable], game.Issues);
        }
    }

    [Fact]
    public void The_executable_can_be_given_directly_or_be_the_only_elf()
    {
        using var temp = new TempDirectory();
        var homebrew = temp.WriteBytes(@"app\homebrew.elf", Magic.Elf);

        Assert.Equal(homebrew, GameFolder.Inspect(temp.Combine("app")).Executable);
        var quoted = GameFolder.Inspect($"\"{homebrew}\"");
        Assert.Equal(homebrew, quoted.Executable);
        Assert.Equal(temp.Combine("app"), quoted.Folder);
        Assert.Equal("app", quoted.DisplayName);

        temp.WriteBytes(@"app\second.elf", Magic.Elf);
        Assert.Null(GameFolder.Inspect(temp.Combine("app")).Executable);
    }

    [Theory]
    [InlineData("{\"titleId\":\"PPSA1\",\"localizedParameters\":{\"en-US\":{\"titleName\":\"A\"},\"ja-JP\":{\"titleName\":\"B\"}}}", "A")]
    [InlineData("{\"defaultLanguage\":\"ja-JP\",\"localizedParameters\":{\"en-US\":{\"titleName\":\"A\"},\"ja-JP\":{\"titleName\":\"B\"}}}", "B")]
    [InlineData("{\"localizedParameters\":{\"defaultLanguage\":\"fr-FR\",\"ja-JP\":{\"titleName\":\"B\"}}}", "B")]
    [InlineData("{\"localizedParameters\":{\"en-US\":{\"titleName\":\"  \"}}}", null)]
    [InlineData("{\"titleId\":\"PPSA1\"}", null)]
    public void Title_name_selection(string json, string? expected)
    {
        Assert.Equal(expected, ParamJson.Parse(json)?.TitleName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[1]")]
    [InlineData("{ broken")]
    public void Unusable_param_json_is_ignored(string json)
    {
        Assert.Null(ParamJson.Parse(json));
    }

    [Fact]
    public void Output_names()
    {
        var game = new GameInfo { Folder = @"D:\Dumps\PPSA01234-app\", Param = new ParamJson("PPSA01234", "Some: Game?", "01.000.000") };
        var plain = new GameInfo { Folder = @"D:\Dumps\homebrew", Param = new ParamJson("../evil", null, null) };

        Assert.Equal("PPSA01234.exe", OutputNaming.ExecutableName(game));
        Assert.Equal("app.exe", OutputNaming.ExecutableName(plain));
        Assert.Equal(@"D:\Dumps\PPSA01234-app - AnyPS5", OutputNaming.DefaultOutputDirectory(game, null));
        Assert.Equal(@"E:\Games\Some Game [PPSA01234]", OutputNaming.DefaultOutputDirectory(game, @"E:\Games"));
        Assert.Equal(@"E:\Games\homebrew [.. evil]", OutputNaming.DefaultOutputDirectory(plain, @"E:\Games"));
        Assert.Equal("Game", OutputNaming.SanitizeFileName(" ?. "));
    }
}
