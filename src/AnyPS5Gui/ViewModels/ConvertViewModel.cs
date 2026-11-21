using System.Collections.ObjectModel;
using System.ComponentModel;
using AnyPS5Gui.Core.Components;
using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Library;
using AnyPS5Gui.Core.Relinker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AnyPS5Gui.ViewModels;

public sealed record IssueItem(InfoBarSeverity Severity, string Message);

public sealed record ForkChoiceItem(string Repository, string Summary)
{
    public string Label => string.IsNullOrWhiteSpace(Summary) ? Repository : $"{Repository}  ·  {Summary}";
    public Uri Uri => new($"https://github.com/{Repository}");
}

/// <summary>The Convert page: pick a game, choose options, run the relinker and lay out the result.</summary>
public partial class ConvertViewModel : ObservableObject
{
    // Changing any of these changes the command line shown to the user.
    private static readonly HashSet<string> CommandInputs =
    [
        nameof(OutputFolder), nameof(SelectedVersion), nameof(ToIntel), nameof(WindowsGui),
        nameof(WindowsDiagnostics), nameof(FrameRedZone), nameof(TraceImports), nameof(Registry),
        nameof(FunctionSymbolRoots), nameof(UnusedFilterIndex), nameof(FixedBase), nameof(SceModulePath),
        nameof(ModuleDirectories), nameof(Executables), nameof(PatchModeIndex), nameof(PatchFile),
        nameof(ExtraArguments),
    ];

    // Typing a path looks at the folder once the typing pauses, not on every key.
    private readonly DispatcherQueueTimer _inspectTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
    private readonly CommunityClient _community = new(App.Http);
    private CancellationTokenSource? _cancellation;
    private bool _outputChosenByUser;
    private bool _settingOutput;
    private GameEntry? _lastConverted;

    public ConvertViewModel()
    {
        _inspectTimer.Interval = TimeSpan.FromMilliseconds(350);
        _inspectTimer.IsRepeating = false;
        _inspectTimer.Tick += (_, _) => Inspect();

        GamePath = "";
        OutputFolder = "";
        FixedBase = "";
        SceModulePath = "";
        ModuleDirectories = "";
        Executables = "";
        PatchFile = "";
        ExtraArguments = "";
        CommandPreview = "";
        StatusText = "";
        ResultMessage = "";
        GameTitle = "";
        GameSubtitle = "";
        GameDetails = "";
        AdviceMessage = "";
        CommunityStatus = "";
        DiscussionUri = new Uri(GameCatalog.DiscussionSearchUrl(null));
        SelectedVersion = Components.Current.Version;

        // Games are built for an AMD CPU; on Intel the relinker has to rewrite the AMD-only instructions.
        ToIntel = App.Host.IsIntel;
        LinkFiles = App.Settings.LinkFiles;

        Components.Changed += (_, _) =>
        {
            if (SelectedVersion is null || !App.Components.IsInstalled(SelectedVersion))
                SelectedVersion = PickVersionForGame(Game);
            OnPropertyChanged(nameof(CurrentComponents));
            NotifyCommands();
            UpdatePreview();
        };

        if (App.Settings.LastGameFolder is { } last && Directory.Exists(last)) OpenGame(last);
    }

    public LogBuffer Log { get; } = new();
    public ObservableCollection<IssueItem> Issues { get; } = [];
    public ObservableCollection<ForkChoiceItem> SuggestedForks { get; } = [];
    public ComponentsViewModel Components => App.ComponentsViewModel;
    public ResolvedComponents CurrentComponents => App.Components.Resolve(App.Settings, SelectedVersion);

