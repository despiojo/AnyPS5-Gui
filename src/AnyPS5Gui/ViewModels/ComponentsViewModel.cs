using System.Collections.ObjectModel;
using AnyPS5Gui.Core.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnyPS5Gui.ViewModels;

/// <summary>The AnyPS5 relinker and libraries: which versions exist, which are downloaded, which one is used.</summary>
public partial class ComponentsViewModel : ObservableObject
{
    private readonly ReleaseClient _client = new(App.Http);
    private CancellationTokenSource? _cancellation;
    private bool _refreshing;

    public ComponentsViewModel()
    {
        Status = "";
        Error = "";
        RefreshInstalled();
    }

    /// <summary>Raised when the relinker a conversion would use may have changed.</summary>
    public event EventHandler? Changed;

    public ObservableCollection<ReleaseInfo> Releases { get; } = [];
    public ObservableCollection<string> Installed { get; } = [];

    [ObservableProperty]
    public partial ReleaseInfo? SelectedRelease { get; set; }

    [ObservableProperty]
    public partial string? ActiveVersion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(HasDetails))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand), nameof(CancelCommand), nameof(RemoveActiveCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial string Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasDetails))]
    public partial string Error { get; set; }

    public bool IsIdle => !IsBusy;
    public bool HasError => Error.Length > 0;

    /// <summary>There is progress, a result or an error worth a line under the download button.</summary>
    public bool HasDetails => IsBusy || HasError || Status.Length > 0;
    public bool HasInstalled => Installed.Count > 0;

    public ResolvedComponents Current => App.Components.Resolve(App.Settings);

    /// <summary>Nothing to convert with yet: the Convert page offers the download.</summary>
    public bool NeedsInstall => !Current.HasRelinker;

    public string Summary
    {
        get
        {
            var current = Current;
            if (!current.HasRelinker) return Loc.Get("Components_NotInstalled");
            var custom = !string.IsNullOrWhiteSpace(App.Settings.CustomRelinkerPath) &&
                         current.RelinkerPath == App.Settings.CustomRelinkerPath;
            return custom
                ? Loc.Get("Components_Custom")
                : Loc.Format("Components_Using", ComponentStore.VersionLabel(current.Version!));
        }
    }

    public void RefreshInstalled()
    {
        _refreshing = true;
        Installed.Clear();
        foreach (var version in App.Components.InstalledVersions()) Installed.Add(version);
        ActiveVersion = null;
        ActiveVersion = Current.Version;
        _refreshing = false;
        NotifyChanged();
    }

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(NeedsInstall));
        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(Summary));
        RemoveActiveCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveVersionChanged(string? value)
    {
        if (_refreshing || value is null) return;
        App.Settings.ActiveVersion = value;
        App.SaveSettings();
        NotifyChanged();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckAsync()
    {
        await RunAsync(LoadReleasesAsync);
    }

    /// <summary>Downloads the selected release, or the one a check for versions would select when none has been chosen.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task InstallAsync()
    {
        await RunAsync(async cancellation =>
        {
            if (SelectedRelease is null) await LoadReleasesAsync(cancellation);
            if (SelectedRelease is not { } release)
            {
                // Whatever the check found wrong says more than "nothing found".
                if (!HasError) Error = Loc.Get("Components_NoRelease");
                Status = "";
                return;
            }

            var progress = new Progress<InstallProgress>(Report);
            await App.Components.InstallAsync(release, App.Http, progress, cancellation);
            App.Settings.ActiveVersion = release.VersionName;
            App.SaveSettings();
            RefreshInstalled();
            Status = Loc.Format("Components_Installed", release.Label);
        });
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRemoveActive))]
    private void RemoveActive()
    {
        if (ActiveVersion is not { } version) return;
        try
        {
            App.Components.Remove(version);
            Error = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
        App.Settings.ActiveVersion = null;
        App.SaveSettings();
        RefreshInstalled();
    }

    private bool CanRemoveActive => IsIdle && ActiveVersion is not null;

    private async Task LoadReleasesAsync(CancellationToken cancellation)
    {
        Status = Loc.Get("Components_Checking");
        IsIndeterminate = true;
        var official = await _client.GetReleasesAsync(cancellationToken: cancellation);

        IReadOnlyList<ReleaseInfo> other = [];
        ReleaseInfo? latestOther = null;
        var prefer = false;
        string? sourceError = null;
        if (App.Settings.ExtraReleaseSource is { Length: > 0 } source &&
            !source.Equals(ReleaseClient.Repository, StringComparison.OrdinalIgnoreCase))
        {
            prefer = App.Settings.PreferExtraReleaseSource;
            if (!ReleaseClient.IsValidRepository(source))
            {
                sourceError = Loc.Format("Components_BadSource", source);
            }
            else
            {
                // The source failing, or not answering in time, leaves AnyPS5's own releases to choose from.
                try
                {
                    other = await _client.GetReleasesAsync(source, cancellation);
                    if (prefer) latestOther = await _client.GetLatestReleaseAsync(source, cancellation);
                }
                catch (HttpRequestException e)
                {
                    sourceError = Loc.Format("Components_SourceFailed", source, e.Message);
                }
            }
        }

        var offer = ReleaseOffer.Create(official, other, prefer, latestOther);
        Releases.Clear();
        foreach (var release in offer.Releases) Releases.Add(release);
        SelectedRelease = offer.Default;

        // A preferred source with no latest release to take leaves the choice to the list: a download
        // takes none on its own, not even AnyPS5's newest, which is not what was asked for.
        if (prefer && offer.Default is null) sourceError ??= Loc.Format("Components_NoLatest", App.Settings.ExtraReleaseSource);
        Status = offer.Default is { } latest
            ? Loc.Format(App.Components.IsInstalled(latest.VersionName) ? "Components_UpToDate" : "Components_Available", latest.Label)
            : prefer ? "" : Loc.Get("Components_NoRelease");
        if (sourceError is not null) Error = sourceError;
    }

    /// <summary>The list was read from sources that are no longer the ones in the settings.</summary>
    public void ForgetReleases()
    {
        Releases.Clear();
        SelectedRelease = null;
        Status = "";
        Error = "";
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        IsBusy = true;
        IsIndeterminate = true;
        Progress = 0;
        Error = "";
        using var cancellation = _cancellation = new CancellationTokenSource();
        try
        {
            await work(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = Loc.Get("Components_Cancelled");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException
                                      or UnauthorizedAccessException or InvalidOperationException
                                      or System.Text.Json.JsonException)
        {
            Status = "";
            Error = e.Message;
        }
        finally
        {
            _cancellation = null;
            IsIndeterminate = false;
            IsBusy = false;
        }
    }

    private void Report(InstallProgress progress)
    {
        IsIndeterminate = progress.Total <= 0;
        Progress = progress.Total > 0 ? 100.0 * progress.Done / progress.Total : 0;
        Status = progress.Stage == InstallStage.Extracting
            ? Loc.Format("Install_Extracting", progress.Done, progress.Total)
            : Loc.Format("Install_" + progress.Stage, Megabytes(progress.Done), Megabytes(progress.Total));
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");
}
