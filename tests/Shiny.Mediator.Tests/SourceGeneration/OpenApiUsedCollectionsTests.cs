using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.Mediator.Tests.SourceGeneration;


/// <summary>
/// <c>UsedCollectionsOnly="true"</c>: the generated resolver wires <c>List&lt;T&gt;</c> and <c>T[]</c> of a model only where
/// generated code reaches that collection through the options - a member (nested in a list or a dictionary too), a response, a
/// request body. Without it every model gets both, a generic instantiation per model in AOT/ReadyToRun code. The same document
/// generated twice: UsedCollectionsApi with the switch, AllCollectionsApi without.
/// </summary>
public class OpenApiUsedCollectionsTests
{
    static IJsonTypeInfoResolver Resolver(Type anyModel, string name)
        => (IJsonTypeInfoResolver)anyModel.Assembly
            .GetType($"{anyModel.Namespace}.{name}", throwOnError: true)!
            .GetField("Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    // the generated resolver only, as in an app without reflection
    static JsonSerializerOptions Used() => new() { TypeInfoResolver = Resolver(typeof(UsedCollectionsApi.Order), "UsedCollectionsApiJsonResolver") };
    static JsonSerializerOptions All() => new() { TypeInfoResolver = Resolver(typeof(AllCollectionsApi.Order), "AllCollectionsApiJsonResolver") };


    [Theory]
    [InlineData(typeof(List<UsedCollectionsApi.OrderItem>))] // member
    [InlineData(typeof(List<List<UsedCollectionsApi.Cell>>))] // nested member
    [InlineData(typeof(List<UsedCollectionsApi.Cell>))]
    [InlineData(typeof(Dictionary<string, List<UsedCollectionsApi.Tag>>))] // dictionary value
    [InlineData(typeof(List<UsedCollectionsApi.Tag>))]
    [InlineData(typeof(List<UsedCollectionsApi.Pet>))] // response
    [InlineData(typeof(List<UsedCollectionsApi.Batch>))] // request body
    [InlineData(typeof(List<string>))]
    public void Collections_Generated_Code_Reaches_Are_Wired(Type type)
        => Used().TryGetTypeInfo(type, out _).ShouldBeTrue();

    [Theory]
    [InlineData(typeof(List<UsedCollectionsApi.Order>))]
    [InlineData(typeof(List<UsedCollectionsApi.Customer>))]
    [InlineData(typeof(List<UsedCollectionsApi.Receipt>))]
    [InlineData(typeof(UsedCollectionsApi.Order[]))]
    [InlineData(typeof(UsedCollectionsApi.OrderItem[]))]
    [InlineData(typeof(UsedCollectionsApi.Pet[]))]
    public void Collections_Nothing_Generated_Reaches_Are_Not(Type type)
        => Used().TryGetTypeInfo(type, out _).ShouldBeFalse();

    [Theory]
    [InlineData(typeof(List<AllCollectionsApi.Order>))]
    [InlineData(typeof(List<AllCollectionsApi.Customer>))]
    [InlineData(typeof(AllCollectionsApi.Receipt[]))]
    [InlineData(typeof(AllCollectionsApi.OrderItem[]))]
    public void Without_The_Switch_Every_Model_Gets_Both(Type type)
        => All().TryGetTypeInfo(type, out _).ShouldBeTrue();

    [Fact]
    public void Models_Round_Trip_Through_The_Trimmed_Resolver()
    {
        var options = Used();
        var json = """{"id":"0198f5a2-7c4e-7000-8000-000000000001","items":[{"name":"Tea"}],"grid":[[{"value":1},{"value":2}]],"tagsByGroup":{"a":[{"name":"x"}]},"labels":["l"],"customer":{"name":"Ann"}}""";

        var order = JsonSerializer.Deserialize(json, (JsonTypeInfo<UsedCollectionsApi.Order>)options.GetTypeInfo(typeof(UsedCollectionsApi.Order)))!;

        order.Items!.Single().Name.ShouldBe("Tea");
        order.Grid!.Single().Select(x => x.Value).ShouldBe([1, 2]);
        order.TagsByGroup!["a"].Single().Name.ShouldBe("x");
        order.Customer!.Name.ShouldBe("Ann");
        JsonSerializer.Serialize(order, (JsonTypeInfo<UsedCollectionsApi.Order>)options.GetTypeInfo(typeof(UsedCollectionsApi.Order))).ShouldBe(json);
    }

    [Fact]
    public void Responses_And_Bodies_Read_Through_The_Trimmed_Resolver()
    {
        var options = Used();

        var pets = JsonSerializer.Deserialize("""[{"name":"Rex"}]""", (JsonTypeInfo<List<UsedCollectionsApi.Pet>>)options.GetTypeInfo(typeof(List<UsedCollectionsApi.Pet>)))!;
        var body = JsonSerializer.Serialize(new List<UsedCollectionsApi.Batch> { new() { Size = 3 } }, (JsonTypeInfo<List<UsedCollectionsApi.Batch>>)options.GetTypeInfo(typeof(List<UsedCollectionsApi.Batch>)));

        pets.Single().Name.ShouldBe("Rex");
        body.ShouldBe("""[{"size":3}]""");
    }
}
