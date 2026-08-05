# Mimari

## Bağımlılık yönü

```text
Domain <- Application <- Infrastructure <- Api
                          ^
Browser -> Web BFF -> HTTP/JSON -> Api -> SQL Server
```

- Domain framework bağımsız entity invariant’larını ve request lifecycle’ını korur.
- Application, MediatR CQRS use-case’lerini ve küçük persistence/query abstraction’larını içerir; EF Core veya HTTP tipi içermez.
- Infrastructure, aggregate-focused command repository’lerini, `AsNoTracking` DB projection query servislerini, SQL Server, transaction ve güvenlik adapter’larını uygular.
- API ince controller, exact-role authorization, JWT Bearer, rate limiting, Swagger ve ProblemDetails sınırıdır.
- Web ayrı MVC/Razor BFF’tir; backend project reference taşımaz. Tokenlar yalnız server-side session store’dadır; browser storage/HTML/JavaScript’e çıkmaz.

## Yazma ve okuma ayrımı

```text
Command -> Handler -> Domain behavior -> Focused repository -> UnitOfWork/Transaction -> SQL Server
Query   -> Handler -> Read service -> AsNoTracking + filter/sort/projection -> DTO
```

Request state + approval + audit ile user/department/password mutation + token revoke + audit tek transaction ve tek SaveChanges sınırındadır. `RowVersion` stale mutation’ı 409 `concurrency.conflict` olarak döner.

Her MediatR handler tek use-case orchestration’ını doğrudan gösterir. Admin user ve department komutları ortak bir CRUD/`Operations` servisine delegasyon yapmaz; repository kontrolleri, domain davranışı, transaction/UoW, audit ve gerektiğinde token revoke sınırı ilgili handler içinde görünürdür. Paylaşılan `AdministrationHandlerRules` yalnız kimlik, normalizasyon ve aktif department doğrulaması gibi küçük kuralları sağlar; use-case yürütmez.

## Request lifecycle

```text
Draft -> PendingManagerApproval -> Approved
                           \----> PendingAdminApproval -> Approved
PendingManagerApproval/PendingAdminApproval -> Rejected
Draft/PendingManagerApproval -> Cancelled
```

Manager kendi talebini karara bağlayamaz. Admin Employee self-service kullanmaz ve Manager rolünü devralmaz. Soft delete bütün aktif list/detail/queue sorgularından kaydı çıkarır, history kayıtlarını korur.

## Rol matrisi

| Capability | Employee | Manager | Admin |
|---|---:|---:|---:|
| Kendi talepleri | Evet | Evet | Hayır |
| Departman talepleri | Hayır | Authoritative managed department | Hayır |
| Manager approval | Hayır | Evet; self hariç | Hayır |
| Admin approval | Hayır | Hayır | Evet |
| Tüm talepler + soft delete | Hayır | Hayır | Evet |
| Users/Departments/Audit | Hayır | Hayır | Evet |
| Profil/parola | Evet | Evet | Evet |
