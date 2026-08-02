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
- SQL Server 2019 veya sonrası; Windows geliştirme ortamında SQL Server Express LocalDB kullanılabilir

Kurulumu doğrulamak için:

```powershell
dotnet --info
sqllocaldb info MSSQLLocalDB
dotnet tool restore
```

## Restore, build ve test

Repository kökünde:

```powershell
dotnet restore .\CorporateRequestManagement.sln
dotnet build .\CorporateRequestManagement.sln --configuration Release --no-restore
dotnet test .\CorporateRequestManagement.sln --configuration Release --no-build
```

Entegrasyon testleri her koşuda `CorporateRequestManagement_IntegrationTests_` önekli benzersiz bir gerçek SQL Server veritabanı oluşturur, migration uygular ve yalnızca kendi oluşturduğu veritabanını siler. LocalDB dışındaki bir SQL Server için bağlantı şu ortam değişkeniyle verilebilir:

```powershell
$env:CRM_TEST_SQLSERVER_CONNECTION = "Server=localhost;Integrated Security=true;TrustServerCertificate=true"
```

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
