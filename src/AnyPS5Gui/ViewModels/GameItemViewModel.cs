using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AnyPS5Gui.Core;
using AnyPS5Gui.Core.Components;
using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Launch;
using AnyPS5Gui.Core.Library;
using AnyPS5Gui.Core.Relinker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;

namespace AnyPS5Gui.ViewModels;

/// <summary>One converted game: how it is started, whether it is running, and what it printed.</summary>
public partial class GameItemViewModel : ObservableObject
{
    private static readonly string[] Resolutions = ["", "1920x1080", "3840x2160"];

    private CancellationTokenSource? _cancellation;

    public GameItemViewModel(GameEntry entry)
    {
        Entry = entry;
        Icon = Images.Load(entry.IconPath);
        SelectedLibraryVersion = PickLibraryVersion();
    }

    public GameEntry Entry { get; }
    public LogBuffer Log { get; } = new();
    public ImageSource? Icon { get; }

    public string Title => Entry.Title;

    // What a screen reader announces for the game in the list.
    public override string ToString() => Title;
    public string Directory => Entry.Directory;
    public Uri DiscussionUri => new(GameCatalog.DiscussionSearchUrl(Entry.TitleId, Entry.Title));

    public string Subtitle => string.Join("  ·  ",
        new[]
            {
                Entry.TitleId, Entry.ContentVersion,
                Entry.ComponentVersion is { } version ? "AnyPS5 " + ComponentStore.VersionLabel(version) : null,
            }
            .Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>The variables the game will be started with, so nothing the app does is hidden.</summary>
    public string EnvironmentPreview
    {
        get
        {
            var environment = Launch.ToEnvironment();
            return environment.Count == 0
                ? Loc.Get("Games_NoEnvironment")
                : string.Join(Environment.NewLine, environment.Select(pair => $"{pair.Key}={pair.Value}"));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand), nameof(StopCommand), nameof(ReconvertCommand), nameof(SwitchLibrariesCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchLibrariesCommand))]
    public partial string? SelectedLibraryVersion { get; set; }

    public bool IsIdle => !IsRunning;
    public bool CanReconvert => IsIdle && (!string.IsNullOrWhiteSpace(Entry.SourceFolder) ||
                                           System.IO.Directory.Exists(Path.Combine(Entry.Directory, "app0")));
    public bool CanSwitchLibraries => IsIdle && SelectedLibraryVersion is { } version && App.Components.IsInstalled(version);

    private LaunchSettings Launch => Entry.Launch;

    public bool Fullscreen { get => Launch.Fullscreen; set => Set(Launch.Fullscreen, value, v => Launch.Fullscreen = v); }

    /// <summary>NaN is the number box left empty: the first display.</summary>
    public double DisplayIndex
    {
        get => Launch.DisplayIndex ?? double.NaN;
        set => Set(Launch.DisplayIndex, double.IsNaN(value) || value < 0 ? (int?)null : (int)value, v => Launch.DisplayIndex = v);
    }

    public string Gpu { get => Launch.Gpu ?? ""; set => Set(Launch.Gpu ?? "", value, v => Launch.Gpu = v); }

    public int PresentModeIndex
    {
        get => (int)Launch.PresentMode;
        set => SetIndex(value, () => Set(Launch.PresentMode, (PresentMode)Math.Clamp(value, 0, 3), v => Launch.PresentMode = v));
    }

    public double FpsLimit
    {
        get => Launch.FpsLimit;
        set => Set(Launch.FpsLimit, double.IsNaN(value) || value < 0 ? 0 : value, v => Launch.FpsLimit = v);
    }

    public bool Fsr { get => Launch.Fsr; set => Set(Launch.Fsr, value, v => Launch.Fsr = v); }

    public double FsrSharpness
    {
        get => Launch.FsrSharpness;
        set => Set(Launch.FsrSharpness, Math.Round(value, 1), v => Launch.FsrSharpness = v);
    }

    public int OutputResolutionIndex
    {
        get => Math.Max(0, Array.IndexOf(Resolutions, Launch.OutputResolution ?? ""));
        set => SetIndex(value, () => Set(Launch.OutputResolution ?? "", Resolutions[Math.Clamp(value, 0, Resolutions.Length - 1)],
            v => Launch.OutputResolution = v.Length == 0 ? null : v));
    }

    /// <summary>0: AnyPS5's default. 1: follow Windows. From 2 on: a console language, by its number plus 2.</summary>
    public int LanguageIndex
    {
        get => Launch.Language switch
        {
            null or "" => 0,
            "auto" => 1,
            var number => int.TryParse(number, out var index) && index >= 0 && index < ConsoleLanguages.Names.Count ? index + 2 : 0,
        };
        set => SetIndex(value, () => Set(LanguageIndex, value, v => Launch.Language = v switch
        {
            <= 0 => null,
            1 => "auto",
            _ => (v - 2).ToString(),
        }));
    }

    public double Volume { get => Launch.Volume; set => Set(Launch.Volume, (int)Math.Round(value), v => Launch.Volume = v); }

    public bool MuteOnFocusLoss
    {
        get => Launch.MuteOnFocusLoss;
        set => Set(Launch.MuteOnFocusLoss, value, v => Launch.MuteOnFocusLoss = v);
    }

