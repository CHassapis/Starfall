using Starlights.Modules.Elements.Integration;

namespace Starlights.Modules.Characters.Services.Processing;

/// <summary>
/// Evaluates a rule's requirements, as imported from Aurora with element ids in place of Aurora ids:
/// <c>,</c> is AND, <c>|</c> or <c>||</c> is OR, <c>!</c> is NOT, parentheses group. An element id term holds when
/// the character has that element registered; a bracket term <c>[name:N]</c> when that value is at least N:
/// <c>[level:5]</c> and <c>[character:5]</c> the character level, <c>[level:warlock:3]</c> a class level,
/// <c>[str:13]</c> an ability score, anything else a statistic (<c>[innate speed:fly:1]</c>); a type term <c>[type:spell]</c>
/// when the character has an element of that type (a feat's "the ability to cast at least one spell"); an equipment term
/// <c>[equipped:armor:none]</c> (Aurora's equipped conditions) when the character's equipment matches it.
/// Anything unknown (a value the character does not have, ids of elements that were not imported) does not hold,
/// so "!ID_SOMETHING_UNKNOWN" is met.
/// </summary>
public static class RequirementsExpression
{
    public static bool Evaluate(string expression, Func<Guid, bool> hasElement, int characterLevel) =>
        Evaluate(expression, hasElement, name => name is "level" or "character" ? characterLevel : null);

    /// <param name="value">A bracket term's value by its lowercase Aurora name ("str", "level:warlock"); null when unknown.</param>
    public static bool Evaluate(string expression, Func<Guid, bool> hasElement, Func<string, int?> value, Func<string, bool>? equipped = null)
    {
        var tokens = RequirementTerms.Tokenize(expression);
        var position = 0;

        string? Peek() => position < tokens.Count ? tokens[position] : null;

        bool Term(string term)
        {
            // one grammar with the importer, which reports the terms nothing can meet (RequirementTerms)
            var inner = term.Length > 1 ? term[1..^1] : term;
            switch (RequirementTerms.Classify(term))
            {
                case RequirementTermKind.Element:
                    return hasElement(Guid.Parse(term));
                case RequirementTermKind.Equipped:
                    return equipped?.Invoke(inner["equipped:".Length..].Trim().ToLowerInvariant()) ?? false;
                case RequirementTermKind.Value:
                    var cut = inner.LastIndexOf(':');
                    return value(inner[..cut].Trim().ToLowerInvariant()) is { } actual && actual >= int.Parse(inner[(cut + 1)..].Trim());
                case RequirementTermKind.Type:
                    return value(inner.Trim().ToLowerInvariant()) is > 0;
                default:
                    return false;
            }
        }

        bool Or()
        {
            var result = And();
            while (Peek() == "|")
            {
                position++;
                result |= And();
            }
            return result;
        }

        bool And()
        {
            var result = Not();
            while (Peek() == ",")
            {
                position++;
                result &= Not();
            }
            return result;
        }

        bool Not()
        {
            if (Peek() == "!")
            {
                position++;
                return !Not();
            }

            if (Peek() == "(")
            {
                position++;
                var result = Or();
                if (Peek() == ")")
                {
                    position++;
                }
                return result;
            }

            var term = position < tokens.Count ? tokens[position++] : string.Empty;
            return term.Length > 0 && Term(term);
        }

        return Or();
    }
}
