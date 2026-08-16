using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Core.Deploy;

public static class RuntimeAttachment
{
    public static IReadOnlyList<EnabledMod> WithoutStoreRuntime(IEnumerable<EnabledMod> enabled)
        => enabled
            .Where(item => !HarvestRules.IsRuntimeVersion(item.Version))
            .Select((item, index) => new EnabledMod
            {
                ModKey = item.ModKey,
                Version = item.Version,
                Priority = index,
                Enabled = item.IsOn
            })
            .ToArray();

    public static IReadOnlyList<EnabledMod> DeployableOnly(IEnumerable<EnabledMod> enabled)
        => WithoutStoreRuntime(enabled.Where(item => item.IsOn));

    public static List<EnabledMod> AllDeployable(string managerData)
        => ModStore.List(managerData)
            .Where(document => document.Deployable && !HarvestRules.IsRuntimeVersion(document.Version))
            .Select((document, index) => new EnabledMod
            {
                ModKey = document.ModKey,
                Version = document.Version,
                Priority = index,
                Enabled = true
            })
            .ToList();
}
