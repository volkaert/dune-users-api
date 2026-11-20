L'API doit proposer un endpoint appelé `/v1/reconciliations/user-onboardings` qui est une opération paginée qui retourne les dates d'onboardings des users avec un filtre sur la date d'onboarding.

Ce endpoint prend les paramètres suivants:
- tenantId
- fromOnboardedAt (DateTimeOffset)
- toOnboardedAt (DateTimeOffset)
- page (starts with 1)
- pageSize (default: 1000)
- asOf (DateTimeOffset, fourni par l'appelant) : seuls les users insérés en base avant cet instant (insertedAt < asOf, asOf exclu) sont renvoyés. L'appelant fixe asOf au début du parcours et passe la même valeur pour toutes les pages, afin que des insertions pendant le parcours ne décalent pas les pages.

Ce endpoint retourne des pages de la forme:
```json
{
    "userOnboardings": [
        {
            "tenantUserId": "<tenantUserId>",
            "onboardedAt": "<OnboardedAt>"
        }
    ]
}  
```