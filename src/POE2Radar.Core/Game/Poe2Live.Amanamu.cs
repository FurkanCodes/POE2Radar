namespace POE2Radar.Core.Game;

/// <summary>
/// Low-cost, read-only detection for the Lightless Well monsters spawned by Amanamu.
/// Behavioral identifiers are based on the community AmanamuVoidAlert plugin; this is an
/// independent implementation on POE2Radar's external read layer.
/// </summary>
public sealed partial class Poe2Live
{
    public const string AmanamuMonsterModId = "MonsterAbyssLightlessFaction1";
    public const string AmanamuBuffPrefix = "abyss_lightless_well";
    public const string AmanamuInsideCloudBuff = "abyss_lightless_well_immune";

    private const int AmanamuMaxModsPerVector = 64;

    // 1 = identity checked but not statically confirmed; 2 = confirmed target.
    private readonly Dictionary<nint, byte> _amanamuIdentity = new();
    private readonly Dictionary<nint, nint> _amanamuBuffsAddr = new();
    private readonly Dictionary<nint, string> _amanamuBuffNameByDefinition = new();

    public readonly record struct AmanamuReadResult(bool IsTarget, bool InsideCloud);

    public static bool IsAmanamuIdentity(string? value)
        => !string.IsNullOrEmpty(value)
           && (value.Contains(AmanamuMonsterModId, StringComparison.OrdinalIgnoreCase)
               || value.Contains("MonsterAbyssLightless", StringComparison.OrdinalIgnoreCase)
               || value.Contains("LightlessWells", StringComparison.OrdinalIgnoreCase));

    public static bool IsAmanamuBuff(string? value)
        => !string.IsNullOrEmpty(value)
           && value.Contains(AmanamuBuffPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsAmanamuInsideCloudBuff(string? value)
        => !string.IsNullOrEmpty(value)
           && value.Contains(AmanamuInsideCloudBuff, StringComparison.OrdinalIgnoreCase);

    /// <summary>No memory reads; lets the world reader preserve its discovery budget for unknowns.</summary>
    public bool IsKnownAmanamu(nint entity)
        => _amanamuIdentity.GetValueOrDefault(entity) == 2;

    /// <summary>
    /// Reads identity only when <paramref name="allowIdentityProbe"/> is true. Once confirmed,
    /// only the candidate's small Buffs vector is polled to keep cloud immunity live.
    /// </summary>
    public AmanamuReadResult ReadAmanamuState(in EntityDot entity, bool allowIdentityProbe)
    {
        var identity = _amanamuIdentity.GetValueOrDefault(entity.Address);

        // Metadata is already present in EntityDot, so this confirmation costs no process read.
        if (identity != 2 && IsAmanamuIdentity(entity.Metadata))
        {
            identity = 2;
            _amanamuIdentity[entity.Address] = identity;
        }

        if (identity == 0 && allowIdentityProbe)
        {
            identity = HasAmanamuMonsterMod(entity.Address) ? (byte)2 : (byte)1;
            _amanamuIdentity[entity.Address] = identity;
        }

        if (identity == 2)
        {
            var buffs = ReadAmanamuBuffs(entity.Address);
            return new AmanamuReadResult(true, buffs.InsideCloud);
        }

        // Buff-only identity is a fallback for entities whose static mod data is unavailable.
        // The world reader grants this path to only a handful of unknowns per world tick.
        if (allowIdentityProbe)
        {
            var buffs = ReadAmanamuBuffs(entity.Address);
            if (buffs.HasLightlessWellBuff)
            {
                _amanamuIdentity[entity.Address] = 2;
                return new AmanamuReadResult(true, buffs.InsideCloud);
            }
        }

        return default;
    }

    private bool HasAmanamuMonsterMod(nint entity)
    {
        if (!_ompAddr.TryGetValue(entity, out var omp))
        {
            omp = ResolveComponent(entity, "ObjectMagicProperties");
            _ompAddr[entity] = omp;
        }
        if (omp == 0) return false;

        return VectorHasAmanamuMod(omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Implicit)
               || VectorHasAmanamuMod(omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Explicit)
               || VectorHasAmanamuMod(omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Enchant)
               || VectorHasAmanamuMod(omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Hellscape)
               || VectorHasAmanamuMod(omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Crucible);
    }

    private bool VectorHasAmanamuMod(nint vectorAddress)
    {
        if (!_reader.TryReadStruct<StdVector>(vectorAddress, out var vec) || vec.First == 0)
            return false;

        var count = ((long)vec.Last - (long)vec.First) / Poe2.ModVectors.EntryStride;
        if (count is <= 0 or > AmanamuMaxModsPerVector) return false;

        try
        {
            foreach (var mod in _reader.ReadArray<ModArrayStruct>(vec.First, (int)count))
            {
                if (mod.ModsPtr != 0 && IsAmanamuIdentity(ReadModTemplate(mod.ModsPtr)))
                    return true;
            }
        }
        catch
        {
            // Entity/component teardown during an area transition is normal.
        }
        return false;
    }

    private (bool HasLightlessWellBuff, bool InsideCloud) ReadAmanamuBuffs(nint entity)
    {
        if (!_amanamuBuffsAddr.TryGetValue(entity, out var buffs))
        {
            buffs = ResolveComponent(entity, "Buffs");
            _amanamuBuffsAddr[entity] = buffs;
        }
        if (buffs == 0
            || !_reader.TryReadStruct<StdVector>(buffs + Poe2.Buffs.StatusEffects, out var effects)
            || effects.First == 0)
            return default;

        var count = ((long)effects.Last - (long)effects.First) / Poe2.StatusEffect.PointerStride;
        if (count is <= 0 or > Poe2.StatusEffect.MaxCount) return default;

        nint[] effectPointers;
        try { effectPointers = _reader.ReadArray<nint>(effects.First, (int)count); }
        catch { return default; }

        var hasLightless = false;
        foreach (var effect in effectPointers)
        {
            if (effect == 0
                || !_reader.TryReadStruct<nint>(effect + Poe2.StatusEffect.BuffDefinition, out var definition)
                || definition == 0)
                continue;

            if (!_amanamuBuffNameByDefinition.TryGetValue(definition, out var name))
            {
                if (!_reader.TryReadStruct<nint>(
                        definition + Poe2.StatusEffect.BuffDefinitionName,
                        out var namePtr)
                    || namePtr == 0)
                    continue;
                name = _reader.ReadStringUtf16(namePtr, 96);
                _amanamuBuffNameByDefinition[definition] = name;
            }
            if (!IsAmanamuBuff(name)) continue;
            hasLightless = true;
            if (IsAmanamuInsideCloudBuff(name))
                return (true, true);
        }
        return (hasLightless, false);
    }

    private void ResetAmanamuCaches()
    {
        _amanamuIdentity.Clear();
        _amanamuBuffsAddr.Clear();
        _amanamuBuffNameByDefinition.Clear();
    }

    private void EvictAmanamuEntity(nint entity)
    {
        _amanamuIdentity.Remove(entity);
        _amanamuBuffsAddr.Remove(entity);
    }
}
