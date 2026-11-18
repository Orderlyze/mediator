using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <summary>
/// <c>IgnoreUnknownValues="true"</c>: a value a newer server sends and this document does not declare reads as if it were not
/// there, instead of failing the whole response - an enum string becomes <c>default</c> for <c>E</c>, <c>null</c> for <c>E?</c>
/// and is left out of a <c>List&lt;E&gt;</c> member; an unknown discriminator reads as <c>null</c>. unknown-values.json is
/// generated three times: UnknownValuesApi (with LenientEnums and without enum attributes, as the POS client uses it),
/// UnknownValuesStrictApi (without LenientEnums) and UnknownValuesOffApi (without the option). Read through the generated
/// resolver only, as in an app without reflection.
/// </summary>
public class OpenApiIgnoreUnknownValuesTests
{
    delegate T Reader<T>(ref Utf8JsonReader reader, JsonSerializerOptions options);


    static JsonSerializerOptions GeneratedOnly<T>()
    {
        var ns = typeof(T).Namespace!;
        var resolver = (IJsonTypeInfoResolver)typeof(T).Assembly
            .GetType($"{ns}.{ns}JsonResolver", throwOnError: true)!
            .GetField("Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    static T Deserialize<T>(string json) => JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)GeneratedOnly<T>().GetTypeInfo(typeof(T)))!;


    [Fact]
    public void Unknown_String_Enum_Reads_As_Default_Null_Or_Left_Out()
    {
        var item = Deserialize<UnknownValuesApi.StockItemData>("""
            {"productName":"Burrata","valuationBasis":"average","previousBasis":"average","bases":["receipt","average","purchasePrice"]}
            """);

        item.ProductName.ShouldBe("Burrata");
        // required, so E: default - the first declared value
        item.ValuationBasis.ShouldBe(default(UnknownValuesApi.ValuationBasis));
        item.PreviousBasis.ShouldBeNull();
        item.Bases.ShouldBe([UnknownValuesApi.ValuationBasis.receipt, UnknownValuesApi.ValuationBasis.purchasePrice]);
    }

    [Fact]
    public void Known_Values_And_Other_Spellings_Read_As_Before()
    {
        var item = Deserialize<UnknownValuesApi.StockItemData>("""
            {"productName":"x","valuationBasis":"RECEIPT","previousBasis":"purchasePrice","bases":["none",2],"kind":"extra"}
            """);

        item.ValuationBasis.ShouldBe(UnknownValuesApi.ValuationBasis.receipt);
        item.PreviousBasis.ShouldBe(UnknownValuesApi.ValuationBasis.purchasePrice);
        item.Bases.ShouldBe([UnknownValuesApi.ValuationBasis.none, UnknownValuesApi.ValuationBasis.purchasePrice]);
        item.Kind.ShouldBe(UnknownValuesApi.StockKind.Extra);
    }

    [Fact]
    public void Numeric_Enums_Keep_Every_Integer_And_Drop_Unknown_Names()
    {
        Deserialize<UnknownValuesApi.StockItemData>("""{"productName":"x","valuationBasis":"none","kind":7}""")
            .Kind.ShouldBe((UnknownValuesApi.StockKind)7);
        Deserialize<UnknownValuesApi.StockItemData>("""{"productName":"x","valuationBasis":"none","kind":"Bundle"}""")
            .Kind.ShouldBeNull();
    }

    [Fact]
    public void Unknown_Discriminator_Reads_As_Null()
    {
        var item = Deserialize<UnknownValuesApi.StockItemData>("""
            {
              "productName": "x",
              "valuationBasis": "none",
              "source": { "kind": "giftCard", "code": "A1", "nested": { "kind": "catalogItem" } },
              "sources": [
                { "kind": "giftCard", "code": "A1" },
                { "kind": "customSupply", "name": "Wine" }
              ]
            }
            """);

        item.Source.ShouldBeNull();
        // a list keeps the element, as null: the list converter belongs to System.Text.Json
        item.Sources.ShouldNotBeNull();
        item.Sources.Count.ShouldBe(2);
        item.Sources[0].ShouldBeNull();
        item.Sources[1].ShouldBeOfType<UnknownValuesApi.ItemSourceCustom>().Name.ShouldBe("Wine");
    }

    [Fact]
    public void A_Response_Enum_Reads_Like_A_Member()
    {
        // getValuationBasis answers with the bare enum: the resolver entries read through the same helper
        var options = GeneratedOnly<UnknownValuesApi.StockItemData>();
        var info = (JsonTypeInfo<UnknownValuesApi.ValuationBasis>)options.GetTypeInfo(typeof(UnknownValuesApi.ValuationBasis));
        var nullable = (JsonTypeInfo<UnknownValuesApi.ValuationBasis?>)options.GetTypeInfo(typeof(UnknownValuesApi.ValuationBasis?));

        JsonSerializer.Deserialize("\"receipt\"", info).ShouldBe(UnknownValuesApi.ValuationBasis.receipt);
        JsonSerializer.Deserialize("\"average\"", info).ShouldBe(default(UnknownValuesApi.ValuationBasis));
        JsonSerializer.Deserialize("\"average\"", nullable).ShouldBeNull();
        JsonSerializer.Deserialize("null", nullable).ShouldBeNull();
        JsonSerializer.Deserialize("\"purchasePrice\"", nullable).ShouldBe(UnknownValuesApi.ValuationBasis.purchasePrice);
        JsonSerializer.Serialize<UnknownValuesApi.ValuationBasis?>(UnknownValuesApi.ValuationBasis.receipt, nullable).ShouldBe("\"receipt\"");
    }

    [Fact]
    public void Without_LenientEnums_Only_The_Declared_Spelling_Is_Known()
    {
        var item = Deserialize<UnknownValuesStrictApi.StockItemData>("""
            {"productName":"x","valuationBasis":"receipt","previousBasis":"RECEIPT","bases":["purchasePrice",1,"none"],"kind":2}
            """);

        item.ValuationBasis.ShouldBe(UnknownValuesStrictApi.ValuationBasis.receipt);
        item.PreviousBasis.ShouldBeNull();
        item.Bases.ShouldBe([UnknownValuesStrictApi.ValuationBasis.purchasePrice, UnknownValuesStrictApi.ValuationBasis.none]);
        item.Kind.ShouldBe(UnknownValuesStrictApi.StockKind.Extra);
        Deserialize<UnknownValuesStrictApi.StockItemData>("""{"productName":"x","valuationBasis":"none","kind":"Extra"}""").Kind.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"productName":"x","valuationBasis":"average"}""")]
    [InlineData("""{"productName":"x","valuationBasis":"none","bases":["average"]}""")]
    [InlineData("""{"productName":"x","valuationBasis":"none","source":{"kind":"giftCard"}}""")]
    public void Without_The_Option_The_Same_Response_Fails(string json)
    {
        Should.Throw<JsonException>(() => Deserialize<UnknownValuesOffApi.StockItemData>(json));
    }

    [Theory]
    [InlineData("""{"productName":"x","valuationBasis":true}""")]
    [InlineData("""{"productName":"x","valuationBasis":{"name":"average"}}""")]
    [InlineData("""{"productName":"x","valuationBasis":"none","bases":[["average"]]}""")]
    [InlineData("""{"productName":"x","valuationBasis":"none","source":{"name":"no discriminator"}}""")]
    public void A_Broken_Contract_Still_Fails(string json)
    {
        Should.Throw<JsonException>(() => Deserialize<UnknownValuesApi.StockItemData>(json));
        Should.Throw<JsonException>(() => Deserialize<UnknownValuesStrictApi.StockItemData>(json));
    }

    [Fact]
    public void Helpers_Never_Ask_The_Options()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new RefusingResolver() };

        Read("\"average\"", UnknownValuesApi.ValuationBasisJson.Read, options).ShouldBe(default(UnknownValuesApi.ValuationBasis));
        Read("\"average\"", UnknownValuesApi.ValuationBasisJson.ReadOrNull, options).ShouldBeNull();
        Read("\"Bundle\"", UnknownValuesApi.StockKindJson.ReadOrNull, options).ShouldBeNull();
        Read("\"RECEIPT\"", UnknownValuesStrictApi.ValuationBasisJson.ReadOrNull, options).ShouldBeNull();
        Read("0", UnknownValuesStrictApi.ValuationBasisJson.ReadOrNull, options).ShouldBeNull();
        Read("\"receipt\"", UnknownValuesStrictApi.ValuationBasisJson.Read, options).ShouldBe(UnknownValuesStrictApi.ValuationBasis.receipt);
    }

    [Fact]
    public void Writing_Stays_The_Wire_Format()
    {
        var options = GeneratedOnly<UnknownValuesApi.StockItemData>();
        var info = (JsonTypeInfo<UnknownValuesApi.StockItemData>)options.GetTypeInfo(typeof(UnknownValuesApi.StockItemData));
        const string json = """{"productName":"x","valuationBasis":"purchasePrice","previousBasis":null,"bases":["receipt"],"kind":2,"sources":[{"kind":"catalogItem","productId":"0b8a7f5e-1d2c-4f3a-9b6e-7c5d4e3f2a10"}]}""";

        JsonSerializer.Serialize(JsonSerializer.Deserialize(json, info), info).ShouldBe(json);
    }


    static T Read<T>(string json, Reader<T> read, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return read(ref reader, options);
    }

    sealed class RefusingResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
            => throw new InvalidOperationException($"asked the options for {type}");
    }
}
