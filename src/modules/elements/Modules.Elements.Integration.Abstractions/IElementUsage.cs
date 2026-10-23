namespace Starlights.Modules.Elements.Integration;

/// <summary>
/// Which characters use elements: what they picked in the builder (and what that granted them) and the spells they
/// prepared. Implemented by the characters module, so the elements module can say whom a homebrew change reaches
/// without reading characters itself. Reads only.
/// </summary>
public interface IElementUsage
{
    /// <summary>The characters that use any of the elements, each with the ones it uses, by character name.</summary>
    Task<IReadOnlyList<ElementUser>> GetUsersAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken cancellationToken = default);
}

/// <param name="ElementIds">The elements (of those asked about) the character uses.</param>
public sealed record ElementUser(Guid CharacterId, string CharacterName, string? PlayerName, IReadOnlyList<Guid> ElementIds)
{
    /// <summary>Of those elements, the ones the character has prepared, each with the spellcastings it is prepared as ("Wizard").</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> PreparedAs { get; init; } = new Dictionary<Guid, IReadOnlyList<string>>();
}
