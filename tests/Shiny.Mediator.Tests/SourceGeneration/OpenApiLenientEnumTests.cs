using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <summary>
/// <c>LenientEnums="true"</c>: the generated {Enum}Json helpers read every spelling of a member themselves - a name for a
/// numeric enum (any casing, flag combinations), a number or another casing for a string enum - without reflection and
/// without asking the options. The same document as <see cref="OpenApiEnumSerializationTests"/>, generated a second time
/// into the namespace LenientEnumsApi.
/// </summary>
public class OpenApiLenientEnumTests
{
    delegate T Reader<T>(ref Utf8JsonReader reader, JsonSerializerOptions options);


    static IJsonTypeInfoResolver GeneratedResolver()
        => (IJsonTypeInfoResolver)typeof(LenientEnumsApi.OrderData).Assembly
            .GetType("LenientEnumsApi.LenientEnumsApiJsonResolver", throwOnError: true)!
            .GetField("Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    // the generated resolver only, as in an app without reflection, and the reflection path
    static IEnumerable<JsonSerializerOptions> Options() => [new() { TypeInfoResolver = GeneratedResolver() }, JsonSerializerOptions.Default];


    [Theory]
    [InlineData("\"Closed\"", LenientEnumsApi.OrderStatus.Closed)]
    [InlineData("\"closed\"", LenientEnumsApi.OrderStatus.Closed)]
    [InlineData("\"VOIDED\"", LenientEnumsApi.OrderStatus.Voided)]
    [InlineData("\"2\"", LenientEnumsApi.OrderStatus.Closed)]
    [InlineData("1", LenientEnumsApi.OrderStatus.Open)]
    public void Numeric_Enums_Read_Names_And_Numbers(string status, LenientEnumsApi.OrderStatus expected)
    {
        foreach (var options in Options())
            JsonSerializer.Deserialize<LenientEnumsApi.OrderData>($$"""{"status":{{status}}}""", options)!.Status.ShouldBe(expected);
    }

    [Fact]
    public void Numeric_Enums_Read_Flag_Combinations_Unnamed_Values_And_Long_Members_By_Name()
    {
        foreach (var options in Options())
        {
            var read = JsonSerializer.Deserialize<LenientEnumsApi.OrderData>("""{"status":1,"rights":"sell, cashbook","priority":"value20","code":"Big"}""", options)!;
            read.Rights.ShouldBe(LenientEnumsApi.StaffRight.Sell | LenientEnumsApi.StaffRight.Cashbook);
            read.Priority.ShouldBe(LenientEnumsApi.Priority.Value20);
            read.Code.ShouldBe(LenientEnumsApi.BigCode.Big);
        }
    }

    [Theory]
    [InlineData("\"OPEN\"", LenientEnumsApi.ReturnState.open)]
    [InlineData("\"too many requests\"", LenientEnumsApi.ReturnState.TooManyRequests)]
    [InlineData("\"TooManyRequests\"", LenientEnumsApi.ReturnState.TooManyRequests)]
    [InlineData("\"CLASS\"", LenientEnumsApi.ReturnState.Class)]
    [InlineData("0", LenientEnumsApi.ReturnState.open)]
    [InlineData("1", LenientEnumsApi.ReturnState.TooManyRequests)]
    public void String_Enums_Read_Other_Casings_Member_Names_And_Numbers(string state, LenientEnumsApi.ReturnState expected)
    {
        foreach (var options in Options())
        {
            var read = JsonSerializer.Deserialize<LenientEnumsApi.OrderData>($$"""{"status":1,"returnState":{{state}},"states":[{{state}}],"channel":"TAKEAWAY"}""", options)!;
            read.ReturnState.ShouldBe(expected);
            read.States.ShouldBe([expected]);
            read.Channel.ShouldBe(LenientEnumsApi.OrderDataChannel.takeAway);
        }
    }

    [Theory]
    [InlineData("""{"status":"Reopened"}""")]
    [InlineData("""{"status":true}""")]
    [InlineData("""{"status":1.5}""")]
    [InlineData("""{"status":1,"code":"Huge"}""")]
    [InlineData("""{"status":1,"returnState":"unknown"}""")]
    [InlineData("""{"status":1,"returnState":2.5}""")]
    [InlineData("""{"status":1,"channel":"delivery"}""")]
    public void Unknown_Names_And_Other_Tokens_Still_Fail(string json)
    {
        foreach (var options in Options())
            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<LenientEnumsApi.OrderData>(json, options));
    }

