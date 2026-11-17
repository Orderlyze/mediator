using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <summary>
/// <c>OmitEnumConverterAttributes="true"</c>: the generated enums carry no <c>[JsonConverter]</c> - NativeAOT would keep the
/// attribute's converter (System.Text.Json's generic machinery) for every enum. The generated converters and the resolver read
/// and write them all the same. The document of <see cref="OpenApiLenientEnumTests"/>, generated once more into PlainEnumsApi.
/// </summary>
public class OpenApiOmitEnumConverterAttributesTests
{
    static JsonSerializerOptions ResolverOnly() => new()
    {
        TypeInfoResolver = (IJsonTypeInfoResolver)typeof(PlainEnumsApi.OrderData).Assembly
            .GetType("PlainEnumsApi.PlainEnumsApiJsonResolver", throwOnError: true)!
            .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!
    };


    [Theory]
    [InlineData(typeof(PlainEnumsApi.OrderStatus))]
    [InlineData(typeof(PlainEnumsApi.ReturnState))]
    [InlineData(typeof(PlainEnumsApi.StaffRight))]
    public void Enums_Carry_No_Converter_Attribute(Type type)
    {
        type.GetCustomAttribute<JsonConverterAttribute>().ShouldBeNull();
        // with the attribute (the default) for comparison
        typeof(LenientEnumsApi.OrderData).Assembly.GetType(type.FullName!.Replace("PlainEnumsApi", "LenientEnumsApi"))!
            .GetCustomAttribute<JsonConverterAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Flags_Stay()
        => typeof(PlainEnumsApi.StaffRight).GetCustomAttribute<FlagsAttribute>().ShouldNotBeNull();

    [Fact]
    public void Models_Read_And_Write_Their_Enums_Through_The_Helpers()
    {
        var info = (JsonTypeInfo<PlainEnumsApi.OrderData>)ResolverOnly().GetTypeInfo(typeof(PlainEnumsApi.OrderData));

        var read = JsonSerializer.Deserialize("""{"status":"closed","rights":"sell, cashbook","returnState":"TooManyRequests","states":[0,"too many requests"],"channel":"TAKEAWAY"}""", info)!;
        var json = JsonSerializer.Serialize(read, info);

        read.Status.ShouldBe(PlainEnumsApi.OrderStatus.Closed);
        read.Rights.ShouldBe(PlainEnumsApi.StaffRight.Sell | PlainEnumsApi.StaffRight.Cashbook);
        read.ReturnState.ShouldBe(PlainEnumsApi.ReturnState.TooManyRequests);
        read.States.ShouldBe([PlainEnumsApi.ReturnState.open, PlainEnumsApi.ReturnState.TooManyRequests]);
        json.ShouldContain("\"status\":2");
        json.ShouldContain("\"returnState\":\"too many requests\"");
        json.ShouldContain("\"channel\":\"takeAway\"");
        // the same wire format as with the attributes
        var lenient = new JsonSerializerOptions { TypeInfoResolver = (IJsonTypeInfoResolver)typeof(LenientEnumsApi.OrderData).Assembly
            .GetType("LenientEnumsApi.LenientEnumsApiJsonResolver", throwOnError: true)!
            .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! };
        var other = JsonSerializer.Deserialize(json, (JsonTypeInfo<LenientEnumsApi.OrderData>)lenient.GetTypeInfo(typeof(LenientEnumsApi.OrderData)))!;
        JsonSerializer.Serialize(other, (JsonTypeInfo<LenientEnumsApi.OrderData>)lenient.GetTypeInfo(typeof(LenientEnumsApi.OrderData))).ShouldBe(json);
    }

    [Fact]
    public void A_Response_Enum_Reads_Through_The_Resolver()
    {
        var options = ResolverOnly();
        var info = (JsonTypeInfo<PlainEnumsApi.OrderStatus>)options.GetTypeInfo(typeof(PlainEnumsApi.OrderStatus));

        JsonSerializer.Deserialize("\"voided\"", info).ShouldBe(PlainEnumsApi.OrderStatus.Voided);
        JsonSerializer.Serialize(PlainEnumsApi.OrderStatus.Closed, info).ShouldBe("2");
    }
}
