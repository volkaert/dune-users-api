# Plan 03 — Endpoint `GET /v1/reconciliations/user-onboardings`

Source : [docs/specs/03-Get onboarded users.md](../specs/03-Get%20onboarded%20users.md)

## Objectif

Exposer un endpoint paginé qui renvoie, pour un tenant, les users onboardés sur une plage de dates, avec pour chacun son `tenantUserId` et son `onboardedAt`. L'appelant (processus de réconciliation) parcourt les pages jusqu'à épuisement.

À la fin de ce plan :

- l'endpoint `GET /v1/reconciliations/user-onboardings` existe dans `Features/Reconciliations/`, avec sa requête, sa réponse et son validateur ;
- la pagination est stable (tri déterministe) et s'appuie sur un index adapté ;
- des tests d'intégration couvrent le filtrage, la pagination, l'isolation des tenants et la validation.

## Hors périmètre

- Authentification et autorisation : l'API n'en a pas encore. L'endpoint est `AllowAnonymous()`, comme `/health` (voir « Points d'attention »).
- Le nombre total de résultats (`totalCount`) : il coûte un `COUNT(*)` par page et n'est pas demandé par la spec.
- La pagination par curseur (keyset) : la spec impose `page` / `pageSize`.

## Contrat

### Requête

`GET /v1/reconciliations/user-onboardings?tenantId=acme&fromOnboardedAt=2026-11-20T00:00:00Z&toOnboardedAt=2026-11-21T00:00:00Z&asOf=2026-11-21T06:00:00Z&page=1&pageSize=1000`

| Paramètre | Type | Obligatoire | Défaut | Règles |
|---|---|---|---|---|
| `tenantId` | `string` | oui | — | Non vide, 100 caractères max (longueur de la colonne) |
| `fromOnboardedAt` | `DateTimeOffset` | oui | — | Borne **incluse** |
| `toOnboardedAt` | `DateTimeOffset` | oui | — | Borne **exclue**, strictement après `fromOnboardedAt` |
| `page` | `int` | non | `1` | `>= 1` |
| `pageSize` | `int` | non | `1000` | Entre `1` et `1000` |
| `asOf` | `DateTimeOffset` | oui | — | Fourni par l'appelant. Borne **exclue** : seuls les users avec `inserted_at < asOf` sont renvoyés (voir « Révision : `asOf` ») |

- Plage semi-ouverte `[from, to)` : deux plages consécutives (`[J, J+1)`, `[J+1, J+2)`) ne se recouvrent pas et ne laissent pas de trou, ce qui convient à une réconciliation par jour. C'est aussi la convention utilisée dans les tests du plan 02.
- Les dates acceptent n'importe quel offset (`Z`, `+02:00`…) ; elles sont converties en UTC avant la requête (Npgsql refuse un paramètre `timestamptz` avec un offset non nul).

### Réponse `200`

```json
{
  "userOnboardings": [
    { "tenantUserId": "u-001", "onboardedAt": "2026-11-20T08:00:00+00:00" },
    { "tenantUserId": "u-002", "onboardedAt": "2026-11-20T09:15:00+00:00" }
  ]
}
```

