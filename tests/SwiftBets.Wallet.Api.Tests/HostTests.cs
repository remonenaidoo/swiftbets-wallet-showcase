using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Contracts.Grpc.Wallet.V1;
using WalletGrpc = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet;

namespace SwiftBets.Wallet.Api.Tests;

public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private readonly HttpClient _client;
    private readonly Factory _factory;

    public HostTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_is_healthy_without_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Unknown_route_returns_the_error_envelope()
    {
        using var response = await _client.GetAsync(new Uri("/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Top_up_requires_an_operator()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{Guid.NewGuid()}/topup") { Content = JsonContent.Create(new { minorUnits = 100, currency = "ZAR" }) };
        request.Headers.Authorization = new("Bearer", TestJwt.Issue("punter-1", "Punter"));

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Issuing_vouchers_needs_the_payments_approve_permission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/wallet/vouchers/batches") { Content = JsonContent.Create(new { count = 1, amount = 1_000, note = "x" }) };
        request.Headers.Authorization = new("Bearer", TestJwt.Issue("operator-1", "Operator"));

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Metrics_are_exposed()
    {
        var body = await _client.GetStringAsync(new Uri("/metrics", UriKind.Relative), TestContext.Current.CancellationToken);

        body.ShouldContain("process_cpu_seconds_total");
    }

    [Fact]
    public async Task GetBalance_without_a_token_is_unauthenticated()
    {
        var error = await BalanceError(token: null);

        error.StatusCode.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task GetBalance_with_a_punter_token_is_forbidden()
    {
        var error = await BalanceError(TestJwt.Issue("punter-1", "Punter"));

        error.StatusCode.ShouldBe(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task GetBalance_with_a_service_token_is_not_an_auth_error()
    {
        var error = await BalanceError(TestJwt.Issue("client:payout", "Service"), accountId: "not-an-id");

        error.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    private async Task<RpcException> BalanceError(string? token, string? accountId = null)
    {
        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var client = new WalletGrpc.WalletClient(channel);
        var headers = token is null ? null : new Metadata { { "authorization", $"Bearer {token}" } };
        using var call = client.GetBalanceAsync(new GetBalanceRequest { AccountId = accountId ?? Guid.NewGuid().ToString() }, headers, cancellationToken: TestContext.Current.CancellationToken);
        return await Should.ThrowAsync<RpcException>(() => call.ResponseAsync);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
        builder.UseSetting("ConnectionStrings:SbWallet", "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;TrustServerCertificate=True;Connect Timeout=1");
        builder.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1");
            builder.UseSetting("Wallet:Vouchers:CodeSecret", "test-secret");
            builder.UseSetting("Jwt:Authority", TestJwt.Issuer);
            builder.ConfigureServices(services => services.UseTestJwt());
        }
    }
}
