namespace Lones.SptManager.Core.Instance;

public interface IFileVersionReader
{
    string? GetFileVersion(string fullPath);
}