- `onboardedAt` est renvoyé en UTC (l'offset d'origine n'est pas conservé, voir plan 02).
- Tri : `onboarded_at` croissant, puis `tenant_user_id` pour départager les égalités. Sans ce second critère, l'ordre entre deux users onboardés au même instant n'est pas garanti et un user pourrait apparaître sur deux pages ou sur aucune.
  - `tenant_user_id` est une chaîne opaque, de format inconnu (pas forcément un UUID ni une valeur croissante). Le tri ne lui donne aucun sens métier ou chronologique : il sert seulement d'ordre total et stable entre ex æquo. Il suffit pour cela que la valeur soit **unique dans le tenant**, ce que garantit l'index `ux_users_tenant_id_tenant_user_id`, et comparable, ce qu'est toute chaîne (ordre de la collation de la base).
  - L'ordre des ex æquo n'est donc pas un ordre d'arrivée et le client ne doit pas l'interpréter.
- Fin de parcours : une page qui contient moins de `pageSize` éléments (éventuellement vide) est la dernière. Une page au-delà de la fin renvoie `200` avec une liste vide, pas `404`.
- Tenant inconnu ou sans user sur la plage : `200` avec une liste vide.

### Réponse `400`

Paramètre manquant, mal formé (date illisible) ou hors règles : réponse d'erreur standard de FastEndpoints (`ErrorResponse`), avec un message par paramètre en erreur.

## Requête SQL et index

Requête EF Core attendue (lecture seule, projection directe vers le DTO) :

```csharp
db.Users
    .AsNoTracking()
    .Where(u => u.TenantId == req.TenantId && u.OnboardedAt >= from && u.OnboardedAt < to)
    .Where(u => u.InsertedAt < asOf)
    .OrderBy(u => u.OnboardedAt).ThenBy(u => u.TenantUserId)
    .Skip((req.Page - 1) * req.PageSize)
    .Take(req.PageSize)
    .Select(u => new UserOnboarding(u.TenantUserId, u.OnboardedAt))
    .ToListAsync(ct);
```

L'index existant `ix_users_onboarded_at` (sur `onboarded_at` seul) n'est pas adapté : il faudrait ensuite filtrer le tenant ligne par ligne puis trier. On le remplace par un index composite :

- `ix_users_tenant_id_onboarded_at_tenant_user_id` sur `(tenant_id, onboarded_at, tenant_user_id)`, avec `INCLUDE (inserted_at)` depuis l'ajout de `asOf`.

Il sert à la fois l'égalité sur le tenant, la plage de dates et le tri complet, sans étape de tri, et couvre toutes les colonnes lues (parcours d'index seul possible). L'index `ix_users_onboarded_at` est supprimé : il avait été créé en prévision de cette requête, qui filtre toujours par tenant.

## Arborescence cible

```
src/DuneUsersApi/
├── Features/Reconciliations/
│   ├── GetUserOnboardingsEndpoint.cs
│   ├── GetUserOnboardingsRequest.cs
│   ├── GetUserOnboardingsResponse.cs      # + record UserOnboarding
│   └── GetUserOnboardingsValidator.cs
└── Infrastructure/Persistence/
    ├── Users/UserConfiguration.cs         # index remplacé
    └── Migrations/<horodatage>_IndexUsersByTenantAndOnboardedAt.cs

tests/DuneUsersApi.Tests/
├── Fixtures/TestUsers.cs                  # fabrique de User partagée (extraite de UserPersistenceTests)
└── Features/Reconciliations/
    └── GetUserOnboardingsEndpointTests.cs
```

## Étapes

### Étape 1 — Index composite et migration

1. Dans `UserConfiguration`, remplacer l'index sur `OnboardedAt` par :
   ```csharp
   builder.HasIndex(u => new { u.TenantId, u.OnboardedAt, u.TenantUserId })
       .HasDatabaseName("ix_users_tenant_id_onboarded_at_tenant_user_id");
   ```
2. Générer la migration :
   ```bash
   dotnet ef migrations add IndexUsersByTenantAndOnboardedAt \
     --project src/DuneUsersApi \
     --output-dir Infrastructure/Persistence/Migrations
   ```
3. Relire le SQL (`dotnet ef migrations script`) : un `DROP INDEX ix_users_onboarded_at` et un `CREATE INDEX` sur les trois colonnes, dans cet ordre.

**Validation** : `dotnet build` passe ; le test `Model_has_no_pending_changes` reste vert.

### Étape 2 — Requête, réponse et validateur

1. `GetUserOnboardingsRequest` : `string? TenantId`, `DateTimeOffset? FromOnboardedAt`, `DateTimeOffset? ToOnboardedAt`, `int Page = 1`, `int PageSize = 1000`. Les champs obligatoires sont nullables pour qu'un paramètre absent soit signalé par le validateur (`400` explicite) au lieu d'être lié à `default` (`0001-01-01`). FastEndpoints lie les paramètres de query par nom, sans tenir compte de la casse.
2. `GetUserOnboardingsResponse` :
   ```csharp
   public sealed record UserOnboarding(string TenantUserId, DateTimeOffset OnboardedAt);

   public sealed record GetUserOnboardingsResponse(IReadOnlyList<UserOnboarding> UserOnboardings);
   ```
   La sérialisation camelCase par défaut produit directement `userOnboardings`, `tenantUserId` et `onboardedAt` : aucun attribut `[JsonPropertyName]` n'est nécessaire.
