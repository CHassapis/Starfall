namespace Starlights.Modules.Elements.Integration;

/// <summary>
/// The grammar of a rule's requirements as imported (element ids in place of Aurora ids): <c>,</c> is AND, <c>|</c>
/// or <c>||</c> is OR, <c>!</c> is NOT, parentheses group, and the terms between them. The characters module
/// evaluates them (RequirementsExpression) and the importer reports the terms nothing can check, both with this.
/// </summary>
public static class RequirementTerms
{
    /// <summary>The expression's tokens: its terms, and the operators , | ! ( ) ("||" is one "|").</summary>
    public static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var term = new System.Text.StringBuilder();

        void Flush()
        {
            var value = term.ToString().Trim();
            if (value.Length > 0)
            {
                tokens.Add(value);
            }
            term.Clear();
        }

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            // [stat:value] is a single term (it may contain spaces)
            if (c == '[')
            {
                Flush();
                var end = expression.IndexOf(']', i);
                end = end < 0 ? expression.Length - 1 : end;
                tokens.Add(expression[i..(end + 1)]);
                i = end;
                continue;
            }

            switch (c)
            {
                case ',' or '!' or '(' or ')':
                    Flush();
                    tokens.Add(c.ToString());
                    break;
                case '|':
                    Flush();
                    if (tokens.Count == 0 || tokens[^1] != "|")
                    {
                        tokens.Add("|");
                    }
                    break;
                default:
                    term.Append(c);
                    break;
            }
        }

        Flush();
        return tokens;
    }

    public static bool IsOperator(string token) => token is "," or "|" or "!" or "(" or ")";

    /// <summary>
    /// What a term is: an element id (held when the character has that element), <c>[name:N]</c> (a value of at
    /// least N), <c>[type:x]</c> (an element of that type), <c>[equipped:…]</c> (the character's equipment), or
    /// something no character can meet (an Aurora id left as it was because the element is not in the content, or a
    /// bracket term of no known kind).
    /// </summary>
    public static RequirementTermKind Classify(string term)
    {
        if (Guid.TryParse(term, out _))
        {
            return RequirementTermKind.Element;
        }
        if (term.StartsWith('[') && term.EndsWith(']'))
        {
            var inner = term[1..^1];
            if (inner.StartsWith("equipped:", StringComparison.OrdinalIgnoreCase))
            {
                return RequirementTermKind.Equipped;
            }
            var cut = inner.LastIndexOf(':');
            if (cut > 0 && int.TryParse(inner[(cut + 1)..].Trim(), out _))
            {
                return RequirementTermKind.Value;
            }
            if (inner.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                return RequirementTermKind.Type;
            }
        }
        return RequirementTermKind.Unknown;
    }

    /// <summary>The terms of an expression that no character can meet, each once, in order.</summary>
    public static IEnumerable<string> Unknown(string? expression) =>
        string.IsNullOrEmpty(expression)
            ? []
            : Tokenize(expression).Where(t => !IsOperator(t) && Classify(t) == RequirementTermKind.Unknown).Distinct(StringComparer.Ordinal);
}

public enum RequirementTermKind
{
    /// <summary>Nothing can meet it: it never holds.</summary>
    Unknown,
    Element,
    Value,
    Type,
    Equipped,
}
