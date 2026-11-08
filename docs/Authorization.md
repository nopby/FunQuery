# Authorization

FunQuery tidak membaca token sendiri. Adapter HTTP menyediakan titik otorisasi yang dipanggil
**sebelum cache dan eksekusi**, sehingga request yang ditolak tidak pernah menyentuh data.

## Model

```text
Request ──► IQueryIdentityProvider ──► QueryIdentity (tenant, app, permissions)
                                        │
                                        ▼
                              IQueryAuthorizer.AuthorizeAsync
                                        │
                       allow ───────────┴─────────── deny (401/403)
                         │
                         ▼
                 cache → source/executor
```

- `IQueryIdentityProvider` — host mengisi `QueryIdentity` dari klaim token. Jangan pernah mengambil
  tenant/app dari query string.
- `IQueryAuthorizer` — menerima `QueryAuthorizationRequest` (`SourceName`, `Query`, `Identity`,
  `HttpContext`) dan mengembalikan `QueryAuthorizationDecision`.
- Bila authorizer **tidak** didaftarkan, perilaku lama tetap berlaku (tanpa otorisasi) — cocok untuk
  pemakaian internal/CLI.

## Registrasi

```csharp
builder.Services
    .AddFunQueryIdentityProvider<TokenIdentityProvider>()
    .AddFunQueryAuthorization<OrbitaQueryAuthorizer>();
```

Authorizer custom:

```csharp
public sealed class OrbitaQueryAuthorizer : IQueryAuthorizer
{
    public ValueTask<QueryAuthorizationDecision> AuthorizeAsync(
        QueryAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Identity.TenantId is null)
            return ValueTask.FromResult(QueryAuthorizationDecision.Unauthenticated());

        if (request.SourceName is null)
            return ValueTask.FromResult(QueryAuthorizationDecision.Allow()); // inline $source

        return ValueTask.FromResult(
            request.Identity.HasPermission($"{request.SourceName}.read")
                ? QueryAuthorizationDecision.Allow()
                : QueryAuthorizationDecision.Forbidden(
                    $"Permission '{request.SourceName}.read' is required."));
    }
}
```

## Authorizer berbasis permission per source

```csharp
builder.Services
    .AddFunQueryIdentityProvider<TokenIdentityProvider>()
    .AddFunQueryPermissionAuthorizer()
    .AddSourcePermission("orders", "sales.orders.read")
    .AddSourcePermission("products", "masterdata.products.read");
```

Aturan bawaan:

- Source tanpa policy **ditolak** (fail closed) — source baru tidak otomatis terbuka.
- Permission dicek terhadap `QueryIdentity.Permissions`.
- Source inline (`$source([...])`) diizinkan karena tidak membaca data service.

## Status dan envelope

| Keputusan | Status | `code` |
|---|---|---|
| `Allow()` | lanjut normal | — |
| `Forbidden()` | `403` | `FORBIDDEN` |
| `Unauthenticated()` | `401` | `UNAUTHENTICATED` |

Body memakai envelope error FunQuery yang sama (`code`, `message`).

## Interaksi dengan cache

- Otorisasi dijalankan **sebelum** lookup cache, sehingga penolakan tidak bisa mengambil hasil dari
  cache.
- `IQueryCacheScopeProvider` tetap wajib di host multi-tenant agar hasil tenant A tidak dipakai
  tenant B; scope juga harus memuat role bila permission berbeda per role.

## Yang perlu disediakan host

1. Implementasi `IQueryIdentityProvider` (klaim `tenant_id`, `app_id`, `permissions`, `sub`).
2. Implementasi `IQueryAuthorizer` atau pemakaian `AddFunQueryPermissionAuthorizer()`.
3. `IQueryCacheScopeProvider` untuk caching multi-tenant.
