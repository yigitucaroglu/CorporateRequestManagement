# Kurumsal Talep ve Onay Yönetim Sistemi

Çalışanların kurumsal talepler oluşturduğu ve yönetici/yönetim onay akışlarından geçirdiği .NET 8 örnek uygulamasıdır.

## Mimari

Backend Clean Architecture sınırlarını izler:

```text
Domain <- Application <- Infrastructure <- Api
```

Domain bağımsızdır; Application yalnızca Domain'e, Infrastructure Application ve Domain'e, Api ise Application ve Infrastructure'a referans verir. Ayrı MVC `Web` projesi backend projelerine referans vermez ve API ile yalnızca HTTP üzerinden iletişim kurar.

## Gereksinimler

- .NET 8 SDK
- SQL Server 2019 veya sonrası

Kurulumu doğrulamak için:

```powershell
dotnet --info
dotnet tool restore
```

## Restore, build ve test

Repository kökünde:

```powershell
dotnet restore .\CorporateRequestManagement.sln
dotnet build .\CorporateRequestManagement.sln --configuration Release --no-restore
dotnet test .\CorporateRequestManagement.sln --configuration Release --no-build
```

Entegrasyon testleri her koşuda `CorporateRequestManagement_IntegrationTests_` önekli benzersiz bir gerçek SQL Server veritabanı oluşturur, migration uygular ve yalnızca kendi oluşturduğu veritabanını siler. Test bağlantısının tek kaynağı `CRM_TEST_SQLSERVER_CONNECTION` ortam değişkenidir; değişken yoksa testler açıkça başarısız olur.

```powershell
$env:CRM_TEST_SQLSERVER_CONNECTION = "<güvenli-test-sql-server-bağlantısı>"
```

## Authentication kurulumu

JWT issuer `CorporateRequestManagement.Api`, audience `CorporateRequestManagement.Clients`, access token ömrü 15 dakika ve refresh token ömrü 7 gündür. Signing key repository dosyalarında tutulmaz; en az 32 byte Development anahtarı user-secrets veya environment üzerinden sağlanır:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<minimum-32-byte-development-key>" --project .\src\CorporateRequestManagement.Api
$env:Jwt__SigningKey = "<minimum-32-byte-development-key>"
```

Endpointler `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/revoke` ve yetkili `GET /api/auth/me` uçlarıdır. Raw refresh token yalnız istemciye döner; veritabanında yalnız SHA-256 özeti saklanır. Browser tarafında refresh token `localStorage` içinde tutulmamalıdır; Web taşıma kararı sonraki Auth UI diliminde uygulanacaktır.

Auth uçları IP başına dakikada 5, genel API ise authenticated kullanıcı veya IP başına dakikada 100 istekle sınırlıdır. Hatalar `code`, `traceId` ve validation için `errors` içeren ProblemDetails döndürür. Development Swagger arayüzünde Bearer access token ile Authorize kullanılabilir; örnekler `docs/api/auth.http` dosyasındadır.

## EF Core migration yönetimi

EF aracı repository-local manifest ile 8.0 sürümüne sabitlenmiştir:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext

dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext
```

Yeni migration ve design-time database update işlemleri de aynı Infrastructure factory standardını kullanır:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext `
  --output-dir Persistence\Migrations

dotnet tool run dotnet-ef database update `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext
```

Bu komutlar yalnız design-time migration yönetimi içindir. Runtime Development migration ve seed akışı aşağıdaki explicit API initializer komutuyla ayrı tutulur.

Development veritabanını migration ile oluşturup idempotent demo verisini eklemek için:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\CorporateRequestManagement.Api -- --initialize-database
```

Normal API başlangıcı migration veya seed çalıştırmaz. `--initialize-database` komutu Development dışındaki ortamlarda reddedilir. Bağlantı dizesi Development ayarında güvenli bir LocalDB varsayılanı içerir; diğer ortamlar `ConnectionStrings__DefaultConnection` sağlamalıdır.

Demo kullanıcıları:

| Rol | E-posta | Parola |
|---|---|---|
| Admin | `admin@crm.local` | `CrmDemo!2026` |
| Manager | `manager@crm.local` | `CrmDemo!2026` |
| Employee | `employee@crm.local` | `CrmDemo!2026` |

Parolalar veritabanında ASP.NET Core `PasswordHasher<User>` çıktısı olarak tutulur; düz metin parola saklanmaz. Bu kimlik bilgileri yalnızca Development içindir.

## Uygulamaları çalıştırma

```powershell
dotnet run --project .\src\CorporateRequestManagement.Api --urls http://localhost:5100
dotnet run --project .\src\CorporateRequestManagement.Web --urls http://localhost:5200
```

Phase 1C sonunda API henüz özellik endpoint'i sunmaz; root adresinin 404 dönmesi beklenir. Persistence katmanı SQL Server mapping, migration, repository/unit-of-work, transaction ve Development seed temellerini sağlar.

## Web authentication ve Metronic

