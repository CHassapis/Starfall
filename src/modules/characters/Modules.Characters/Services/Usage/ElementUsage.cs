using Starlights.Modules.Characters.Data;
using Starlights.Modules.Characters.Domain.Elements;
using Starlights.Modules.Elements.Integration;
using Starlights.Platform.Data;

namespace Starlights.Modules.Characters.Services.Usage;

/// <summary>
/// Which characters use elements, for the elements module (a homebrew edit's preview): their registrations (what
/// they picked and what that granted them) and their prepared spells. Reads only; every character is loaded for the
/// prepared spells, which is fine for a group's characters.
/// </summary>
internal sealed class ElementUsage : IElementUsage
{
    private readonly IPersistence _persistence;

    public ElementUsage(IPersistence persistence) => _persistence = persistence;

    public async Task<IReadOnlyList<ElementUser>> GetUsersAsync(IReadOnlyCollection<Guid> elementIds, CancellationToken cancellationToken = default)
    {
        var wanted = elementIds.ToHashSet();
        if (wanted.Count == 0)
        {
            return [];
        }

        var uses = new Dictionary<Guid, SortedSet<Guid>>();
        void Use(Guid character, Guid element)
        {
            if (!uses.TryGetValue(character, out var set))
            {
                uses[character] = set = [];
            }
            set.Add(element);
        }

        foreach (var (character, element) in await _persistence.GetRepository<IRegistrationRepository>().GetAssociationsAsync([.. wanted.Select(id => new ElementId(id))]))
        {
            Use(character.Value, element.Value);
        }
        var characters = (await _persistence.GetRepository<ICharactersRepository>().GetCharactersAsync()).ToDictionary(c => c.Id.Value);
        var prepared = new Dictionary<Guid, Dictionary<Guid, SortedSet<string>>>();
        foreach (var character in characters.Values)
        {
            foreach (var (casting, spells) in character.Magic.Prepared)
            foreach (var spell in spells.Where(wanted.Contains))
            {
                Use(character.Id.Value, spell);
                var mine = prepared.TryGetValue(character.Id.Value, out var found) ? found : prepared[character.Id.Value] = [];
                (mine.TryGetValue(spell, out var castings) ? castings : mine[spell] = new SortedSet<string>(StringComparer.Ordinal)).Add(casting);
            }
        }

        return uses
            .Where(u => characters.ContainsKey(u.Key))
            .Select(u => new ElementUser(u.Key, characters[u.Key].Name, string.IsNullOrWhiteSpace(characters[u.Key].PlayerName) ? null : characters[u.Key].PlayerName, [.. u.Value])
            {
                PreparedAs = prepared.TryGetValue(u.Key, out var spells)
                    ? spells.ToDictionary(s => s.Key, s => (IReadOnlyList<string>)[.. s.Value])
                    : new Dictionary<Guid, IReadOnlyList<string>>(),
            })
            .OrderBy(u => u.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.CharacterId)
            .ToList();
    }
}
