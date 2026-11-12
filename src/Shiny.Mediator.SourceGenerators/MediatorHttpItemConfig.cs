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
    /// Path of an operation list (one operationId or component schema name per line, relative to the OpenAPI file): only
    /// these operations, these schemas and the schemas they reach are generated. Null generates the whole document.
    /// </summary>
    public string? Operations { get; set; }
}