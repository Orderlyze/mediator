using Shiny.Mediator.Infrastructure;
using Shiny.Mediator.Infrastructure.Impl;
using Xunit.Abstractions;

namespace Shiny.Mediator.Tests;


/// <summary>
/// The mediator source generator cannot see handlers that another generator emitted, so the OpenAPI/HTTP client brings
/// its own executor. Without it every generated request took LocalRequestExecutor (MakeGenericType per call, not Native
/// AOT safe). The handlers here are replaced by fakes; the dispatch path is the generated one.
/// </summary>
public class GeneratedHttpRequestExecutorTests(ITestOutputHelper output)
{
    [Fact]
    public void Registering_the_client_twice_adds_its_executor_once()
    {
        var services = new ServiceCollection();
        services.AddGeneratedOpenApiClient();
        services.AddGeneratedOpenApiClient();

        services
            .Count(x => x.ServiceType == typeof(IRequestExecutor) && x.ImplementationType == typeof(__ShinyMediatorOpenApiClientRequestExecutor))
            .ShouldBe(1);
    }


    [Fact]
    public void The_director_picks_the_generated_executor_only_for_generated_requests()
    {
        var director = this.Build().GetRequiredService<IMediatorDirector>();

        director.GetRequestExecutor(new EnumsApi.GetOrderHttpRequest()).ShouldBeOfType<__ShinyMediatorOpenApiClientRequestExecutor>();
        director.GetRequestExecutor(new NotGeneratedRequest()).ShouldBeOfType<LocalRequestExecutor>();
    }


    [Fact]
    public async Task A_generated_request_runs_its_handler_and_middleware()
    {
        var order = new EnumsApi.OrderData { OrderId = Guid.NewGuid() };
        var middleware = new RecordingMiddleware();
        var mediator = this.Build(services =>
        {
            services.AddSingleton<IRequestHandler<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>>(new FixedHandler<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>(order));
            services.AddSingleton<IRequestMiddleware<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>>(middleware);
        }).GetRequiredService<IMediator>();

        var (_, result) = await mediator.Request(new EnumsApi.GetOrderHttpRequest());

        result.ShouldBeSameAs(order);
        middleware.Calls.ShouldBe(1);
    }


    [Fact]
    public async Task A_request_asked_through_a_covariant_result_still_returns_the_result()
    {
        var order = new EnumsApi.OrderData { OrderId = Guid.NewGuid() };
        var mediator = this.Build(services =>
            services.AddSingleton<IRequestHandler<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>>(new FixedHandler<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>(order))
        ).GetRequiredService<IMediator>();

        IRequest<object> request = new EnumsApi.GetOrderHttpRequest();
        var (_, result) = await mediator.Request(request);

        result.ShouldBeSameAs(order);
    }


    [Fact]
    public async Task A_value_type_result_comes_back_unchanged()
    {
        var mediator = this.Build(services =>
            services.AddSingleton<IRequestHandler<EnumsApi.GetOrderStatusHttpRequest, EnumsApi.OrderStatus>>(new FixedHandler<EnumsApi.GetOrderStatusHttpRequest, EnumsApi.OrderStatus>(EnumsApi.OrderStatus.Voided))
        ).GetRequiredService<IMediator>();

        var (_, result) = await mediator.Request(new EnumsApi.GetOrderStatusHttpRequest());

        result.ShouldBe(EnumsApi.OrderStatus.Voided);
    }


    IServiceProvider Build(Action<IServiceCollection>? fakes = null)
    {
        var services = new ServiceCollection();
        services.AddXUnitLogging(output);
        services.AddConfiguration();
        // fakes first: the generated registration uses TryAddSingleton and keeps them
        fakes?.Invoke(services);
        services.AddShinyMediator(x => x.AddGeneratedOpenApiClient(), includeStandardMiddleware: false);
        services.AddSingleton<IRequestHandler<NotGeneratedRequest, string>, NotGeneratedHandler>();
        return services.BuildServiceProvider();
    }
}


file record NotGeneratedRequest : IRequest<string>;

file class NotGeneratedHandler : IRequestHandler<NotGeneratedRequest, string>
{
    public Task<string> Handle(NotGeneratedRequest request, IMediatorContext context, CancellationToken cancellationToken) => Task.FromResult("local");
}

file class FixedHandler<TRequest, TResult>(TResult result) : IRequestHandler<TRequest, TResult> where TRequest : IRequest<TResult>
{
    public Task<TResult> Handle(TRequest request, IMediatorContext context, CancellationToken cancellationToken) => Task.FromResult(result);
}

file class RecordingMiddleware : IRequestMiddleware<EnumsApi.GetOrderHttpRequest, EnumsApi.OrderData>
{
    public int Calls { get; private set; }

    public Task<EnumsApi.OrderData> Process(
        IMediatorContext context,
        RequestHandlerDelegate<EnumsApi.OrderData> next,
        CancellationToken cancellationToken
    )
    {
        this.Calls++;
        return next();
    }
}
