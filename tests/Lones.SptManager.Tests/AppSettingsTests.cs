using Lones.SptManager.Core.Instance;

namespace Lones.SptManager.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void LoadTheme_MissingFile_IsWindows()
    {
        var root = NewTemp();
        try
        {
            Assert.Equal(AppTheme.Windows, AppSettings.LoadTheme(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(AppTheme.Dark, "dark")]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.Windows, "windows")]
    public void SaveThenLoad_RoundTrips(AppTheme theme, string written)
    {
        var root = NewTemp();
        try
        {
            AppSettings.SaveTheme(root, theme);
            Assert.Contains("\"theme\": \"" + written + "\"", File.ReadAllText(AppSettings.FilePath(root)));
            Assert.Equal(theme, AppSettings.LoadTheme(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("DARK", AppTheme.Dark)]
    [InlineData(" Light ", AppTheme.Light)]
    [InlineData("nope", AppTheme.Windows)]
    [InlineData(null, AppTheme.Windows)]
    public void ParseTheme_AcceptsKnownValues(string? value, AppTheme expected)
        => Assert.Equal(expected, AppSettings.ParseTheme(value));

    private static string NewTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