    [ObservableProperty] public partial string GamePath { get; set; }
    [ObservableProperty] public partial string OutputFolder { get; set; }
    [ObservableProperty] public partial string? SelectedVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommunityCommand))]
    public partial bool HasGame { get; set; }

    [ObservableProperty] public partial string GameTitle { get; set; }
    [ObservableProperty] public partial string GameSubtitle { get; set; }
    [ObservableProperty] public partial string GameDetails { get; set; }
    [ObservableProperty] public partial ImageSource? Icon { get; set; }
    [ObservableProperty] public partial Uri DiscussionUri { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdvice))]
    public partial string AdviceMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommunityStatus))]
    public partial string CommunityStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommunityCommand))]
    public partial bool IsCheckingCommunity { get; set; }

    public bool HasAdvice => AdviceMessage.Length > 0;
    public bool HasCommunityStatus => CommunityStatus.Length > 0;
    public bool HasSuggestedForks => SuggestedForks.Count > 0;
    public bool CanCheckCommunity => HasGame && !IsCheckingCommunity;

    [ObservableProperty] public partial bool ToIntel { get; set; }
    [ObservableProperty] public partial bool WindowsGui { get; set; }
    [ObservableProperty] public partial bool LinkFiles { get; set; }

    [ObservableProperty] public partial int UnusedFilterIndex { get; set; }
    [ObservableProperty] public partial bool WindowsDiagnostics { get; set; }
    [ObservableProperty] public partial bool FrameRedZone { get; set; }
    [ObservableProperty] public partial bool TraceImports { get; set; }
    [ObservableProperty] public partial bool Registry { get; set; }
    [ObservableProperty] public partial bool FunctionSymbolRoots { get; set; }
    [ObservableProperty] public partial string FixedBase { get; set; }
    [ObservableProperty] public partial string SceModulePath { get; set; }
    [ObservableProperty] public partial string ModuleDirectories { get; set; }
    [ObservableProperty] public partial string Executables { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatchFileEnabled))]
    public partial int PatchModeIndex { get; set; }

    [ObservableProperty] public partial string PatchFile { get; set; }
    [ObservableProperty] public partial string ExtraArguments { get; set; }

    [ObservableProperty] public partial string CommandPreview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial bool IsIndeterminate { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; }

    [ObservableProperty] public partial bool ResultOpen { get; set; }
    [ObservableProperty] public partial InfoBarSeverity ResultSeverity { get; set; }
    [ObservableProperty] public partial string ResultMessage { get; set; }
    [ObservableProperty] public partial bool CanShowInGames { get; set; }

    public GameInfo? Game { get; private set; }
    public bool IsIdle => !IsBusy;
    public bool PatchFileEnabled => PatchModeIndex == (int)PatchMode.File;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && CommandInputs.Contains(e.PropertyName)) UpdatePreview();
    }

    partial void OnGamePathChanged(string value)
    {
        _inspectTimer.Stop();
        _inspectTimer.Start();
    }

    /// <summary>A game chosen in one go (browsed for, dropped, remembered) rather than typed: shown at once.</summary>
    public void OpenGame(string path, string? preferredVersion = null)
    {
        GamePath = path;
        _inspectTimer.Stop();
        Inspect();
        if (preferredVersion is not null && App.Components.IsInstalled(preferredVersion))
            SelectedVersion = preferredVersion;
    }

    partial void OnOutputFolderChanged(string value)
    {
        // An emptied box goes back to following the game.
        if (!_settingOutput) _outputChosenByUser = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnLinkFilesChanged(bool value)
    {
        if (App.Settings.LinkFiles == value) return;
        App.Settings.LinkFiles = value;
        App.SaveSettings();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    /// <summary>Called when the default output folder in the settings changes.</summary>
    public void RefreshDefaultOutput()
    {
        if (!_outputChosenByUser && Game is { CanConvert: true } game)
            SetOutput(OutputNaming.DefaultOutputDirectory(game, App.Settings.OutputRoot));
    }

    [RelayCommand]
    private async Task BrowseGameFolderAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) OpenGame(folder);
    }

    [RelayCommand]
    private async Task BrowseGameFileAsync()
    {
        if (await Pickers.PickFileAsync("*") is { } file) OpenGame(file);
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) OutputFolder = folder;
    }

    [RelayCommand]
    private async Task BrowsePatchFileAsync()
    {
        if (await Pickers.PickFileAsync(".toml") is not { } file) return;
        PatchFile = file;
        PatchModeIndex = (int)PatchMode.File;
    }

    [RelayCommand]
    private async Task BrowseModuleParentAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) SceModulePath = folder;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ConvertAsync() => RunAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRunAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    public void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void ShowInGames()
    {
        App.Window.Navigate(MainWindow.GamesTag);
        if (_lastConverted is not null) App.GamesViewModel.Select(_lastConverted);
    }

    [RelayCommand(CanExecute = nameof(CanCheckCommunity))]
    private async Task CheckCommunityAsync()
    {
        if (Game is not { } game) return;
        IsCheckingCommunity = true;
        CommunityStatus = Loc.Get("Convert_CommunityChecking");
        try
        {
            CommunityReport report;
            try
            {
                report = await _community.LookupGameAsync(game.Param?.TitleId, game.DisplayName);
            }
            catch (HttpRequestException) when (!ReferenceEquals(Game, game))
            {
                return;
            }
            if (!ReferenceEquals(Game, game)) return;
            if (report.Discussions.FirstOrDefault() is { } discussion)
            {
                DiscussionUri = new Uri(discussion.Url);
                CommunityStatus = discussion.Status is { Length: > 0 } status
                    ? Loc.Format("Convert_CommunityDiscussionStatus", discussion.Title, status, discussion.Comments)
                    : Loc.Format("Convert_CommunityDiscussionFound", discussion.Title, discussion.Comments);
            }
            else
            {
                CommunityStatus = report.Forks.Count > 0
                    ? Loc.Format("Convert_CommunityForksFound", report.Forks.Count)
                    : Loc.Get("Convert_CommunityNothingFound");
            }

            foreach (var fork in report.Forks)
            {
                if (SuggestedForks.Any(existing =>
                        existing.Repository.Equals(fork.Repository, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var summary = !string.IsNullOrWhiteSpace(fork.Description)
                    ? fork.Description
                    : SettingsViewModel.DescribeFork(fork.Repository);
                SuggestedForks.Add(new ForkChoiceItem(fork.Repository, summary));
            }
            OnPropertyChanged(nameof(HasSuggestedForks));
        }
        catch (HttpRequestException e)
        {
            CommunityStatus = e.Message;
        }
        finally
        {
            IsCheckingCommunity = false;
        }
    }

    [RelayCommand]
    private async Task UseForkAsync(ForkChoiceItem? fork)
    {
        if (fork is null) return;
        var installed = App.Components.InstalledVersions().FirstOrDefault(version =>
            ComponentStore.RepositoryOf(version).Equals(fork.Repository, StringComparison.OrdinalIgnoreCase));
        if (installed is not null)
        {
            SelectedVersion = installed;
            CommunityStatus = Loc.Format("Convert_UsingFork", ComponentStore.VersionLabel(installed));
            return;
        }

        await Components.UseRepositoryAsync(fork.Repository);
        var downloaded = App.Components.InstalledVersions().FirstOrDefault(version =>
            ComponentStore.RepositoryOf(version).Equals(fork.Repository, StringComparison.OrdinalIgnoreCase));
        if (downloaded is not null)
        {
            SelectedVersion = downloaded;
            CommunityStatus = Loc.Format("Convert_UsingFork", ComponentStore.VersionLabel(downloaded));
        }
        else
        {
            CommunityStatus = Components.HasError ? Components.Error : Components.Status;
        }
    }

    private bool CanRun => IsIdle && Game is { CanConvert: true } && !string.IsNullOrWhiteSpace(OutputFolder) &&
                           CurrentComponents.HasRelinker;

    private void NotifyCommands()
    {
        ConvertCommand.NotifyCanExecuteChanged();
        DryRunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Inspect()
    {
        Issues.Clear();
        Game = null;
        if (!string.IsNullOrWhiteSpace(GamePath))
        {
            try
            {
                Game = GameFolder.Inspect(GamePath);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException
                                          or NotSupportedException)
            {
                Issues.Add(new IssueItem(InfoBarSeverity.Error, e.Message));
            }
        }

        if (Game is { } game)
        {
            foreach (var issue in game.Issues)
            {
                // One line says it for a game that comes in containers, executable and modules alike.
                if (issue == GameIssue.WrappedModules && game.Kind == ExecutableKind.Self) continue;
                var severity = issue switch
                {
                    GameIssue.NoExecutable or GameIssue.NotAnElf => InfoBarSeverity.Error,
                    // Nothing to do about these: the conversion unwraps them.
                    GameIssue.WrappedExecutable or GameIssue.WrappedModules => InfoBarSeverity.Informational,
                    _ => InfoBarSeverity.Warning,
                };
                Issues.Add(new IssueItem(severity, Loc.Format("Issue_" + issue, game.WrappedModules)));
            }
            if (game.HasPatchFile) Issues.Add(new IssueItem(InfoBarSeverity.Informational, Loc.Get("Convert_PatchFileFound")));

            if (game.CanConvert)
            {
                if (!_outputChosenByUser) SetOutput(OutputNaming.DefaultOutputDirectory(game, App.Settings.OutputRoot));
                if (App.Settings.LastGameFolder != game.Folder)
                {
                    App.Settings.LastGameFolder = game.Folder;
                    App.SaveSettings();
                }
            }
        }

        var shown = Game is { CanConvert: true } ? Game : null;
        HasGame = shown is not null;
        GameTitle = shown?.DisplayName ?? "";
        GameSubtitle = shown?.Param is { } param
            ? string.Join("  ·  ", new[] { param.TitleId, param.ContentVersion }.Where(part => part is not null))
            : "";
        GameDetails = shown is null
            ? ""
            : Loc.Format("Convert_GameDetails", shown.Executable,
                shown.ModuleDirectories.Count > 0 ? string.Join(", ", shown.ModuleDirectories) : "—");
        Icon = Images.Load(shown?.IconPath);
        DiscussionUri = new Uri(GameCatalog.DiscussionSearchUrl(shown?.Param?.TitleId, shown?.DisplayName));

        CommunityStatus = "";
        SuggestedForks.Clear();
        if (GameCatalog.Find(shown) is { } profile)
        {
            AdviceMessage = DescribeProfile(profile);
            foreach (var repository in profile.RecommendedForks)
                SuggestedForks.Add(new ForkChoiceItem(repository, SettingsViewModel.DescribeFork(repository)));
        }
        else
        {
            AdviceMessage = "";
        }
        OnPropertyChanged(nameof(HasSuggestedForks));

        if (shown is not null) SelectedVersion = PickVersionForGame(shown);

        NotifyCommands();
        UpdatePreview();
    }

    private static string DescribeProfile(GameProfile profile) => profile.SummaryKey switch
    {
        "Convert_AdviceGhostOfYotei" => Loc.Get("Convert_AdviceGhostOfYotei"),
        _ => "",
    };

    private string? PickVersionForGame(GameInfo? game)
    {
        var installed = App.Components.InstalledVersions();
        if (GameCatalog.RecommendInstalledVersion(game, installed) is { } recommended)
            return recommended;

        if (game is not null)
        {
            var previous = App.Library.Games.FirstOrDefault(entry =>
                (!string.IsNullOrEmpty(entry.SourceFolder) &&
                 string.Equals(Path.GetFullPath(entry.SourceFolder), Path.GetFullPath(game.Folder), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(entry.TitleId) &&
                 string.Equals(entry.TitleId, game.Param?.TitleId, StringComparison.OrdinalIgnoreCase)));
            if (previous?.ComponentVersion is { } previousVersion && App.Components.IsInstalled(previousVersion))
                return previousVersion;
        }

        return Components.Current.Version;
    }

    private void SetOutput(string folder)
    {
        _settingOutput = true;
        OutputFolder = Path.GetFullPath(folder);
        _settingOutput = false;
    }

    private RelinkerOptions BuildOptions(bool dryRun) => new()
    {
        DryRun = dryRun,
        ToIntel = ToIntel,
        WindowsGui = WindowsGui,
        WindowsDiagnostics = WindowsDiagnostics,
        FrameRedZone = FrameRedZone,
        TraceImports = TraceImports,
        Registry = Registry,
        FunctionSymbolRoots = FunctionSymbolRoots,
        UnusedFilter = UnusedFilterIndex,
        WindowsFixedBase = FixedBase,
        SceModulePath = SceModulePath,
        ModuleDirectories = RelinkerCommand.SplitLines(ModuleDirectories),
        Executables = RelinkerCommand.SplitLines(Executables),
        PatchMode = (PatchMode)Math.Clamp(PatchModeIndex, 0, 2),
        PatchFile = PatchFile,
        ExtraArguments = ExtraArguments,
    };

    private ConversionRequest? BuildRequest(bool dryRun)
    {
        if (Game is not { CanConvert: true } game || string.IsNullOrWhiteSpace(OutputFolder)) return null;
        var components = CurrentComponents;
        return new ConversionRequest
        {
            Game = game,
            OutputDirectory = OutputFolder.Trim().Trim('"'),
            ExecutableName = OutputNaming.ExecutableName(game),
            Options = BuildOptions(dryRun),
            RelinkerPath = components.RelinkerPath ?? "relinker.exe",
            LibsPath = components.LibsPath,
            LinkFiles = LinkFiles,
        };
    }

    private void UpdatePreview()
    {
        try
        {
            CommandPreview = BuildRequest(dryRun: false) is { } request
                ? RelinkerCommand.Format(Path.GetFileName(request.RelinkerPath), Converter.BuildArguments(request))
                : "";
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            CommandPreview = e.Message;
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        ConversionRequest? request;
        try
        {
            request = BuildRequest(dryRun);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowResult(InfoBarSeverity.Error, e.Message);
            return;
        }
        if (request is null) return;

        Log.Clear();
        Log.StartFile((dryRun ? "dryrun-" : "convert-") + (request.Game.Param?.TitleId ?? request.Game.DisplayName));
        ResultOpen = false;
        IsBusy = true;
        IsIndeterminate = true;
        Progress = 0;
        using var cancellation = _cancellation = new CancellationTokenSource();
        try
        {
            Log.Add(LogKind.Info, RelinkerCommand.Format(request.RelinkerPath, Converter.BuildArguments(request)));

            var result = await Converter.RunAsync(request, Log.AddOutput, Log.AddOutput,
                new Progress<ConversionProgress>(Report), cancellation.Token);

            if (!result.Succeeded)
            {
                Finish(InfoBarSeverity.Error, Loc.Format("Convert_Failed", result.ExitCode));
            }
            else if (dryRun)
            {
                Finish(InfoBarSeverity.Success, Loc.Get("Convert_DryRunDone"));
            }
            else
            {
                if (result.Libraries is { } libraries) Log.Add(LogKind.Info, Loc.Format("Convert_LibrariesPlaced", Describe(libraries)));
                else Log.Add(LogKind.Warning, Loc.Get("Convert_NoLibraries"));
                if (result.GameFiles is { } gameFiles) Log.Add(LogKind.Info, Loc.Format("Convert_GameFilesPlaced", Describe(gameFiles)));

                var entry = new GameEntry
                {
                    Title = request.Game.DisplayName,
                    TitleId = request.Game.Param?.TitleId,
                    ContentVersion = request.Game.Param?.ContentVersion,
                    ExecutablePath = Path.GetFullPath(request.OutputExecutable),
                    SourceFolder = request.Game.Folder,
                    ComponentVersion = CurrentComponents.Version,
                    ConvertedAt = DateTimeOffset.Now,
                };
                if (GameCatalog.Find(request.Game) is { RecommendMimalloc: true })
                    entry.Launch.Mimalloc = true;

                _lastConverted = App.Library.AddOrUpdate(entry);
                App.GamesViewModel.Refresh();
                Finish(InfoBarSeverity.Success, Loc.Format("Convert_Done", request.OutputExecutable), canShowInGames: true);
            }
        }
        catch (OperationCanceledException)
        {
            Finish(InfoBarSeverity.Warning, Loc.Get("Convert_Cancelled"));
        }
        catch (ConversionException e)
        {
            Finish(InfoBarSeverity.Error, Loc.Format("Problem_" + e.Problem, e.Detail));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception
                                      or ArgumentException or NotSupportedException)
        {
            Finish(InfoBarSeverity.Error, e.Message);
        }
        finally
        {
            Log.EndFile();
            _cancellation = null;
            IsIndeterminate = false;
            Progress = 0;
            StatusText = "";
            IsBusy = false;
        }
    }

    private void Report(ConversionProgress progress)
    {
        var stage = Loc.Get("Stage_" + progress.Stage);
        IsIndeterminate = progress.Total == 0;
        Progress = progress.Total > 0 ? 100.0 * progress.Done / progress.Total : 0;
        StatusText = progress.Total > 0 ? $"{stage} {progress.Done:N0} / {progress.Total:N0}" : stage;
    }

    private void Finish(InfoBarSeverity severity, string message, bool canShowInGames = false)
    {
        Log.Add(severity switch
        {
            InfoBarSeverity.Success => LogKind.Success,
            InfoBarSeverity.Warning => LogKind.Warning,
            _ => LogKind.Error,
        }, message);
        ShowResult(severity, message, canShowInGames);
    }

    private void ShowResult(InfoBarSeverity severity, string message, bool canShowInGames = false)
    {
        ResultSeverity = severity;
        ResultMessage = message;
        CanShowInGames = canShowInGames;
        ResultOpen = true;
    }

    private static string Describe(MirrorResult result) =>
        Loc.Format("Convert_MirrorSummary", result.Linked, result.Copied, result.Skipped);
}
