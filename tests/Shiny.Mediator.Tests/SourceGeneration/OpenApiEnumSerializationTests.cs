using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <summary>
/// Numeric OpenAPI enums keep their names (x-enumNames), flags (x-enumFlags) and travel as numbers
/// on the wire through both the reflection path and the generated per-type converters; decimal
/// formats map to System.Decimal. The generated converters read and write enum members through each
/// enum's {Enum}Json helper, not through System.Text.Json metadata for the enum.
/// </summary>
public class OpenApiEnumSerializationTests
{
    const string Json = """
        {"orderId":"6f1f0b5e-6d4e-4c6b-9f2a-0d1f6f3c2a11","status":2,"rights":5,"priority":20,"totalGross":12.5,"discountRate":null,"ratio":0.25}
        """;

    delegate T Reader<T>(ref Utf8JsonReader reader, JsonSerializerOptions options);


    static IJsonTypeInfoResolver GeneratedResolver()
        => (IJsonTypeInfoResolver)typeof(EnumsApi.OrderData).Assembly
            .GetType("EnumsApi.EnumsApiJsonResolver", throwOnError: true)!
            .GetField("Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    // the generated resolver only, no reflection fallback
    static JsonSerializerOptions GeneratedOnly() => new() { TypeInfoResolver = GeneratedResolver() };


    [Fact]
    public void Enum_Members_Use_Document_Names_And_Flags()
    {
        ((int)EnumsApi.OrderStatus.Closed).ShouldBe(2);
        Enum.GetNames<EnumsApi.OrderStatus>().ShouldBe(["Open", "Closed", "Voided"]);
        typeof(EnumsApi.StaffRight).IsDefined(typeof(FlagsAttribute), false).ShouldBeTrue();
        (EnumsApi.StaffRight.Sell | EnumsApi.StaffRight.Cashbook).ShouldBe((EnumsApi.StaffRight)5);
        Enum.GetNames<EnumsApi.Priority>().ShouldBe(["Value10", "Value20"]);
        Enum.GetUnderlyingType(typeof(EnumsApi.BigCode)).ShouldBe(typeof(long));
        ((long)EnumsApi.BigCode.Big).ShouldBe(5000000000L);
        typeof(EnumsApi.OrderData).GetProperty("TotalGross")!.PropertyType.ShouldBe(typeof(decimal));
        typeof(EnumsApi.OrderData).GetProperty("DiscountRate")!.PropertyType.ShouldBe(typeof(decimal?));
        typeof(EnumsApi.OrderData).GetProperty("Ratio")!.PropertyType.ShouldBe(typeof(double));
    }

    [Fact]
    public void Numeric_Enums_Roundtrip_As_Numbers()
    {
        var obj = JsonSerializer.Deserialize<EnumsApi.OrderData>(Json)!;
        obj.Status.ShouldBe(EnumsApi.OrderStatus.Closed);
        obj.Rights.ShouldBe(EnumsApi.StaffRight.Sell | EnumsApi.StaffRight.Cashbook);
        obj.Priority.ShouldBe(EnumsApi.Priority.Value20);
        obj.TotalGross.ShouldBe(12.5m);
        obj.DiscountRate.ShouldBeNull();

        var json = JsonSerializer.Serialize(obj);
        json.ShouldContain("\"status\":2");
        json.ShouldContain("\"rights\":5");
        json.ShouldContain("\"priority\":20");
        json.ShouldContain("\"totalGross\":12.5");
    }

    [Fact]
    public void Enum_Members_Roundtrip_Through_The_Generated_Resolver_Alone()
    {
        var order = new EnumsApi.OrderData
        {
            Status = EnumsApi.OrderStatus.Voided,
            Rights = EnumsApi.StaffRight.Sell | EnumsApi.StaffRight.CancelInvoice,
            ReturnState = EnumsApi.ReturnState.TooManyRequests,
            States = [EnumsApi.ReturnState.open, EnumsApi.ReturnState.Class],
            Code = EnumsApi.BigCode.Big,
            Channel = EnumsApi.OrderDataChannel.takeAway
        };

        foreach (var options in new[] { GeneratedOnly(), JsonSerializerOptions.Default })
        {
            var json = JsonSerializer.Serialize(order, options);
            json.ShouldContain("\"status\":3");
            json.ShouldContain("\"rights\":3");
            json.ShouldContain("\"returnState\":\"Too Many Requests\"");
            json.ShouldContain("\"states\":[\"open\",\"class\"]");
            json.ShouldContain("\"code\":5000000000");
            json.ShouldContain("\"channel\":\"takeAway\"");

            var back = JsonSerializer.Deserialize<EnumsApi.OrderData>(json, options)!;
            back.Status.ShouldBe(order.Status);
            back.Rights.ShouldBe(order.Rights);
            back.ReturnState.ShouldBe(order.ReturnState);
            back.States.ShouldBe(order.States);
            back.Code.ShouldBe(order.Code);
            back.Channel.ShouldBe(order.Channel);
        }
    }

    [Fact]
    public void Enum_Members_Need_No_Resolver_Entries()
    {
        var resolver = GeneratedResolver();
        var options = new JsonSerializerOptions();

        foreach (var type in new[] { typeof(EnumsApi.ReturnState), typeof(EnumsApi.ReturnState?), typeof(List<EnumsApi.ReturnState>), typeof(EnumsApi.BigCode), typeof(EnumsApi.OrderDataChannel), typeof(EnumsApi.StaffRight?) })
            resolver.GetTypeInfo(type, options).ShouldBeNull(type.ToString());

        // a response of getOrderStatus: the handler reads it through the options
        resolver.GetTypeInfo(typeof(EnumsApi.OrderStatus), options).ShouldNotBeNull();
    }

    [Fact]
    public void Helpers_Write_And_Read_What_The_Enum_Converters_Do()
    {
        Check(Enum.GetValues<EnumsApi.ReturnState>().Append((EnumsApi.ReturnState)42), EnumsApi.ReturnStateJson.Write, EnumsApi.ReturnStateJson.Read);
        Check(Enum.GetValues<EnumsApi.OrderDataChannel>(), EnumsApi.OrderDataChannelJson.Write, EnumsApi.OrderDataChannelJson.Read);
        Check(Enum.GetValues<EnumsApi.OrderStatus>().Append((EnumsApi.OrderStatus)99), EnumsApi.OrderStatusJson.Write, EnumsApi.OrderStatusJson.Read);
        Check(Enum.GetValues<EnumsApi.StaffRight>().Append((EnumsApi.StaffRight)7), EnumsApi.StaffRightJson.Write, EnumsApi.StaffRightJson.Read);
        Check(Enum.GetValues<EnumsApi.Priority>(), EnumsApi.PriorityJson.Write, EnumsApi.PriorityJson.Read);
        Check(Enum.GetValues<EnumsApi.BigCode>(), EnumsApi.BigCodeJson.Write, EnumsApi.BigCodeJson.Read);

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

                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(expected));
                reader.Read();
                read(ref reader, JsonSerializerOptions.Default).ShouldBe(value);
            }
        }
    }

    [Fact]
    public void Other_Spellings_Are_Read_Only_Through_The_Options_Own_Enum_Metadata()
    {
        // JsonStringEnumConverter (the enum's attribute, through reflection) also reads other casings and numbers
        foreach (var other in new[] { "\"OPEN\"", "0" })
        {
            var json = $$"""{"status":1,"returnState":{{other}}}""";
            JsonSerializer.Deserialize<EnumsApi.OrderData>(json, JsonSerializerOptions.Default)!.ReturnState.ShouldBe(EnumsApi.ReturnState.open);
            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>(json, GeneratedOnly()));
        }

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"returnState":"unknown"}""", JsonSerializerOptions.Default));
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"code":"Big"}""", GeneratedOnly()));
    }

    [Fact]
    public void Null_And_Malformed_Enum_Members()
    {
        foreach (var options in new[] { GeneratedOnly(), JsonSerializerOptions.Default })
        {
            var read = JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"status":null,"returnState":null,"states":null}""", options)!;
            read.Status.ShouldBe(default);
            read.ReturnState.ShouldBeNull();
            read.States.ShouldBeNull();

            // optional members that do not allow null are left out
            var json = JsonSerializer.Serialize(new EnumsApi.OrderData(), options);
            json.ShouldNotContain("returnState");
            json.ShouldNotContain("states");
            json.ShouldContain("\"priority\":null");

            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"states":[null]}""", options));
            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"states":"open"}""", options));
            Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EnumsApi.OrderData>("""{"returnState":true}""", options));
        }
    }
}
