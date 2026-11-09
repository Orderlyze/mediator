using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;

namespace Shiny.Mediator.SourceGenerators;


/// <summary>
/// Restricts an OpenAPI document to a list (<c>MediatorHttp</c> metadata <c>Operations</c>) of operationIds and
/// component schema names. Only the listed operations get contracts and handlers, and only the component schemas
/// they reach are generated, plus listed schemas and what they reach (types code uses without calling an operation
/// that returns them, e.g. payloads of a server-sent event stream).
/// <list type="bullet">
/// <item>Reach follows <c>$ref</c>s from path and operation parameters, request bodies, responses and response
/// headers, and inside schemas through properties, pattern properties, definitions, items, additional properties,
/// <c>not</c>, <c>allOf</c>/<c>oneOf</c>/<c>anyOf</c> and discriminator mappings.</item>
/// <item>A discriminated hierarchy is kept whole, in both the union (<c>oneOf</c>/<c>anyOf</c>) and the inheritance
/// (<c>allOf</c>) shape: a reached variant keeps its base and a reached base all its variants. A variant without its
/// base would be generated as a standalone class, and a base without a variant could not read that payload.</item>
/// </list>
/// </summary>
public class OpenApiOperationFilter
{
    readonly HashSet<string> names;


    /// <param name="names">operationIds and component schema names</param>
    public OpenApiOperationFilter(IEnumerable<string> names)
    {
        this.names = new HashSet<string>(names, StringComparer.Ordinal);
    }


    /// <summary>One operationId or schema name per line; blank lines and everything after <c>#</c> are ignored.</summary>
    public static List<string> ParseList(string text)
    {
        var result = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            var comment = line.IndexOf('#');
            if (comment >= 0)
                line = line.Substring(0, comment);

            line = line.Trim();
            if (line.Length > 0)
                result.Add(line);
        }
        return result;
    }


    public bool Includes(OpenApiOperation operation)
        => operation.OperationId != null && this.names.Contains(operation.OperationId);


    /// <summary>Listed names that are neither an operationId nor a component schema of the document, sorted.</summary>
    public List<string> GetUnknown(OpenApiDocument document)
    {
        var known = new HashSet<string>(
            EnumerateOperations(document).Select(x => x.OperationId).Where(x => x != null)!,
            StringComparer.Ordinal
        );
        if (document.Components?.Schemas != null)
            known.UnionWith(document.Components.Schemas.Keys);

        return this.names.Where(x => !known.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }


    /// <summary>The component schemas reachable from the listed operations, in document order.</summary>
    public IDictionary<string, IOpenApiSchema> GetReachableSchemas(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>();
        var walker = new Walker(schemas);
        // analyzed over the whole document: the generator analyzes the kept schemas again and warns there
        var polymorphism = OpenApiPolymorphism.Analyze(schemas, _ => { });

        foreach (var name in this.names)
            walker.Keep(name);

        foreach (var path in document.Paths)
        {
            var operations = path.Value?.Operations;
            if (operations == null)
                continue;

            var included = operations.Values.Where(this.Includes).ToList();
            if (included.Count == 0)
                continue;

            walker.VisitParameters(path.Value!.Parameters);
            foreach (var operation in included)
            {
                walker.VisitParameters(operation.Parameters);
                walker.VisitContent(operation.RequestBody?.Content);

                if (operation.Responses != null)
                {
                    foreach (var response in operation.Responses.Values)
                    {
                        walker.VisitContent(response.Content);
                        if (response.Headers != null)
                        {
                            foreach (var header in response.Headers.Values)
                            {
                                walker.Visit(header.Schema);
                                walker.VisitContent(header.Content);
                            }
                        }
                    }
                }
            }
        }
        walker.Drain();

        // whole hierarchies, until what the added members reach adds no further one
        int count;
        do
        {
            count = walker.Kept.Count;
            foreach (var name in walker.Kept.ToList())
            {
                var root = polymorphism.Variants.TryGetValue(name, out var variant)
                    ? variant.Root
                    : polymorphism.Bases.TryGetValue(name, out var self) ? self : null;
                if (root == null)
                    continue;

                walker.Keep(root.ComponentName);
                foreach (var member in root.Variants)
                    walker.Keep(member.Value);
            }
            walker.Drain();
        }
        while (walker.Kept.Count != count);

        var result = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            if (walker.Kept.Contains(schema.Key))
                result.Add(schema.Key, schema.Value);
        }
        return result;
    }


    static IEnumerable<OpenApiOperation> EnumerateOperations(OpenApiDocument document)
        => document.Paths
            .Where(x => x.Value?.Operations != null)
            .SelectMany(x => x.Value!.Operations!.Values);


    class Walker(IDictionary<string, IOpenApiSchema> schemas)
    {
        readonly Queue<string> pending = new();
        readonly HashSet<IOpenApiSchema> visitedInline = new();

        public HashSet<string> Kept { get; } = new(StringComparer.Ordinal);


        public void Keep(string name)
        {
            if (schemas.ContainsKey(name) && this.Kept.Add(name))
                this.pending.Enqueue(name);
        }


        public void Drain()
        {
            while (this.pending.Count > 0)
            {
                var schema = schemas[this.pending.Dequeue()];
                // a component that is itself an alias ($ref) keeps its target
                if (schema is OpenApiSchemaReference)
                    this.Visit(schema);
                else
                    this.VisitChildren(schema);
            }
        }


        public void VisitParameters(IList<IOpenApiParameter>? parameters)
        {
            if (parameters == null)
                return;

            foreach (var parameter in parameters)
            {
                this.Visit(parameter.Schema);
                this.VisitContent(parameter.Content);
            }
        }


        public void VisitContent(IDictionary<string, IOpenApiMediaType>? content)
        {
            if (content == null)
                return;

            foreach (var media in content.Values)
            {
                this.Visit(media.Schema);
                this.Visit(media.ItemSchema);
            }
        }


        public void Visit(IOpenApiSchema? schema)
        {
            if (schema == null)
                return;

            if (schema is OpenApiSchemaReference)
            {
                var id = OpenApiPolymorphism.GetReferenceId(schema);
                if (id != null)
                    this.Keep(id);
                return;
            }

            if (this.visitedInline.Add(schema))
                this.VisitChildren(schema);
        }


        void VisitChildren(IOpenApiSchema schema)
        {
            this.VisitAll(schema.Properties?.Values);
            this.VisitAll(schema.PatternProperties?.Values);
            this.VisitAll(schema.Definitions?.Values);
            this.VisitAll(schema.AllOf);
            this.VisitAll(schema.OneOf);
            this.VisitAll(schema.AnyOf);
            this.Visit(schema.Items);
            this.Visit(schema.AdditionalProperties);
            this.Visit(schema.Not);

            if (schema.Discriminator != null)
            {
                this.VisitAll(schema.Discriminator.Mapping?.Values);
                this.Visit(schema.Discriminator.DefaultMapping);
            }
        }


        void VisitAll(IEnumerable<IOpenApiSchema>? items)
        {
            if (items == null)
                return;

            foreach (var item in items)
                this.Visit(item);
        }
    }
}
