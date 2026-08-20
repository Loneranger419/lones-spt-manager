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

    [Fact]
    public void LoadUndeployOnExit_MissingFile_IsTrue()
    {
        var root = NewTemp();
        try
        {
            Assert.True(AppSettings.LoadUndeployOnExit(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadUndeployOnExit_OlderSettingsWithoutKey_IsTrue()
    {
        var root = NewTemp();
        try
        {
            File.WriteAllText(AppSettings.FilePath(root), """{ "theme": "dark" }""");
            Assert.True(AppSettings.LoadUndeployOnExit(root));
            Assert.Equal(AppTheme.Dark, AppSettings.LoadTheme(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveUndeployOnExit_PreservesTheme()
    {
        var root = NewTemp();
        try
        {
            AppSettings.SaveTheme(root, AppTheme.Dark);
            AppSettings.SaveUndeployOnExit(root, false);
            Assert.Equal(AppTheme.Dark, AppSettings.LoadTheme(root));
            Assert.False(AppSettings.LoadUndeployOnExit(root));
            var json = File.ReadAllText(AppSettings.FilePath(root));
            Assert.Contains("\"theme\": \"dark\"", json);
            Assert.Contains("\"undeployOnExit\": false", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveTheme_PreservesUndeployOnExit()
    {
        var root = NewTemp();
        try
        {
            AppSettings.SaveUndeployOnExit(root, false);
            AppSettings.SaveTheme(root, AppTheme.Light);
            Assert.False(AppSettings.LoadUndeployOnExit(root));
            Assert.Equal(AppTheme.Light, AppSettings.LoadTheme(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
