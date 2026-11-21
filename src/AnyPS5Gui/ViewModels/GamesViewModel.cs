using System.Collections.ObjectModel;
using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Launch;
using AnyPS5Gui.Core.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnyPS5Gui.ViewModels;

/// <summary>The Games page: the converted games and the one being looked at.</summary>
public partial class GamesViewModel : ObservableObject
{
    public GamesViewModel()
    {
        Languages = [Loc.Get("Games_LanguageDefault"), Loc.Get("Games_LanguageAuto"), .. ConsoleLanguages.Names];
        Refresh();
        Selected = Games.FirstOrDefault();
        Components.Changed += (_, _) =>
        {
            foreach (var game in Games) game.EntryChanged();
        };
    }

    public ComponentsViewModel Components => App.ComponentsViewModel;
    public ObservableCollection<GameItemViewModel> Games { get; } = [];

    /// <summary>Entries of the language box, in the order <see cref="GameItemViewModel.LanguageIndex"/> counts.</summary>
    public string[] Languages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial GameItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Games.Count == 0;

    /// <summary>Brings the list in line with the library after a conversion.</summary>
    public void Refresh()
    {
        foreach (var entry in App.Library.Games)
        {
            if (Games.FirstOrDefault(game => game.Entry == entry) is { } existing) existing.EntryChanged();
            else Games.Add(new GameItemViewModel(entry));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Select(GameEntry entry) => Selected = Games.FirstOrDefault(game => game.Entry == entry) ?? Selected;

    public void StopAll()
    {
        foreach (var game in Games) game.Stop();
    }

    /// <summary>Lists a game converted earlier, or outside the app, by its executable.</summary>
    [RelayCommand]
    private async Task AddExistingAsync()
    {
        if (await Pickers.PickFileAsync(".exe") is not { } executable) return;

        var folder = Path.GetDirectoryName(executable)!;
        var param = ParamJson.Read(Path.Combine(folder, "app0", "sce_sys", "param.json"));
        var entry = App.Library.AddOrUpdate(new GameEntry
        {
            Title = param?.TitleName ?? Path.GetFileName(folder),
            TitleId = param?.TitleId,
            ContentVersion = param?.ContentVersion,
            ExecutablePath = executable,
        });
        Refresh();
        Select(entry);
    }

    /// <summary>Takes the game off the list. Its files are not touched.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is not { } game) return;
        game.Stop();
        var index = Games.IndexOf(game);
        App.Library.Remove(game.Entry);
        Games.Remove(game);
        Selected = Games.Count == 0 ? null : Games[Math.Min(index, Games.Count - 1)];
        OnPropertyChanged(nameof(IsEmpty));
    }
}
