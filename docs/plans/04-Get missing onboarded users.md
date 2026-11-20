# Plan 04 — Endpoint `GET /v1/reconciliations/user-onboardings/missings`

Source : [docs/specs/04-Get missing onboarded users.md](../specs/04-Get%20missing%20onboarded%20users.md)

## Objectif

Permettre à l'appelant de récupérer les onboardings qu'un parcours de `/v1/reconciliations/user-onboardings` (plan 03) n'a pas pu voir : ceux des users insérés en base **après** le `asOf` de ce parcours, avec un `onboardedAt` dans la plage déjà parcourue.

Usage attendu :

1. Parcours initial de `[from, to)` avec `asOf = A1` → users avec `inserted_at <= A1`.
2. Plus tard, appel de `/missings` sur la même plage avec `missingsSince = A1` et `asOf = A2` → users avec `A1 < inserted_at <= A2`.
3. Appels suivants en chaîne : `missingsSince = A2`, `asOf = A3`, etc.

L'union des résultats est exactement l'ensemble des users de la plage avec `inserted_at <= A_n`, sans doublon : chaque appel couvre l'intervalle d'insertion `(missingsSince, asOf]`, qui prolonge le précédent sans le chevaucher.

À la fin de ce plan :

- l'endpoint existe dans `Features/Reconciliations/`, avec sa requête et son validateur, et réutilise la réponse et les règles de validation de l'endpoint du plan 03 ;
- des tests d'intégration couvrent la fenêtre d'insertion, la complémentarité avec le parcours initial, la pagination et la validation.

## Hors périmètre

- Les users dont l'`onboardedAt` est **modifié** après insertion : `inserted_at` ne change pas, l'endpoint ne les voit donc pas comme manquants. Aucun endpoint ne modifie `onboardedAt` aujourd'hui ; le cas demanderait un `updated_at` (spec dédiée).
- Les users supprimés depuis le parcours initial.
- Authentification (même situation que le plan 03 : `AllowAnonymous()`).

## Contrat

### Requête

`GET /v1/reconciliations/user-onboardings/missings?tenantId=acme&fromOnboardedAt=2026-11-20T00:00:00Z&toOnboardedAt=2026-11-21T00:00:00Z&missingsSince=2026-11-21T06:00:00Z&asOf=2026-11-22T06:00:00Z&page=1&pageSize=1000`

Mêmes paramètres et mêmes règles que `/v1/reconciliations/user-onboardings`, plus :

| Paramètre | Type | Obligatoire | Défaut | Règles |
|---|---|---|---|---|
| `missingsSince` | `DateTimeOffset` | oui | — | Borne **exclue** : seuls les users avec `inserted_at > missingsSince` sont renvoyés. Normalement le `asOf` du parcours précédent |
| `asOf` | `DateTimeOffset` | oui | — | Borne **incluse** (`inserted_at <= asOf`), comme dans le plan 03. Strictement après `missingsSince` |

- **Borne basse exclue** : le parcours initial a renvoyé `inserted_at <= A1` ; `/missings` avec `missingsSince = A1` doit renvoyer le complément exact, `inserted_at > A1`. Une borne incluse renverrait une seconde fois les users insérés exactement à `A1`.
- `asOf <= missingsSince` → `400` (`'asOf' must be after 'missingsSince'.`), par cohérence avec `toOnboardedAt` / `fromOnboardedAt`. La fenêtre serait vide : c'est plus probablement une inversion de paramètres qu'une demande voulue.
- Dates converties en UTC avant la requête ; `+` à encoder en `%2B` (voir plan 03).

### Réponse `200`

Format identique à celui de la spec 03, porté par le même type `GetUserOnboardingsResponse` :

```json
{
  "userOnboardings": [
    { "tenantUserId": "u-042", "onboardedAt": "2026-11-20T14:05:00+00:00" }
  ]
}
```

- Tri : `onboardedAt` puis `tenantUserId`, comme le plan 03. La réponse se fusionne ainsi naturellement avec les résultats du parcours initial, et la même stratégie de pagination s'applique (le même `asOf` pour toutes les pages fige la fenêtre).
- Fin de parcours, liste vide, tenant inconnu : mêmes règles que le plan 03.

### Réponse `400`

Les erreurs du plan 03, plus `missingsSince` absent ou illisible, et `asOf <= missingsSince`.

