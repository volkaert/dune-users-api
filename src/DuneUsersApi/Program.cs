using DuneUsersApi.Infrastructure.Persistence;
using System.Text.Json;
using FastEndpoints;
using FluentValidation;
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

// Messages d'erreur homogènes : chaque message cite le paramètre sous le nom exposé par l'API (camelCase, comme
// les clés de "errors"), et non sous un nom découpé en mots ("tenant Id").
ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
    member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);

app.UseFastEndpoints(c => c.Binding.FailureMessage = (type, propertyName, value) =>
    $"'{JsonNamingPolicy.CamelCase.ConvertName(propertyName)}' must be a valid "
    + $"{(Nullable.GetUnderlyingType(type) ?? type).Name}. You entered '{value}'.");

await app.RunAsync();

public partial class Program;
