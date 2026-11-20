using DuneUsersApi.Infrastructure.Persistence;
using System.Globalization;
using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());
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

// Messages toujours en anglais, comme le contrat documenté : sans ce réglage, FluentValidation les traduit selon la
// culture du serveur (en français sur un poste configuré en français, en anglais dans le conteneur).
ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.InvariantCulture;

app.UseFastEndpoints(c =>
{
    c.Binding.FailureMessage = (type, propertyName, value) =>
        $"'{JsonNamingPolicy.CamelCase.ConvertName(propertyName)}' must be a valid "
        + $"{(Nullable.GetUnderlyingType(type) ?? type).Name}. You entered '{value}'.";

    // Une date présente mais vide ("asOf=") est traitée comme absente, et donc signalée par le validateur
    // ("must not be empty"). Le parseur par défaut la lit comme 0001-01-01 : "missingsSince=" renverrait alors
    // tout l'historique. Une date sans offset reste lue dans le fuseau du serveur, comme avec le parseur par défaut.
    c.Binding.ValueParserFor<DateTimeOffset?>(input =>
    {
        var text = input.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParseResult(true, null);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? new ParseResult(true, value)
            : new ParseResult(false, null);
    });
});

await app.RunAsync();

public partial class Program;
