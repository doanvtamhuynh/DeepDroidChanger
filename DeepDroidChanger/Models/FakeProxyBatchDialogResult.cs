namespace DeepDroidChanger.Models;

public enum ProxyAssignmentMode
{
    OneToOne,
    RepeatByCount,
    RepeatAll
}

public enum ProxyRepeatPattern
{
    Interleaved,
    Consecutive
}

public sealed class FakeProxyBatchDialogResult
{
    public FakeProxyBatchDialogResult(
        IReadOnlyList<ProxyEndpoint> proxies,
        ProxyAssignmentMode assignmentMode,
        int repeatCount,
        ProxyRepeatPattern repeatPattern,
        string proxyType,
        bool changeLocationByIp,
        bool changeTimezoneByIp)
    {
        Proxies = proxies;
        AssignmentMode = assignmentMode;
        RepeatCount = repeatCount;
        RepeatPattern = repeatPattern;
        ProxyType = proxyType;
        ChangeLocationByIp = changeLocationByIp;
        ChangeTimezoneByIp = changeTimezoneByIp;
    }

    public IReadOnlyList<ProxyEndpoint> Proxies { get; }
    public ProxyAssignmentMode AssignmentMode { get; }
    public int RepeatCount { get; }
    public ProxyRepeatPattern RepeatPattern { get; }
    public string ProxyType { get; }
    public bool ChangeLocationByIp { get; }
    public bool ChangeTimezoneByIp { get; }
}

public readonly record struct ProxyAssignment<TTarget>(TTarget Target, ProxyEndpoint Proxy);

public static class ProxyAssignmentPlanner
{
    public static IReadOnlyList<ProxyAssignment<TTarget>> Build<TTarget>(
        IReadOnlyList<TTarget> targets,
        FakeProxyBatchDialogResult configuration)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Proxies.Count == 0 || targets.Count == 0)
            return [];

        if (configuration.AssignmentMode == ProxyAssignmentMode.RepeatByCount
            && (configuration.RepeatCount < 1
                || !Enum.IsDefined(configuration.RepeatPattern)))
        {
            return [];
        }

        int assignmentCount = configuration.AssignmentMode switch
        {
            ProxyAssignmentMode.OneToOne => Math.Min(targets.Count, configuration.Proxies.Count),
            ProxyAssignmentMode.RepeatByCount => (int)Math.Min(
                targets.Count,
                (long)configuration.Proxies.Count * configuration.RepeatCount),
            ProxyAssignmentMode.RepeatAll => targets.Count,
            _ => 0
        };

        var assignments = new List<ProxyAssignment<TTarget>>(assignmentCount);
        for (int index = 0; index < assignmentCount; index++)
        {
            int proxyIndex = configuration.AssignmentMode switch
            {
                ProxyAssignmentMode.OneToOne => index,
                ProxyAssignmentMode.RepeatByCount
                    when configuration.RepeatPattern == ProxyRepeatPattern.Consecutive
                    => index / configuration.RepeatCount,
                _ => index % configuration.Proxies.Count
            };
            assignments.Add(new ProxyAssignment<TTarget>(targets[index], configuration.Proxies[proxyIndex]));
        }

        return assignments;
    }
}