## Requête SQL et index

```csharp
db.Users
    .AsNoTracking()
    .Where(u => u.TenantId == req.TenantId && u.OnboardedAt >= from && u.OnboardedAt < to)
    .Where(u => u.InsertedAt > missingsSince && u.InsertedAt <= asOf)
    .OrderBy(u => u.OnboardedAt).ThenBy(u => u.TenantUserId)
    .Skip((req.Page - 1) * req.PageSize)
    .Take(req.PageSize)
    .Select(u => new UserOnboarding(u.TenantUserId, u.OnboardedAt))
    .ToListAsync(ct);
```

**Pas de nouvel index.** `ix_users_tenant_id_onboarded_at_tenant_user_id` sert le filtre tenant + plage et le tri ; `inserted_at` y est en `INCLUDE`, donc le filtre sur la fenêtre d'insertion est évalué sur les entrées de l'index (parcours d'index seul, à vérifier par `EXPLAIN`).

Coût : l'index lit toutes les entrées du tenant sur la plage d'onboarding, même si peu sont manquantes. C'est le volume du parcours initial, déjà jugé acceptable. Un index `(tenant_id, inserted_at)` ne serait utile que si `/missings` était appelé sur des plages d'onboarding très larges (plusieurs mois) : à reconsidérer sur mesure, pas maintenant.

## Partage avec l'endpoint du plan 03

Les deux endpoints ont les mêmes paramètres, à `missingsSince` près, et la même requête, au filtre `inserted_at` près. Leurs **contrats d'entrée restent indépendants** pour pouvoir évoluer séparément ; seuls la réponse et le code de requête interne sont partagés :

1. **Requête** : `GetMissingUserOnboardingsRequest`, classe `sealed` indépendante qui redéclare tous les paramètres (`TenantId`, `FromOnboardedAt`, `ToOnboardedAt`, `Page`, `PageSize` avec sa propre constante `DefaultPageSize = 1000`, `AsOf`) plus `DateTimeOffset? MissingsSince`. `GetUserOnboardingsRequest` n'est pas modifiée.
2. **Validateur** : `GetMissingUserOnboardingsValidator`, indépendant, qui redéclare toutes les règles (mêmes valeurs : `tenantId` 100 caractères max, `toOnboardedAt` après `fromOnboardedAt`, `page` entre 1 et 1 000 000, `pageSize` entre 1 et 1000), plus `MissingsSince` `NotNull` et `AsOf` `GreaterThan(MissingsSince)` quand les deux sont présents. Ses propres constantes `MaxPage` / `MaxPageSize`.
3. **Réponse** : `GetUserOnboardingsResponse` et `UserOnboarding` réutilisés tels quels : la spec impose un format identique.
4. **Requête EF Core** (détail d'implémentation, pas de contrat) : extraire dans `UserOnboardingQueries.cs` (même dossier) deux extensions :
   - `OnboardedBetween(this IQueryable<User>, tenantId, from, to)` — filtre tenant + plage `[from, to)` ;
   - `ToUserOnboardingsPageAsync(this IQueryable<User>, page, pageSize, ct)` — tri, `Skip` / `Take`, projection.

   Chaque endpoint ne garde que son filtre sur `InsertedAt`. Le test `GetUserOnboardingsEndpointTests` existant sert de filet pour ce refactoring.

## Arborescence cible

```
src/DuneUsersApi/Features/Reconciliations/
├── GetUserOnboardingsEndpoint.cs              # utilise UserOnboardingQueries
├── GetMissingUserOnboardingsEndpoint.cs       # nouveau
├── GetMissingUserOnboardingsRequest.cs        # nouveau
├── GetMissingUserOnboardingsValidator.cs      # nouveau
└── UserOnboardingQueries.cs                   # nouveau

tests/DuneUsersApi.Tests/Features/Reconciliations/
└── GetMissingUserOnboardingsEndpointTests.cs  # nouveau
```

Aucune migration.

## Étapes

### Étape 1 — Extraction de la requête partagée

1. Créer `UserOnboardingQueries` et l'utiliser dans `GetUserOnboardingsEndpoint`.

**Validation** : `dotnet test` reste vert, sans modifier les tests existants.

### Étape 2 — Requête, validateur, endpoint

1. Créer `GetMissingUserOnboardingsRequest` (voir « Partage ») ; `MissingsSince` est documenté comme « normalement le `asOf` du parcours précédent, borne exclue ».
2. Créer `GetMissingUserOnboardingsValidator` (voir « Partage »), avec `WithMessage("'{PropertyName}' must be after '{ComparisonProperty}'.")` pour la comparaison (convention du `CLAUDE.md`).
3. Créer `GetMissingUserOnboardingsEndpoint` : `Get("/v1/reconciliations/user-onboardings/missings")`, `AllowAnonymous()`, `Summary(...)` ; conversion des trois dates en UTC, requête décrite plus haut.

**Validation** : `docker compose up --build`, puis `curl` sur les données de démonstration : un parcours avec `asOf` antérieur à l'insertion renvoie `[]`, `/missings` avec ce même instant en `missingsSince` renvoie les users.

### Étape 3 — Tests d'intégration

`GetMissingUserOnboardingsEndpointTests` (`TestBase<ApiFixture>`, un `TenantId` unique par test, URL construite explicitement comme dans les tests du plan 03). Les instants d'insertion sont encadrés par `app.DatabaseNowAsync` entre deux insertions (insertion de A, `t1 = DatabaseNow`, insertion de B, `t2 = DatabaseNow`, insertion de C).

1. **Fenêtre d'insertion** : avec `missingsSince = t1` et `asOf = t2`, seul B est renvoyé.
2. **Bornes exactes** : avec `missingsSince` = `insertedAt` d'un user (relu en base), il est exclu ; avec `asOf` = son `insertedAt`, il est inclus. Un offset `+02:00` sur ces valeurs donne le même résultat.
3. **Complémentarité avec le parcours initial** (scénario de la spec) : parcours de `/user-onboardings` avec `asOf = A1`, insertion de users dans la plage (dont un ex æquo d'`onboardedAt` avec un user déjà vu), puis `/missings` avec `missingsSince = A1`, `asOf = A2` → renvoie exactement les nouveaux ; l'union des deux réponses égale `/user-onboardings` avec `asOf = A2`, sans doublon.
4. **Filtres tenant et plage** : un user inséré dans la fenêtre mais hors plage `[from, to)`, ou d'un autre tenant, n'est pas renvoyé.
5. **Pagination et tri** : plusieurs users manquants, `pageSize=2` → pages triées par `onboardedAt` puis `tenantUserId`, sans doublon ; dernière page incomplète.
6. **Valeurs par défaut** : sans `page` ni `pageSize`, 1001 users manquants → 1000 éléments (la requête a ses propres valeurs par défaut).
7. **Format JSON** : `{ "userOnboardings": [ { "tenantUserId", "onboardedAt" } ] }`.
8. **Messages d'erreur** : messages exacts, dont `'missingsSince' must not be empty.`, `'asOf' must be after 'missingsSince'.` et `'missingsSince' must be a valid DateTimeOffset. You entered '...'.`.
9. **Validation** (`[Theory]`, `400`) : le validateur étant indépendant, tous les cas du plan 03 sont repris (`tenantId` absent ou trop long, bornes d'onboarding absentes, illisibles ou inversées, `page=0`, `pageSize=0`, `pageSize=1001`, `asOf` absent ou illisible), plus `missingsSince` absent ou illisible, `asOf == missingsSince` et `asOf < missingsSince`.

Seul le cas des offsets sur les bornes d'onboarding n'est pas redupliqué : la conversion en UTC et le filtre de plage passent par le code partagé `UserOnboardingQueries`, testé par le plan 03.

**Validation** : `dotnet test` passe.

### Étape 4 — Documentation

1. `README.md` : ligne dans le tableau « Endpoints », section dédiée (paramètre `missingsSince`, exemple `curl`, enchaînement `asOf` → `missingsSince`).
2. Ce plan : section « Notes d'implémentation ».

## Points d'attention

- **Transactions longues, cas aggravé** : `inserted_at` vaut l'heure de **début** de la transaction d'insertion (`now()`). Une ligne avec `inserted_at <= A1` validée après le parcours initial n'a pas été vue par celui-ci, et `/missings` ne la verra jamais non plus (`inserted_at > A1` est faux) : elle est **perdue**, pas seulement retardée. La marge recommandée au plan 03 (`asOf` = maintenant moins une minute) protège aussi ce cas, tant qu'aucune transaction d'insertion ne dure plus que la marge. Un appelant prudent peut aussi prendre `missingsSince` un peu avant le `asOf` précédent et dédoublonner sur `tenantUserId`.
- **Deux horloges** : `missingsSince` et `asOf` viennent de l'appelant, `inserted_at` de PostgreSQL. Tant que l'appelant chaîne ses propres valeurs (`missingsSince` = son `asOf` précédent), un décalage d'horloge n'introduit ni trou ni doublon entre les appels ; il ne joue que sur la stabilité des pages, comme au plan 03.
- **Règles dupliquées** : les deux validateurs et les deux requêtes portent les mêmes règles et valeurs par défaut, sans lien entre eux. Une évolution voulue sur les deux endpoints (ex. `pageSize` max) doit être reportée à la main dans les deux ; c'est le prix de contrats indépendants.

## Critères d'acceptation

- [x] `dotnet build` passe sans warning.
- [x] `GET /v1/reconciliations/user-onboardings/missings` renvoie les users du tenant onboardés dans `[from, to)` et insérés dans `(missingsSince, asOf]`, triés par `onboardedAt` puis `tenantUserId`, au format de la spec.
- [x] Avec `missingsSince` = le `asOf` d'un parcours de `/user-onboardings`, l'union des deux résultats ne contient ni doublon ni oubli.
- [x] `missingsSince` absent ou illisible, ou `asOf <= missingsSince`, renvoie `400` ; les autres paramètres suivent les mêmes règles qu'au plan 03.
- [x] `GetUserOnboardingsEndpoint` utilise la requête partagée et ses tests restent verts.
- [x] `dotnet test` passe.

## Notes d'implémentation

Écarts et ajustements constatés pendant l'implémentation (2026-11-20) :

- **Conversion en UTC des bornes d'onboarding** : faite dans `UserOnboardingQueries.OnboardedBetween`, donc commune aux deux endpoints ; chaque endpoint ne convertit plus que ses propres bornes d'insertion (`asOf`, `missingsSince`).
- **Bug corrigé dans les deux validateurs** : `.When(...)` placé après une comparaison s'applique par défaut à **toute** la chaîne de la règle, `NotNull` compris. Résultat : si `missingsSince` manquait, l'absence de `asOf` n'était pas signalée ; de même, au plan 03, si `fromOnboardedAt` manquait, l'absence de `toOnboardedAt` n'était pas signalée. La condition est désormais limitée à la comparaison (`ApplyConditionTo.CurrentValidator`). Un test de non-régression a été ajouté au plan 03 (`Error_messages_name_parameters_as_exposed_by_the_api` : bornes absentes → deux erreurs).
- **Tests** : 30 tests ajoutés (`GetMissingUserOnboardingsEndpointTests`, dont 21 cas de validation) ; 73 tests au total, tous verts.
- **Validation Docker Compose** : sur la base locale, parcours initial avec `asOf = A1`, insertion d'un user onboardé dans la plage, puis `/missings` avec `missingsSince = A1` → seul ce user est renvoyé ; l'appel suivant (`missingsSince = A2`) renvoie une liste vide. `EXPLAIN` montre un `Index Only Scan` sur `ix_users_tenant_id_onboarded_at_tenant_user_id`, la fenêtre d'insertion étant évaluée en `Filter` sur les entrées de l'index.

## Décisions

Prises le 2026-11-20 :

1. **Nommage** : le paramètre s'appelle `missingsSince` et la route se termine par `/missings`, comme dans la spec.
2. **`asOf <= missingsSince` rejeté en `400`** plutôt que renvoyer une liste vide.
3. **Deux classes indépendantes** pour la requête et le validateur, plutôt qu'un héritage : plus de duplication, mais des contrats qui peuvent diverger librement.

Restant ouvertes (choix par défaut du plan) :

4. **Borne basse exclue** (`inserted_at > missingsSince`) : c'est ce qui rend `/missings` complémentaire de `inserted_at <= asOf` sans doublon. La spec dit « après », ce qui va dans ce sens.
5. **Tri** : `onboardedAt` puis `tenantUserId` (comme le plan 03), plutôt que par `insertedAt`.
