L'API doit proposer un endpoint appelé `/v1/reconciliations/user-onboardings` qui est unu opération paginée qui retourne les dates d'onboardings des users avec un filtre sur la date d'onboarding.

Ce endpoint prend les paramètres suivants:
- tenantId
- fromOnboardedAt (DateTimeOffset)
- toOnboardedAt (DateTimeOffset)
- page (starts with 1)
- pageSize (default: 1000)

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