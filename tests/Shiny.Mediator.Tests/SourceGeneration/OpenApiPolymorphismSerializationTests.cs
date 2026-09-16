using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using PolymorphicApi;

namespace Shiny.Mediator.Tests.SourceGeneration;

public class OpenApiPolymorphismSerializationTests
{
    const string Id = "6f1f0b5e-6d4e-4c6b-9f2a-0d1f6f3c2a11";

    public static IEnumerable<object[]> Sources()
    {
        yield return [$$"""{"kind":"catalogItem","productId":"{{Id}}","correlationId":"{{Id}}"}""", typeof(CatalogItem)];
        yield return [$$"""{"kind":"customSupply","name":"Freie Leistung","grossAmount":12.340000000000000000000000001,"taxClassId":"{{Id}}","taxKind":6,"correlationId":"{{Id}}"}""", typeof(CustomSupply)];
        yield return [$$"""{"kind":"voucherIssue","programId":"{{Id}}","programVersion":3,"nominalAmount":50.25,"precreatedCredentialIds":["{{Id}}"],"origin":{"originKind":"ownProgram","sellerId":"{{Id}}"},"correlationId":"{{Id}}"}""", typeof(VoucherIssue)];
    }

    static JsonSerializerOptions Options() => new(JsonSerializerDefaults.Web)
    {
        // No DefaultJsonTypeInfoResolver / reflection fallback. Every model, variant and collection
        // used below must be handled by the real source-generated resolver and converters.
        TypeInfoResolver = PolymorphicApiJsonResolver.Instance
    };

    static JsonTypeInfo<T> Metadata<T>() => (JsonTypeInfo<T>)Options().GetTypeInfo(typeof(T));
    static T? Read<T>(string json) => JsonSerializer.Deserialize(json, Metadata<T>());
    static T? Read<T>(JsonElement json) => JsonSerializer.Deserialize(json, Metadata<T>());
    static string Write<T>(T value) => JsonSerializer.Serialize(value, Metadata<T>());

    [Theory]
    [MemberData(nameof(Sources))]
    public void Every_Variant_Roundtrips_Through_Required_Base_Property(string sourceJson, Type expectedType)
    {
        var json = $$"""{"itemId":"{{Id}}","source":{{sourceJson}}}""";
        var value = Read<NewOrderItem>(json)!;
        value.Source.GetType().ShouldBe(expectedType);
        value.Source.CorrelationId.ShouldBe(Guid.Parse(Id));
        typeof(OrderItemSource).IsAbstract.ShouldBeTrue();
        typeof(OrderItemSource).GetProperty("Kind").ShouldBeNull();

        var roundtrip = Write(value);
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(roundtrip)).ShouldBeTrue(roundtrip);
    }

    [Theory]
    [InlineData("{\"originKind\":\"externalIssuer\",\"issuer\":\"Partner\",\"agreementId\":\"A-42\"}", typeof(ExternalIssuerOrigin))]
    [InlineData("{\"originKind\":\"legacyImport\",\"batchId\":\"import-2026\",\"originalReference\":null}", typeof(LegacyImportOrigin))]
    public void Independent_Origin_Hierarchy_Preserves_Fields_And_Required_Nullable_Value(string json, Type expectedType)
    {
        var value = Read<VoucherOriginData>(json)!;
        value.GetType().ShouldBe(expectedType);
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(Write(value))).ShouldBeTrue();
    }

    [Fact]
    public void Base_Collections_And_Optional_Null_Compose_With_Generated_Metadata()
    {
        var sources = String.Join(",", Sources().Select(x => (string)x[0]));
        var json = $$"""{"sources":[{{sources}}],"optionalSource":null,"requiredNullableSource":null}""";
        var value = Read<Selection>(json)!;
        value.Sources!.Count.ShouldBe(3);
        value.OptionalSource.ShouldBeNull();
        value.RequiredNullableSource.ShouldBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(Write(value))).ShouldBeTrue();

        var array = Read<OrderItemSource[]>($"[{sources}]")!;
        JsonNode.DeepEquals(JsonNode.Parse($"[{sources}]"), JsonNode.Parse(Write(array))).ShouldBeTrue();
    }

    [Fact]
    public void Required_Nullable_Base_Property_Still_Requires_Presence()
        => Should.Throw<JsonException>(() => Read<Selection>("{}"));

    [Fact]
    public void Direct_Concrete_Serialization_Writes_Exactly_One_Fixed_Discriminator()
    {
        var value = new CatalogItem { ProductId = Guid.Parse(Id), CorrelationId = Guid.Parse(Id) };
        using var json = JsonDocument.Parse(Write(value));
        json.RootElement.GetProperty("kind").GetString().ShouldBe("catalogItem");
        json.RootElement.EnumerateObject().Count(x => x.Name == "kind").ShouldBe(1);
        Read<CatalogItem>(json.RootElement)!.ProductId.ShouldBe(value.ProductId);
    }

    [Fact]
    public void Discriminator_Can_Follow_The_Data_Fields()
    {
        var json = $$"""{"productId":"{{Id}}","correlationId":"{{Id}}","kind":"catalogItem"}""";
        Read<OrderItemSource>(json).ShouldBeOfType<CatalogItem>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")] // missing discriminator
    [InlineData("{\"kind\":null}")]
    [InlineData("{\"kind\":1}")]
    [InlineData("{\"kind\":\"unknown\"}")]
    [InlineData("{\"kind\":\"catalogItem\",\"kind\":\"catalogItem\"}")]
    [InlineData("{\"kind\":\"catalogItem\"}")] // required variant fields missing
    [InlineData("{\"kind\":\"customSupply\",\"name\":null}")]
    public void Invalid_Base_Payloads_Throw_JsonException(string json)
        => Should.Throw<JsonException>(() => Read<OrderItemSource>(json));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"source\":null}")]
    public void Required_Base_Property_Cannot_Be_Absent_Or_Null(string json)
        => Should.Throw<JsonException>(() => Read<NewOrderItem>(json));

    [Fact]
    public void Null_Required_Source_And_Unknown_Runtime_Subtype_Cannot_Be_Written()
    {
        Should.Throw<JsonException>(() => Write(new NewOrderItem()));
        Should.Throw<JsonException>(() => Write<OrderItemSource>(new UnknownSource()));
        Should.Throw<JsonException>(() => Write<OrderItemSource>(null!));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"voucherIssue\"}")]
    [InlineData("null")]
    public void Direct_Concrete_Read_Cannot_Change_Or_Omit_Its_Tag(string json)
        => Should.Throw<JsonException>(() => Read<CatalogItem>(json));

    sealed class UnknownSource : OrderItemSource;
}
