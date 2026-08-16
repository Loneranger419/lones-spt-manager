using Lones.SptManager.Core.Deploy;

namespace Lones.SptManager.App;

public sealed class OverwriteRowViewModel
{
    public OverwriteRowViewModel(string canonicalPath)
    {
        CanonicalPath = canonicalPath;
        StayInOverwrite = HarvestRules.ShouldStayInOverwrite(canonicalPath);
    }

    public string CanonicalPath { get; }

    public bool StayInOverwrite { get; }
}
