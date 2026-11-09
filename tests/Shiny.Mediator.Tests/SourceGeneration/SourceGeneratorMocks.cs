using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <param name="perFile">Values that replace or extend <paramref name="buildProperties"/> for the additional file with that path.</param>
public class MockAnalyzerConfigOptionsProvider(
    Dictionary<string, string> buildProperties,
    Dictionary<string, Dictionary<string, string>>? perFile = null
) : AnalyzerConfigOptionsProvider
{
    readonly MockAnalyzerConfigOptions options = new (buildProperties);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => this.options;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        if (perFile == null || !perFile.TryGetValue(textFile.Path, out var values))
            return this.options;

        var merged = new Dictionary<string, string>(buildProperties);
        foreach (var value in values)
            merged[value.Key] = value.Value;
        return new MockAnalyzerConfigOptions(merged);
    }

    public override AnalyzerConfigOptions GlobalOptions => this.options;
}

public class MockAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        => values.TryGetValue(key, out value);

    public override IEnumerable<string> Keys => values.Keys;
}