    public double Rumble { get => Launch.Rumble; set => Set(Launch.Rumble, (int)Math.Round(value), v => Launch.Rumble = v); }

    public bool DisableShaderCache
    {
        get => Launch.DisableShaderCache;
        set => Set(Launch.DisableShaderCache, value, v => Launch.DisableShaderCache = v);
    }

    public bool HostReport { get => Launch.HostReport; set => Set(Launch.HostReport, value, v => Launch.HostReport = v); }
    public bool Mimalloc { get => Launch.Mimalloc; set => Set(Launch.Mimalloc, value, v => Launch.Mimalloc = v); }

    public bool DisableDozen
    {
        get => Launch.DisableDozen;
        set => Set(Launch.DisableDozen, value, v => Launch.DisableDozen = v);
    }

    public string CustomEnvironment
    {
        get => Launch.CustomEnvironment ?? "";
        set => Set(Launch.CustomEnvironment ?? "", value, v => Launch.CustomEnvironment = v);
    }

    public string Arguments
    {
        get => Launch.Arguments ?? "";
        set => Set(Launch.Arguments ?? "", value, v => Launch.Arguments = v);
    }

    /// <summary>The entry was converted again: its title or version may have changed.</summary>
    public void EntryChanged()
    {
        SelectedLibraryVersion = PickLibraryVersion();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Directory));
        OnPropertyChanged(nameof(DiscussionUri));
        ReconvertCommand.NotifyCanExecuteChanged();
        SwitchLibrariesCommand.NotifyCanExecuteChanged();
    }

    private string? PickLibraryVersion() =>
        Entry.ComponentVersion is { } v && App.Components.IsInstalled(v)
            ? v
            : App.Components.InstalledVersions().FirstOrDefault();

    [RelayCommand(CanExecute = nameof(CanReconvert))]
    private void Reconvert()
    {
        var source = !string.IsNullOrWhiteSpace(Entry.SourceFolder) && System.IO.Directory.Exists(Entry.SourceFolder)
            ? Entry.SourceFolder
            : Path.Combine(Entry.Directory, "app0");
        App.ConvertViewModel.OpenGame(source, Entry.ComponentVersion);
        App.Window.Navigate(MainWindow.ConvertTag);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchLibraries))]
    private void SwitchLibraries()
    {
        if (SelectedLibraryVersion is not { } version || !App.Components.IsInstalled(version)) return;
        try
        {
            var destination = Path.Combine(Entry.Directory, "libs");
            FileLinker.Mirror(App.Components.LibsPath(version), destination, App.Settings.LinkFiles);
            Entry.ComponentVersion = version;
            App.Library.Save();
            EntryChanged();
            Log.Add(LogKind.Success, Loc.Format("Games_LibsSwitched", ComponentStore.VersionLabel(version)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Add(LogKind.Error, e.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LaunchAsync()
    {
        Log.Clear();
        if (!File.Exists(Entry.ExecutablePath))
        {
            Log.Add(LogKind.Error, Loc.Format("Games_ExecutableMissing", Entry.ExecutablePath));
            return;
        }

        var environment = Launch.ToEnvironment();
        var spec = new ProcessSpec(Entry.ExecutablePath, RelinkerCommand.SplitArguments(Launch.Arguments))
        {
            // AnyPS5 keeps saves, trophies, caches and reports beside the executable, relative to this folder.
            WorkingDirectory = Entry.Directory,
            Environment = environment,
        };

        Log.StartFile("run-" + (Entry.TitleId ?? Entry.Title));
        Log.Add(LogKind.Info, RelinkerCommand.Format(spec.FileName, spec.Arguments));
        foreach (var (name, value) in environment) Log.Add(LogKind.Info, $"{name}={value}");

        IsRunning = true;
        using var cancellation = _cancellation = new CancellationTokenSource();
        try
        {
            var exitCode = await ProcessRunner.RunAsync(spec, Log.AddOutput, Log.AddOutput, cancellation.Token);
            // Windows reports crashes as codes such as 0xC0000005, which only read well in hexadecimal.
            Log.Add(exitCode == 0 ? LogKind.Success : LogKind.Error,
                Loc.Format("Games_Exited", exitCode, $"0x{exitCode:X8}"));
        }
        catch (OperationCanceledException)
        {
            Log.Add(LogKind.Warning, Loc.Get("Games_Stopped"));
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            Log.Add(LogKind.Error, e.Message);
        }
        finally
        {
            Log.EndFile();
            _cancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    public void Stop() => _cancellation?.Cancel();

    [RelayCommand]
    private void OpenFolder()
    {
        if (!System.IO.Directory.Exists(Entry.Directory)) return;
        var explorer = new ProcessStartInfo("explorer.exe");
        explorer.ArgumentList.Add(Entry.Directory);
        Process.Start(explorer)?.Dispose();
    }

    // A box reports -1 while it has no items or no selection; that is not a choice to store.
    private static void SetIndex(int value, Action set)
    {
        if (value >= 0) set();
    }

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(EnvironmentPreview));
        App.Library.Save();
    }
}
