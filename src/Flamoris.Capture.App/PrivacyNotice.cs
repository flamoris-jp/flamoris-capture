using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Flamoris.Capture.App;

internal sealed class PrivacyNotice
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAMORIS", "Capture", "capture-preferences.json");
    private bool shown;
    public bool Suppress { get; private set; }
    public PrivacyNotice()
    {
        try { Suppress = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.SuppressNotice ?? false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private sealed record Preference(bool SuppressNotice);
    public void SetDisplay(bool display)
    {
        Suppress = !display; shown = false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Preference(Suppress)));
    }
    public bool Confirm(Window owner)
    {
        if (Suppress || shown) return true;
        var dialog = new Window { Owner = owner, Title = "操作記録の確認", SizeToContent = SizeToContent.Height, Width = 470,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "画面共有中や操作記録中は、個人情報、パスワード、機密情報を入力・表示しないでください。これらの情報が共有または記録される可能性があります。", TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        var skip = new CheckBox { Content = "次回からこのメッセージを表示しない", IsChecked = false, Margin = new Thickness(0, 20, 0, 16) };
        panel.Children.Add(skip);
        var ok = new Button { Content = "OK", IsDefault = true, Width = 90, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { dialog.DialogResult = true; };
        panel.Children.Add(ok); dialog.Content = panel;
        // Closing the window before OK leaves acquisition OFF.
        if (dialog.ShowDialog() != true) return false;
        if (skip.IsChecked == true) SetDisplay(false);
        shown = true; return true;
    }
}
