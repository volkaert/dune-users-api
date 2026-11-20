using DuneUsersApi.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CreatedAtInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(sp.GetRequiredService<CreatedAtInterceptor>()));
builder.Services.AddFastEndpoints();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseFastEndpoints();

await app.RunAsync();

public partial class Program;
