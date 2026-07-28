using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Pickup;

internal readonly record struct PickupPolicyCandidate(string? Metadata, string? Name);

internal enum PickupPolicyReason : byte
{
    Allowed,
    IdentityUnavailable,
    EquipmentDisabled,
    DenyListed,
    NotAllowListed,
}

internal readonly record struct PickupPolicyDecision(
    bool Eligible,
    int Priority,
    PickupPolicyReason Reason);

/// <summary>
/// Filter-aware pickup policy. The in-game filter remains the upper bound because callers only
/// submit visible labels; this module applies the user's additional fail-closed rules and priority.
/// </summary>
internal sealed class PickupPolicy
{
    private static readonly string[] GearRoots =
    [
        "Metadata/Items/Weapons/",
        "Metadata/Items/Armours/",
        "Metadata/Items/Rings/",
        "Metadata/Items/Amulets/",
        "Metadata/Items/Belts/",
        "Metadata/Items/Flasks/",
        "Metadata/Items/Charms/",
        "Metadata/Items/Equipment/",
    ];

    private string _allowSource = "";
    private string _denySource = "";
    private string _prioritySource = "";
    private string[] _allow = [];
    private string[] _deny = [];
    private string[] _priority = [];

    public PickupPolicyDecision Evaluate(
        in PickupPolicyCandidate candidate,
        PickupPolicySettings settings)
    {
        var metadata = candidate.Metadata;
        if (string.IsNullOrWhiteSpace(metadata) ||
            !metadata.StartsWith("Metadata/Items/", StringComparison.Ordinal))
        {
            return Rejected(PickupPolicyReason.IdentityUnavailable);
        }

        RefreshPatterns(settings);

        if (!settings.AllowEquipment && IsEquipment(metadata))
            return Rejected(PickupPolicyReason.EquipmentDisabled);

        if (MatchesAny(_deny, metadata, candidate.Name))
            return Rejected(PickupPolicyReason.DenyListed);

        if (_allow.Length > 0 && !MatchesAny(_allow, metadata, candidate.Name))
            return Rejected(PickupPolicyReason.NotAllowListed);

        var priority = 0;
        for (var i = 0; i < _priority.Length; i++)
        {
            if (!Matches(_priority[i], metadata, candidate.Name)) continue;
            priority = _priority.Length - i;
            break;
        }

        return new PickupPolicyDecision(true, priority, PickupPolicyReason.Allowed);
    }

    private void RefreshPatterns(PickupPolicySettings settings)
    {
        var allowSource = settings.AllowPatterns ?? "";
        if (!string.Equals(_allowSource, allowSource, StringComparison.Ordinal))
        {
            _allowSource = allowSource;
            _allow = ParsePatterns(_allowSource);
        }

        var denySource = settings.DenyPatterns ?? "";
        if (!string.Equals(_denySource, denySource, StringComparison.Ordinal))
        {
            _denySource = denySource;
            _deny = ParsePatterns(_denySource);
        }

        var prioritySource = settings.PriorityPatterns ?? "";
        if (!string.Equals(_prioritySource, prioritySource, StringComparison.Ordinal))
        {
            _prioritySource = prioritySource;
            _priority = ParsePatterns(_prioritySource);
        }
    }

    private static string[] ParsePatterns(string source)
        => source.Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsEquipment(string metadata)
    {
        foreach (var root in GearRoots)
            if (metadata.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool MatchesAny(
        IReadOnlyList<string> patterns,
        string metadata,
        string? name)
    {
        foreach (var pattern in patterns)
            if (Matches(pattern, metadata, name))
                return true;
        return false;
    }

    private static bool Matches(string pattern, string metadata, string? name)
        => metadata.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(name) &&
            name.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static PickupPolicyDecision Rejected(PickupPolicyReason reason)
        => new(false, 0, reason);
}