Web uygulaması browser-for-frontend yaklaşımı kullanır: tarayıcı Authentication API'yi doğrudan çağırmaz. Access ve refresh token değerleri HTML, JavaScript, cookie ticket, `localStorage` veya `sessionStorage` içine yazılmaz; yalnızca Web sunucusundaki ASP.NET Core session token store içinde tutulur. Tarayıcıda güvenli, HttpOnly ve SameSite=Lax olan `__Host-Crm.Web.Auth` kimlik cookie'si ile opaque `__Host-Crm.Web.Session` session cookie'si bulunur. Form POST işlemleri antiforgery korumasındadır.

Web'in API adresi `BackendApi:BaseUrl` ile yapılandırılır. Development için API ve Web ayrı terminallerde şu sırayla başlatılabilir:

```powershell
dotnet run --project .\src\CorporateRequestManagement.Api --urls http://localhost:5100
dotnet run --project .\src\CorporateRequestManagement.Web --urls https://localhost:7240
```

Rotalar: `GET/POST /account/login`, yalnız POST olan `/account/logout` ve yetkili `/` dashboard. Dashboard isteğinde API 401 dönerse aynı opaque session kimliği için tek refresh çalıştırılır, dönen rotation tokenları atomik olarak session'a yazılır ve istek yalnız bir kez yinelenir.

Bu case implementasyonu tek node üzerinde memory-backed session kullanır. Çoklu instance production dağıtımında ortak bir distributed session store ve paylaşılan data-protection key ring gerekir.

Metronic kaynak paketi `.vendor/metronic` altında local-only tutulur. Repository'ye yalnız corporate login ve uygulama shell'i için gereken derlenmiş bundle, font ve görsel alt kümesi kopyalanır; demo sayfaları, kaynak arşivleri, `node_modules`, dokümantasyon ve lisans dosyaları kopyalanmaz. Metronic kaynak/sürüm ve lisans sahipliği proje tesliminde lisans sahibi tarafından doğrulanmalıdır.

## Employee request self-service API

Yetkili kullanıcılar `POST /api/requests`, `GET /api/requests`, `GET /api/requests/{id}`, `PUT /api/requests/{id}`, `POST /api/requests/{id}/submit` ve `POST /api/requests/{id}/cancel` uçlarıyla kendi taleplerini yönetir. Manager’ın kendi talebine ilişkin domain kuralı bulunduğundan sözleşme yeni bir Employee-only rol varsaymaz; authentication ve own-resource ownership zorunludur. Başka kullanıcıya ait veya bulunmayan talep aynı güvenli `404 request.not_found` sonucunu üretir.

`CreatedByUserId`, `DepartmentId` ve `Status` istemci body’sinden alınmaz. Owner authenticated kullanıcıdan, departman ise kullanıcının güncel departmanından türetilir ve talepte snapshot olarak saklanır. Listeleme paging, status/type/date filtreleri, whitelist sort alanları ve kararlı Id tie-breaker kullanır.

Mutation response’ları SQL Server RowVersion değerini 8-byte Base64 string olarak döndürür. Update, submit ve cancel son response’daki RowVersion’ı ister; malformed değer 400, stale değer `409 concurrency.conflict` üretir. Audit action’ları `CorporateRequest.Created`, `CorporateRequest.DraftUpdated`, `CorporateRequest.Submitted` ve `CorporateRequest.Cancelled` olup description, token veya RowVersion içermez. Örnek çağrılar [requests.http](docs/api/requests.http) dosyasındadır.

## Manager ve Admin approval API

Manager approval kapsamı kullanıcının departman alanından değil, authoritative `Department.ManagerId` ilişkisinden belirlenir. Manager queue/detail/decision uçları yalnız ilgili aktif departmanın `PendingManagerApproval` taleplerini kapsar ve manager’ın kendi taleplerini actionable queue dışında bırakır. Başka departmandaki veya bulunmayan talep aynı güvenli 404 sonucunu; self-action `409 approval.self_action_forbidden` sonucunu üretir. Admin, Manager rolünü devralmaz ve manager uçlarını kullanamaz.

Manager approve işleminde amount null veya yapılandırılmış `Approval:Threshold` değerine eşit/altındaysa talep final `Approved`; threshold üstündeyse `PendingAdminApproval` olur. Varsayılan threshold 10.000’dir. Manager reject final `Rejected` üretir. Admin yalnız `PendingAdminApproval` taleplerini global queue üzerinden approve/reject edebilir; iki karar da final durum ve `CompletedDate` üretir. Güvenli varsayılan olarak Admin self-action da yasaktır.

Her karar güncel 8-byte Base64 RowVersion ister. Stale değer `409 concurrency.conflict`, aynı seviyedeki tekrar karar `409 approval.already_processed` üretir. Request mutation, tekil `(RequestId, ApprovalLevel)` RequestApproval kaydı ve redakte audit kaydı tek `SaveChanges` atomiklik sınırındadır. Audit payload’ı rejection reason, token veya RowVersion taşımaz.

Approval history owner, authoritative department manager ve Admin tarafından görülebilir; diğer aktörlerde kaynak varlığını sızdırmayan 404 döner. Phase 6, bu kararlı HTTP sözleşmesinin Manager/Admin Metronic UI dilimini ekleyecektir. Örnekler [approvals.http](docs/api/approvals.http) dosyasındadır.
