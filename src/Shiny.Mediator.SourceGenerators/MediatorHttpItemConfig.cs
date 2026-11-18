namespace Shiny.Mediator.SourceGenerators;

public class MediatorHttpItemConfig
{
    public string Namespace { get; set; } = null!;
    public string? ContractPrefix { get; set; }
    public string? ContractPostfix { get; set; }
    public bool GenerateModelsOnly { get; set; }
    public bool UseInternalClasses { get; set; }
    public bool GenerateJsonConverters { get; set; }

    /// <summary>
    /// With <see cref="GenerateJsonConverters"/>: the generated enum helpers also read any member name (any casing, flag
    /// combinations) and, for string enums, any integer — without reflection and without asking the options. For APIs whose
    /// enums travel sometimes as name and sometimes as number. Anything else is a JsonException.
    /// </summary>
    public bool LenientEnums { get; set; }

    /// <summary>
    /// With <see cref="GenerateJsonConverters"/>: the generated resolver wires <c>List&lt;T&gt;</c> and <c>T[]</c> of a model only
    /// where generated code reads or writes that collection through the options (a model member, a request body, a response).
    /// Without it every model gets both, which costs a generic instantiation per model in AOT/ReadyToRun code even if
    /// nothing ever asks for it. Code outside the generator that serializes another model collection brings its own type info.
    /// </summary>
    public bool UsedCollectionsOnly { get; set; }

    /// <summary>
    /// With <see cref="GenerateJsonConverters"/>: generated enums carry no <c>[JsonConverter(typeof(JsonStringEnumConverter&lt;E&gt;))]</c>
    /// / <c>JsonNumberEnumConverter&lt;E&gt;</c>. The generated converters read and write enums through their <c>{Enum}Json</c> helpers and
    /// the resolver wires its own converter for the enums it registers, so neither needs the attribute; NativeAOT, however, keeps the
    /// attribute's converter (System.Text.Json's whole generic machinery) for every enum, asked for or not. Without the attribute,
    /// reflection-based System.Text.Json and source-generated contexts outside the generator write a string enum as its number.
    /// </summary>
    public bool OmitEnumConverterAttributes { get; set; }

    /// <summary>
    /// With <see cref="GenerateJsonConverters"/>: a value the document does not declare reads as if it were not there, instead of
    /// failing the whole response - an enum string (or, for a string enum without <see cref="LenientEnums"/>, a number) becomes
    /// <c>default</c> for <c>E</c>, <c>null</c> for <c>E?</c> and is left out of a <c>List&lt;E&gt;</c> member; an unknown
    /// discriminator of a polymorphic model reads as <c>null</c>. For clients that must keep working against a newer server.
    /// The enum helpers then never ask the options. Missing discriminators and other tokens still fail; writing is unchanged.
    /// </summary>
    public bool IgnoreUnknownValues { get; set; }

    /// <summary>
    /// Path of an operation list (one operationId or component schema name per line, relative to the OpenAPI file): only
    /// these operations, these schemas and the schemas they reach are generated. Null generates the whole document.
    /// </summary>
    public string? Operations { get; set; }
}