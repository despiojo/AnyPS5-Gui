using System.Reflection;
using AnyPS5Gui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnyPS5Gui.ViewModels;

public sealed record HostCheckItem(string Name, bool Ok)
{
    public string Glyph => Ok ? "" : "";
}

/// <summary>The Settings page: AnyPS5 versions, folders, language, theme and whether this PC is up to it.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] LanguageTags = ["", "en-US", "es-ES"];
    private readonly int _languageAtStart;

    public SettingsViewModel()
    {
        OutputRoot = App.Settings.OutputRoot ?? "";
        CustomRelinkerPath = App.Settings.CustomRelinkerPath ?? "";
        CustomLibsPath = App.Settings.CustomLibsPath ?? "";
        ThemeIndex = (int)App.Settings.Theme;
        LogFilesToKeep = App.Settings.LogFilesToKeep;
        LanguageIndex = _languageAtStart = Math.Max(0, Array.IndexOf(LanguageTags, App.Settings.Language ?? ""));

        var host = App.Host;
        CpuName = host.CpuName;
        HostChecks = [.. host.Features.Select(feature => new HostCheckItem(feature.Name, feature.Supported)),
            new HostCheckItem("Vulkan", host.HasVulkan)];
        HostSummary = Loc.Get(!host.CpuSupported ? "Settings_HostCpuMissing"
            : !host.HasVulkan ? "Settings_HostVulkanMissing"
            : "Settings_HostOk");
    }

    public ComponentsViewModel Components => App.ComponentsViewModel;

    [ObservableProperty] public partial string OutputRoot { get; set; }
    [ObservableProperty] public partial string CustomRelinkerPath { get; set; }
    [ObservableProperty] public partial string CustomLibsPath { get; set; }
    [ObservableProperty] public partial int ThemeIndex { get; set; }

    /// <summary>NaN is the number box left empty: back to the default.</summary>
    [ObservableProperty] public partial double LogFilesToKeep { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartNeeded))]
    public partial int LanguageIndex { get; set; }

    public bool RestartNeeded => LanguageIndex != _languageAtStart;

    public string CpuName { get; }
    public HostCheckItem[] HostChecks { get; }
    public string HostSummary { get; }

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";

    public string DataFolder => App.Paths.Root;

    partial void OnOutputRootChanged(string value)
    {
        if (App.Settings.OutputRoot == Clean(value)) return;
        App.Settings.OutputRoot = Clean(value);
        App.SaveSettings();
        App.ConvertViewModel.RefreshDefaultOutput();
    }

    partial void OnCustomRelinkerPathChanged(string value)
    {
        if (App.Settings.CustomRelinkerPath == Clean(value)) return;
        App.Settings.CustomRelinkerPath = Clean(value);
        App.SaveSettings();
        Components.NotifyChanged();
    }

    partial void OnCustomLibsPathChanged(string value)
    {
        if (App.Settings.CustomLibsPath == Clean(value)) return;
        App.Settings.CustomLibsPath = Clean(value);
        App.SaveSettings();
        Components.NotifyChanged();
    }

    partial void OnLogFilesToKeepChanged(double value)
    {
        var keep = double.IsNaN(value) ? new AppSettings().LogFilesToKeep : (int)Math.Clamp(value, 0, 1000);
        if (App.Settings.LogFilesToKeep == keep) return;
        App.Settings.LogFilesToKeep = keep;
        App.SaveSettings();
    }

    partial void OnThemeIndexChanged(int value)
    {
        var theme = (AppTheme)Math.Clamp(value, 0, 2);
        if (App.Settings.Theme == theme) return;
        App.Settings.Theme = theme;
        App.SaveSettings();
        App.Window.ApplyTheme(theme);
    }

    partial void OnLanguageIndexChanged(int value)
    {
        var tag = Clean(LanguageTags[Math.Clamp(value, 0, LanguageTags.Length - 1)]);
        if (App.Settings.Language == tag) return;
        App.Settings.Language = tag;
        App.SaveSettings();
    }

    [RelayCommand]
    private async Task BrowseOutputRootAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) OutputRoot = folder;
    }

    [RelayCommand]
    private async Task BrowseRelinkerAsync()
    {
        if (await Pickers.PickFileAsync(".exe") is { } file) CustomRelinkerPath = file;
    }

    [RelayCommand]
    private async Task BrowseLibsAsync()
    {
        if (await Pickers.PickFolderAsync() is { } folder) CustomLibsPath = folder;
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(DataFolder);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(App.Paths.LogsDirectory);

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        var explorer = new System.Diagnostics.ProcessStartInfo("explorer.exe");
        explorer.ArgumentList.Add(folder);
        System.Diagnostics.Process.Start(explorer)?.Dispose();
    }

    private static string? Clean(string value)
    {
        value = value.Trim().Trim('"');
        return value.Length == 0 ? null : value;
    }
}
