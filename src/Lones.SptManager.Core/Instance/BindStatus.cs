namespace Lones.SptManager.Core.Instance;

public enum BindStatus
{
    Success = 0,
    GameRootNotFound = 1,
    MissingRequiredFiles = 2,
    UnsupportedSpt40Layout = 3
}

public enum BindWarning
{
    SptVersionNot41,
    EftVersionMismatch,
    ExtraLegacySptFolder
}