3. `GetUserOnboardingsValidator : Validator<GetUserOnboardingsRequest>` :
   - `TenantId` : `NotEmpty`, `MaximumLength(100)` ;
   - `FromOnboardedAt`, `ToOnboardedAt` : `NotNull` ;
   - `ToOnboardedAt` `GreaterThan(FromOnboardedAt)` quand les deux sont présents (comparaison d'instants, indépendante des offsets) ;
   - `Page` : `GreaterThanOrEqualTo(1)` ;
   - `PageSize` : `InclusiveBetween(1, 1000)`.
   - Prévenir le débordement de `(Page - 1) * PageSize` : borner `Page` (ex. `LessThanOrEqualTo(1_000_000)`) ou calculer l'offset en `long`.

### Étape 3 — Endpoint

`GetUserOnboardingsEndpoint(AppDbContext db) : Endpoint<GetUserOnboardingsRequest, GetUserOnboardingsResponse>` :

1. `Configure()` : `Get("/v1/reconciliations/user-onboardings")`, `AllowAnonymous()`, `Summary(...)` décrivant les paramètres et les réponses `200` / `400`. Le préfixe `v1` est écrit dans la route : le versioning intégré de FastEndpoints n'apporte rien tant qu'il n'existe qu'une version.
2. `ExecuteAsync` : convertir les bornes en UTC (`ToUniversalTime()`), exécuter la requête décrite plus haut, renvoyer la page.

**Validation** : `docker compose up --build`, insérer quelques users, puis appeler l'endpoint avec `curl` (en encodant `+` en `%2B` dans les offsets).

### Étape 4 — Tests d'intégration

1. Extraire la fabrique `NewUser(...)` de `UserPersistenceTests` vers `tests/DuneUsersApi.Tests/Fixtures/TestUsers.cs` (méthode statique), et l'utiliser dans les deux classes de tests. Ajouter un utilitaire d'insertion en lot via `AppDbContext`.
2. Créer `tests/DuneUsersApi.Tests/Features/Reconciliations/GetUserOnboardingsEndpointTests.cs`, basé sur `TestBase<ApiFixture>`. Chaque test utilise un `TenantId` unique. Les appels se font avec une URL construite explicitement (`app.Client.GetAsync(url)`, valeurs encodées avec `Uri.EscapeDataString`) : cela teste le contrat réel (noms des paramètres, encodage des offsets, nom `userOnboardings` dans le JSON).

Cas à couvrir :

1. **Filtrage par plage** : seuls les users dont `onboardedAt` est dans `[from, to)` sont renvoyés ; un user à `from` exactement est inclus, un user à `to` exactement est exclu.
2. **Bornes avec offset** : `from = 2026-11-20T02:00:00+02:00` équivaut à `2026-11-20T00:00:00Z` ; les dates renvoyées sont en UTC.
3. **Tri** : résultats triés par `onboardedAt`. Pour des users onboardés au même instant, avec des `tenantUserId` de formats variés (ex. `"42"`, `"jdoe@acme"`, `"Z-9"`, un UUID) insérés dans le désordre : l'ordre est stable d'un appel à l'autre, et une pagination avec `pageSize=1` les renvoie tous une seule fois. Le test ne vérifie pas un ordre précis entre ex æquo, qui dépend de la collation.
4. **Pagination** : 5 users, `pageSize=2` → pages 1 et 2 pleines, page 3 avec 1 élément, page 4 vide ; la concaténation des pages donne tous les users, sans doublon.
5. **Valeurs par défaut** : sans `page` ni `pageSize`, 1001 users → 1000 éléments renvoyés, en commençant par le plus ancien.
6. **Isolation des tenants** : les users d'un autre tenant sur la même plage ne sont pas renvoyés.
7. **Liste vide** : tenant inconnu → `200` et `"userOnboardings": []`.
8. **Format JSON** : la propriété racine s'appelle `userOnboardings` et chaque élément a exactement `tenantUserId` et `onboardedAt` (lecture du JSON brut avec `JsonDocument`).
9. **Validation** (`[Theory]`, `400`) : `tenantId` absent ; `fromOnboardedAt` ou `toOnboardedAt` absent ; date illisible ; `to == from` ; `to < from` ; `page=0` ; `pageSize=0` ; `pageSize=1001`.

**Validation** : `dotnet test` passe.

### Étape 5 — Documentation

1. Ajouter l'endpoint au tableau « Endpoints » du `README.md`, avec un exemple d'appel `curl`.
2. Corriger dans le plan 02 la mention de l'index `ix_users_onboarded_at` (renvoi vers ce plan).
3. Compléter ce plan avec une section « Notes d'implémentation ».

## Points d'attention

- **Encodage du `+` dans l'URL** : dans une query string, `+` est décodé en espace. `fromOnboardedAt=2026-11-20T10:00:00+02:00` non encodé devient illisible (`400`). Les clients doivent encoder (`%2B`) ou utiliser `Z`. À documenter dans le `README.md`.
- **Date sans offset** : `2026-11-20T10:00:00` est interprétée dans le fuseau du serveur (UTC dans le conteneur, mais pas forcément en local). Le validateur ne peut pas le détecter une fois la valeur liée. Option : lier les bornes en `string` et exiger un offset explicite (voir « Décisions à valider »).
- **Pagination par offset et données qui changent** : sans `asOf`, si un user est inséré dans la plage pendant le parcours avec une position déjà lue, les pages suivantes se décalent (un élément vu deux fois, un autre jamais vu). `asOf` règle ce cas (voir « Révision : `asOf` »). La pagination par curseur (`after=(onboardedAt, tenantUserId)`) éliminerait en plus le coût croissant de `OFFSET` sur les pages lointaines.
- **Sécurité** : sans authentification, n'importe quel appelant peut lire les identifiants de n'importe quel tenant en passant son `tenantId`. À traiter dans une spec dédiée (le `tenantId` viendra probablement du jeton plutôt que de la query).

## Critères d'acceptation

- [x] `dotnet build` passe sans warning.
- [x] `GET /v1/reconciliations/user-onboardings` renvoie les users du tenant onboardés dans `[from, to)`, triés par `onboardedAt` puis `tenantUserId`, au format de la spec.
- [x] La pagination (`page`, `pageSize` par défaut à 1000) permet de parcourir tous les résultats sans doublon ni oubli.
- [x] Les paramètres invalides ou manquants renvoient `400`.
- [x] La migration remplace `ix_users_onboarded_at` par l'index composite.
- [x] `dotnet test` passe.

## Notes d'implémentation

Écarts et ajustements constatés pendant l'implémentation (2026-11-20) :

- **Décisions à valider** : elles restent ouvertes ; l'implémentation applique les choix par défaut du plan (`[from, to)`, bornes obligatoires, `pageSize` max 1000, réponse sans métadonnées, dates sans offset acceptées, index remplacé).
- **`tenantUserId`** : confirmé comme chaîne opaque de format inconnu. Il ne sert qu'à départager les ex æquo ; le départage par la clé technique `id` a été écarté.
- **Borne de `page`** : `page` est limité à 1 000 000 (`GetUserOnboardingsValidator.MaxPage`) pour que `(page - 1) * pageSize` tienne dans un `int`.
- **Messages d'erreur homogènes** : par défaut, FluentValidation découpait les noms de propriété en mots (`'tenant Id' must not be empty.`) et FastEndpoints renvoyait un format différent pour les erreurs de liaison (`Value [...] is not valid for a [DateTimeOffset] property!`). Dans `Program.cs`, `ValidatorOptions.Global.DisplayNameResolver` et `Binding.FailureMessage` font désormais citer chaque paramètre sous son nom d'API en camelCase, comme les clés de `errors` : `'tenantId' must not be empty.`, `'toOnboardedAt' must be after 'fromOnboardedAt'.`, `'fromOnboardedAt' must be a valid DateTimeOffset. You entered 'pas-une-date'.`. Ce réglage est global : il s'applique aussi aux futurs endpoints. Le test `Error_messages_name_parameters_as_exposed_by_the_api` vérifie les messages exacts.
- **Fabrique de test** : `TestUsers.New(...)` et l'extension `ApiFixture.InsertAsync(users, ct)` sont partagées par `UserPersistenceTests` et les tests de l'endpoint.
- **Validation Docker Compose** : faite sur une stack isolée (`docker compose -p dune-plan03` avec d'autres ports et un volume neuf, supprimée ensuite) : migration appliquée, index présent, réponses conformes. `EXPLAIN` montre un `Index Only Scan` sur `ix_users_tenant_id_onboarded_at_tenant_user_id`, sans étape de tri. Constaté au passage : un `+` non encodé renvoie `400` (`2026-11-20T02:00:00 02:00` illisible), une date sans offset est acceptée et lue en UTC dans le conteneur.
- **Base locale** : la migration `IndexUsersByTenantAndOnboardedAt` sera appliquée au prochain démarrage de l'API en Development (`docker compose up --build`).

## Décisions à valider

1. **Bornes de la plage** : `[from, to)` (choix du plan), ou bornes toutes deux incluses ?
2. **Bornes obligatoires** : le plan exige `fromOnboardedAt` et `toOnboardedAt`. Faut-il permettre une plage ouverte (ex. « tout depuis `from` ») ?
3. **`pageSize` maximum** : le plan le fixe à 1000 (la valeur par défaut). Faut-il autoriser plus ?
4. **Fin de parcours** : la réponse suit exactement la spec (liste seule ; page incomplète = dernière page). Faut-il ajouter des métadonnées (`page`, `pageSize`, `hasNextPage`) ? `hasNextPage` est peu coûteux (lire `pageSize + 1` lignes).
5. **Dates sans offset** : les accepter (interprétées dans le fuseau du serveur) ou les rejeter en `400` ?
6. **Index** : remplacer `ix_users_onboarded_at` (choix du plan) ou le conserver en plus, si une autre requête doit filtrer par date tous tenants confondus ?

## Révision : `asOf` (2026-11-20)

Ajout d'un paramètre `asOf` pour que des insertions pendant le parcours ne décalent pas les pages.

### Contrat

- Requête : `asOf` **obligatoire**, fourni par l'appelant (`DateTimeOffset`, n'importe quel offset, converti en UTC). Seuls les users avec `inserted_at < asOf` (borne exclue, voir « Révision : bornes ») sont renvoyés. Absent → `400` (`'asOf' must not be empty.`).
- Réponse : inchangée (format de la spec) ; l'appelant connaît déjà `asOf`.
- Usage attendu : l'appelant fixe `asOf` au début du parcours et passe la même valeur pour toutes les pages.

### Choix

- **`asOf` fourni par l'appelant**, jamais calculé par le serveur. Une première version renvoyait l'heure de la base quand `asOf` était absent ; elle a été abandonnée à la demande : l'appelant maîtrise seul l'instant de référence.
- **Index** : `inserted_at` est ajouté en `INCLUDE` à `ix_users_tenant_id_onboarded_at_tenant_user_id` (migration `IncludeInsertedAtInOnboardingsIndex`) : il n'est pas une clé de l'index (ni filtre d'égalité ni tri), mais sa présence permet de garder un parcours d'index seul.

### Limites

- **Deux horloges** : `inserted_at` vient de l'horloge de PostgreSQL (`now()`), `asOf` de celle de l'appelant. Si l'horloge de l'appelant est en avance, `asOf` est dans le futur pour la base : des users insérés pendant le parcours ont `inserted_at < asOf` et peuvent encore décaler les pages. `asOf` n'est pas contrôlé (pas de rejet d'un `asOf` futur).
- **Heure de début de transaction** : `now()` est l'heure de début de la transaction d'insertion, pas celle de sa validation. Une transaction commencée avant `asOf` mais validée après produit une ligne avec `inserted_at < asOf` qui apparaît en cours de parcours.
- **Recommandation aux appelants** : choisir `asOf` légèrement dans le passé (ex. heure courante moins une minute). Cette marge absorbe à la fois un décalage d'horloge et les transactions en cours, tant qu'ils restent inférieurs à la marge.
- **Insertions tardives** : un user inséré après `asOf` avec un `onboardedAt` dans la plage n'est pas vu par ce parcours. Il le sera par un parcours ultérieur de la même plage avec un `asOf` plus récent ; c'est au processus de réconciliation de rejouer les plages concernées si nécessaire.

### Tests ajoutés

1. `asOf` est une borne exclue et accepte un offset : un user est exclu avec `asOf` = son `insertedAt` exprimé en `+02:00`, et renvoyé avec une microseconde de plus.
2. Un user inséré après `asOf` est exclu ; il est inclus avec un `asOf` postérieur.
3. Scénario de décalage : 4 users, `pageSize=2`, insertion d'un user dans la page 1 entre les deux appels → avec le même `asOf`, la page 2 reste `[C, D]` ; avec un `asOf` postérieur à l'insertion, elle devient `[B, C]`.
4. `asOf` absent ou illisible → `400` (`'asOf' must not be empty.`, `'asOf' must be a valid DateTimeOffset. You entered '...'.`).
5. Le format JSON de la réponse reste `{ "userOnboardings": [...] }`.

## Révision : bornes (2026-11-20)

`asOf` passe de borne incluse (`inserted_at <= asOf`) à borne **exclue** (`inserted_at < asOf`), pour que toutes les plages de dates de l'API suivent la même convention, borne basse incluse et borne haute exclue, comme `[fromOnboardedAt, toOnboardedAt)`. L'endpoint `/missings` (plan 04) devient alors `[missingsSince, asOf)` et s'enchaîne sans doublon ni trou avec ce parcours. En pratique, l'écart ne porte que sur les users insérés exactement à l'instant `asOf`, à la microseconde près.

## Révision : messages en anglais et dates vides (2026-11-20)

Deux défauts du traitement des paramètres, constatés en implémentant `dune-transactions-api` et corrigés dans `Program.cs`. Ils concernent les deux endpoints (plans 03 et 04).

- **Messages traduits selon la culture du serveur** : FluentValidation traduisait ses messages selon la culture courante. Sur un poste configuré en français, `'tenantId' must not be empty.` devenait `'tenantId' ne doit pas être vide.`, et les deux tests `Error_messages_name_parameters_as_exposed_by_the_api` échouaient ; dans le conteneur (culture invariante), les messages restaient en anglais. `ValidatorOptions.Global.LanguageManager.Culture` est désormais fixée à la culture invariante : les messages sont toujours en anglais, comme dans le guide.
- **Date vide lue comme `0001-01-01`** : un paramètre de date présent mais vide (`asOf=`, `fromOnboardedAt=`, `missingsSince=`) n'était pas signalé comme absent, car FastEndpoints le liait à `default(DateTimeOffset)`. Conséquences : `fromOnboardedAt=` ou `missingsSince=` renvoyaient tout l'historique (d'où des doublons dans une chaîne `/missings`), et `asOf=` une liste vide en `200`. Un parseur `DateTimeOffset?` (`Binding.ValueParserFor`) traite désormais une valeur vide ou blanche comme absente, ce qui donne `400` avec `'<param>' must not be empty.`. Les autres valeurs sont lues comme avant (date sans offset lue dans le fuseau du serveur).
- **Tests** : cas « dates vides » ajoutés aux tests `Error_messages_name_parameters_as_exposed_by_the_api` et `Rejects_invalid_parameters` des deux endpoints ; 78 tests au total, tous verts.
- **Docs** : README, CLAUDE.md et `HOW-TO-USE-THE-API.md` (section « Errors ») mis à jour. `HOW-TO-USE-THE-API.pdf` n'a pas été régénéré.