    [Fact]
    public void Helpers_Never_Ask_The_Options()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new RefusingResolver() };

        Read("\"closed\"", LenientEnumsApi.OrderStatusJson.Read, options).ShouldBe(LenientEnumsApi.OrderStatus.Closed);
        Read("0", LenientEnumsApi.ReturnStateJson.Read, options).ShouldBe(LenientEnumsApi.ReturnState.open);
        Should.Throw<JsonException>(() => Read("\"unknown\"", LenientEnumsApi.OrderStatusJson.Read, options));
        Should.Throw<JsonException>(() => Read("\"unknown\"", LenientEnumsApi.ReturnStateJson.Read, options));
    }

    [Fact]
    public void Writing_Stays_The_Wire_Format()
    {
        Check(Enum.GetValues<LenientEnumsApi.ReturnState>().Append((LenientEnumsApi.ReturnState)42), LenientEnumsApi.ReturnStateJson.Write, LenientEnumsApi.ReturnStateJson.Read);
        Check(Enum.GetValues<LenientEnumsApi.OrderStatus>().Append((LenientEnumsApi.OrderStatus)99), LenientEnumsApi.OrderStatusJson.Write, LenientEnumsApi.OrderStatusJson.Read);
        Check(Enum.GetValues<LenientEnumsApi.StaffRight>().Append((LenientEnumsApi.StaffRight)7), LenientEnumsApi.StaffRightJson.Write, LenientEnumsApi.StaffRightJson.Read);
        Check(Enum.GetValues<LenientEnumsApi.BigCode>(), LenientEnumsApi.BigCodeJson.Write, LenientEnumsApi.BigCodeJson.Read);

        static void Check<T>(IEnumerable<T> values, Action<Utf8JsonWriter, T> write, Reader<T> read) where T : struct, Enum
        {
            foreach (var value in values)
            {
                // the enum's own [JsonConverter] through reflection
                var expected = JsonSerializer.Serialize(value);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                    write(writer, value);
                Encoding.UTF8.GetString(stream.ToArray()).ShouldBe(expected);
                Read(expected, read, JsonSerializerOptions.Default).ShouldBe(value);
            }
        }
    }

    [Fact]
    public void A_Response_Enum_Reads_Like_A_Member()
    {
        // getOrderStatus answers with the bare enum: the resolver entry reads through the same helper
        var options = new JsonSerializerOptions { TypeInfoResolver = GeneratedResolver() };
        var info = (JsonTypeInfo<LenientEnumsApi.OrderStatus>)options.GetTypeInfo(typeof(LenientEnumsApi.OrderStatus));
        var nullable = (JsonTypeInfo<LenientEnumsApi.OrderStatus?>)options.GetTypeInfo(typeof(LenientEnumsApi.OrderStatus?));

        JsonSerializer.Deserialize("\"voided\"", info).ShouldBe(LenientEnumsApi.OrderStatus.Voided);
        JsonSerializer.Deserialize("3", info).ShouldBe(LenientEnumsApi.OrderStatus.Voided);
        JsonSerializer.Deserialize("null", nullable).ShouldBeNull();
        JsonSerializer.Deserialize("\"closed\"", nullable).ShouldBe(LenientEnumsApi.OrderStatus.Closed);
        JsonSerializer.Serialize(LenientEnumsApi.OrderStatus.Closed, info).ShouldBe("2");
        // an unknown value fails once instead of handing the helper back to itself
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize("\"reopened\"", info));
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
