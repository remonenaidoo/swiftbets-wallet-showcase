using FluentValidation;
using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Wallet.Api.Endpoints;
using SwiftBets.Wallet.Api.Grpc;
using SwiftBets.Wallet.Application;
using SwiftBets.Wallet.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-wallet");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(VoucherEndpoints.ReadPermission, p => p.RequireClaim("perm", VoucherEndpoints.ReadPermission))
    .AddPolicy(VoucherEndpoints.WritePermission, p => p.RequireClaim("perm", VoucherEndpoints.WritePermission))
    .AddPolicy(PromotionEndpoints.Read, p => p.RequireClaim("perm", PromotionEndpoints.Read))
    .AddPolicy(PromotionEndpoints.Write, p => p.RequireClaim("perm", PromotionEndpoints.Write));
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<WalletFaultInterceptor>();
});
builder.Services.AddScoped<IValidator<TopUpEndpoint.TopUpRequest>, TopUpEndpoint.TopUpRequestValidator>();
builder.Services.AddWalletApplication();
builder.Services.AddWalletInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapGrpcService<WalletGrpcService>();
app.MapTopUp();
app.MapCustomerWallet();
app.MapReconciliation();
app.MapVouchers();
app.MapPromotionAdmin();
app.MapSwiftBetsFaultEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
