# ESSA Case Compliance Matrix

Bu belge resmî `ESSA_Official_Case_Text.md` aktarımı ve authoritative PDF gereksinimleriyle karşılaştırılmış tek canonical current-state matristir. Tarihî `MISSING/PARTIAL` durumları current-state tabloya karıştırılmaz. `COMPLETE`, aşağıdaki somut kod ve otomasyon kanıtını ifade eder; gerçek cihazlarda manuel responsive browser kontrolü ayrıca README’de belirtilir.

| ID | Resmî gereksinim | Durum | Kod kanıtı | Test kanıtı | API / Web kanıtı | Doküman |
|---|---|---|---|---|---|---|
| CASE-001 | .NET 8 Web API + SQL Server | COMPLETE | Api/Infrastructure | SQL Server suite | Swagger | README |
| CASE-002 | Clean Architecture | COMPLETE | Domain/Application/Infrastructure/Api/Web | architecture tests | Web HTTP-only | architecture.md |
| CASE-003 | CQRS + MediatR + ince controller | COMPLETE | feature handlers, ISender controllers | architecture tests | controller routes | architecture.md |
| CASE-004 | Employee/Manager/Admin | COMPLETE | UserRole, exact policies | authorization HTTP matrix | Bearer/policies | README |
| CASE-005 | Owner/departman/durum authorization | COMPLETE | request/approval guards | ownership/scope/state tests | güvenli 404/409 | README |
| CASE-006 | User asgari modeli | COMPLETE | User + EF config | mapping/entity tests | DTO projection | er-diagram.md |
| CASE-007 | Department asgari modeli | COMPLETE | Department + EF config | mapping/entity tests | DTO projection | er-diagram.md |
| CASE-008 | Request + RowVersion modeli | COMPLETE | CorporateRequest + EF config | gerçek SQL concurrency | Base64 RowVersion | er-diagram.md |
| CASE-009 | RequestApproval modeli | COMPLETE | entity/config | approval persistence/history | history endpoints/timeline | er-diagram.md |
| CASE-010 | RefreshToken modeli/rotation/revoke | COMPLETE | auth services/repository | rotation/reuse/revoke tests | auth endpoints/BFF | README |
| CASE-011 | AuditLog modeli | COMPLETE | entity/repository/read projection | audit API tests | list/detail UI | er-diagram.md |
| CASE-012 | User/Department/Request soft delete | COMPLETE | domain commands/global filters | SQL soft-delete tests | Admin delete flows | README |
| CASE-013 | Global query filters | COMPLETE | ApplicationDbContext | filter tests | normal query exclusion | architecture.md |
| CASE-014 | Server-derived request owner/dept/status | COMPLETE | create handler | spoofing tests | POST requests | requests.http |
| CASE-015 | Request CRUD/lifecycle endpoints | COMPLETE | request feature slices | lifecycle API tests | API + Web | requests.http |
| CASE-016 | Expense/Equipment positive amount | COMPLETE | validators/domain | validation tests | ProblemDetails | README |
| CASE-017 | Other nullable/positive amount | COMPLETE | validators/domain | validation tests | request contract | README |
| CASE-018 | Durum geçişleri merkezi | COMPLETE | aggregate behavior | transition tests | mutation endpoints | README |
| CASE-019 | Manager self-approval yasağı | COMPLETE | approval handler/domain | negative API tests | 409 | README |
| CASE-020 | 10.000 TL approval akışı | COMPLETE | configured threshold | lower/higher tests | manager/admin routes | approvals.http |
| CASE-021 | Reject reason zorunlu | COMPLETE | validator/domain | negative tests | 400 | approvals.http |
| CASE-022 | Admin self-service devralmaz | COMPLETE | Requests.SelfService exact policy | 403 tests | role-aware menu | README |
| CASE-023 | JWT access token | COMPLETE | JWT service | auth tests | Bearer Swagger | README |
| CASE-024 | Refresh token yalnız hash saklanır | COMPLETE | SHA-256 service | persistence/security tests | raw yalnız auth response | README |
| CASE-025 | Rotation/revoke/reuse | COMPLETE | auth commands | concurrency/reuse tests | auth routes | auth.http |
| CASE-026 | Inactive/deleted auth reddi | COMPLETE | DB-backed validation | negative tests | 401 | README |
| CASE-027 | Rate limiting + 429 | COMPLETE | built-in policies | 429 tests | ProblemDetails | README |
| CASE-028 | Merkezi validation | COMPLETE | FluentValidation pipeline | validator/API tests | 400 validation.failed | README |
| CASE-029 | Ayrık güvenli hata kategorileri | COMPLETE | global handler | ProblemDetails tests | 400/401/403/404/409/429 | README |
| CASE-030 | Paging/page-size | COMPLETE | PageRequest/read services | boundary tests | query contracts | README |
| CASE-031 | Status/type/date filters | COMPLETE | SQL criteria | query/API tests | Admin/Manager filters | API docs |
| CASE-032 | Yetkili DepartmentId filter | COMPLETE | Admin SQL criteria | Phase7 API test | admin requests | admin-requests.http |
| CASE-033 | Positive min/max filters | COMPLETE | shared semantics validators | unit/HTTP tests | Admin/Manager API | API docs |
| CASE-034 | Controlled title/description search | COMPLETE | normalized SQL search | search tests | list endpoints | README |
| CASE-035 | CreatedDate/Amount sorting | COMPLETE | allow-list ordering + Id tie | query tests | query contract | README |
| CASE-036 | Deterministic DB paging/projection | COMPLETE | AsNoTracking projection | repository tests | DTO responses | architecture.md |
| CASE-037 | Admin Users CRUD | COMPLETE | `AdministrationHandlers.cs` içindeki use-case handler’ları | `Phase7MutationAuthorizationTests.Every_phase7_mutation_should_enforce_exact_actor_matrix_over_real_http`; `Phase7AdminSelfProtectionTests.Current_admin_cannot_deactivate_demote_or_delete_self_and_failures_have_no_side_effects`; `Phase7AdminSelfProtectionTests.Self_protection_should_allow_updating_another_employee_and_persist_state_and_audit`; `Phase7ApiIntegrationTests.Admin_user_department_and_password_lifecycle_should_hash_revoke_audit_and_soft_delete` | `/api/admin/users*`; `Phase7WebSecurityAndErrorMappingTests.Backend_401_should_clear_real_web_session_and_old_cookies_should_not_authenticate_again`; `.Department_delete_failure_should_map_safely_without_fake_success`; `.Admin_request_soft_delete_failure_should_map_safely_without_fake_success` | `admin-users.http` |
| CASE-038 | Admin password reset | COMPLETE | confirmation validator/hash/revoke/audit | mismatch/success tests | API + Web | admin-users.http |
| CASE-039 | Department CRUD/manager assignment | COMPLETE | `Create/Update/Assign/DeleteDepartmentHandler` + focused manager guard | `Phase7InvalidManagerAssignmentTests.Manager_assignment_should_reject_every_invalid_actor_without_state_or_audit_and_allow_valid_replacement`; `Phase7ApiIntegrationTests.Active_manager_beyond_first_hundred_departments_should_not_be_demoted_moved_deactivated_or_deleted` | `/api/admin/departments*`; ayrı Web actions | `admin-departments.http` |
| CASE-040 | Admin all active requests/detail | COMPLETE | global read projection | filters/history tests | API + Web | admin-requests.http |
| CASE-041 | Request soft delete/audit/RowVersion | COMPLETE | `DeleteCorporateRequestHandler` transaction/UoW | `Phase7AdminRequestConcurrencyTests.Admin_soft_delete_with_stale_rowversion_should_preserve_non_empty_approval_history_then_accept_fresh_version`: iki approval snapshot’ı, stale 409, fresh soft delete, fiziksel request/approval history koruması ve tek audit; `Phase7ApiIntegrationTests.Admin_and_manager_request_lists_should_apply_scope_amount_filter_and_soft_delete` | `DELETE /api/admin/requests/{id}`; `Phase7WebSecurityAndErrorMappingTests.Admin_request_soft_delete_failure_should_map_safely_without_fake_success` | README |
| CASE-042 | Manager department all-status requests | COMPLETE | ManagerId SQL scope | role/scope/history tests | API + Web | manager-requests.http |
| CASE-043 | Audit list/detail/filter/redaction | COMPLETE | fail-closed allow-list projection | `Phase7ApiIntegrationTests.Audit_projection_should_fail_closed_for_unknown_nested_sensitive_and_malformed_payloads`; `Phase7ValidationTests.Audit_date_order_and_paging_should_fail_validation`; `Phase7WebEncodingTests.Approval_and_audit_details_should_html_encode_user_controlled_text_in_rendered_responses` | `/api/audit-logs*`; Admin encoded Razor detail | `audit-logs.http` |
| CASE-044 | Kritik request audit actions | COMPLETE | request handlers | audit tests | audit query | README |
| CASE-045 | User/Department/password audit | COMPLETE | transactional handlers | lifecycle tests | Admin audit | README |
| CASE-046 | Audit hassas veri içermez | COMPLETE | JSON allow-list/fallback | nested/malformed/secret tests | encoded Razor | README |
| CASE-047 | Atomic request/approval/audit | COMPLETE | transaction/UoW | rollback/concurrency tests | mutation APIs | architecture.md |
| CASE-048 | Optimistic concurrency → 409 | COMPLETE | RowVersion guards/handler | stale/race tests | safe UI feedback | README |
| CASE-049 | Yazılım + Finans seed | COMPLETE | Development seed | idempotency/clean E2E | initializer | README |
| CASE-050 | company.com üç rol seed | COMPLETE | deterministic users | login/clean E2E | auth API | README |
| CASE-051 | Seed transactional/idempotent/dev-only | COMPLETE | seeder/initializer | rollback/reconcile/E2E | explicit flag | README |
| CASE-052 | En az 8 anlamlı test | COMPLETE | unit/integration projects | full suite | HTTP/Web/SQL | README |
| CASE-053 | Swagger/OpenAPI/Bearer | COMPLETE | Swagger global Bearer + anonymous operation override | `Phase7OpenApiContractTests.Generated_openapi_should_expose_phase7_routes_security_and_forbid_spoof_or_sensitive_request_fields`: operation requestBody `$ref` çözümü; `passwordHash`, `isDeleted`, `deletedDate`, `createdDate`, `accessToken`, `refreshToken`, `status`, `createdByUserId`, `departmentId` yasak alan assertion’ları | generated `/swagger/v1/swagger.json` route/method/Bearer/request-schema doğrulaması | README; `ApplicationDbContextFactory` nedeniyle Infrastructure startup kullanan EF 8.0.29 drift komutu: `No changes have been made to the model since the last migration.` |
| CASE-054 | Postman veya .http | COMPLETE | `docs/api/*.http` | current controller/OpenAPI contract review | `https://localhost:7001`, company.com Development seed, gerçek route/enum ve secret içermeyen placeholder'lar | README |
| CASE-055 | ER diyagramı | COMPLETE | gerçek entity/EF alanları; `ApprovedByUserId`, approval `Status`, refresh-token IP ve replacement ilişkisi | entity/configuration comparison | N/A | er-diagram.md |
| CASE-056 | README zorunlu içerik | COMPLETE | N/A | command evidence | startup/security/smoke | README |
| CASE-057 | Clean DB migration-seed-workflow | COMPLETE | isolated GUID DB | `Phase7CleanDatabaseE2ETests` | gerçek HTTP | README |
| CASE-058 | Migration + drift yok | COMPLETE | InitialCreate/snapshot + Infrastructure design-time factory/copy-local tool output | canonical `has-pending-model-changes` | N/A | README |
| CASE-059 | Stack trace/SQL/secret sızmaz | COMPLETE | global handler + central gateway safe mapping | `Phase7TransportResilienceTests` connection/premature/timeout/cancellation/recovery; security tests/scan | 503/504 generic errors | README |
| CASE-060 | Fiziksel delete yok/history korunur | COMPLETE | soft-delete commands | SQL history tests | filtered endpoints | README |
| CASE-061 | Teslim checklist/traceability | COMPLETE | repository delivery | build/test/drift gates | Swagger/.http/Web | bu matris |
| CASE-062 | Web transport resilience | COMPLETE | `BackendApiGateway` merkezi 503/504 sınıflandırması; production generic error route | `Phase7TransportResilienceTests` | safe read/mutation mapping; caller cancellation korunur | README |

## Proje Kararları ve Opsiyonel Web

- Web yalnız HTTP üzerinden API tüketir; backend project reference içermez.
- Admin, Employee veya Manager capability’sini rol hiyerarşisiyle devralmaz.
- Manager dependency kontrolü pagination kullanmaz; `IDepartmentRepository.IsActiveDepartmentManagerAsync` bütün aktif departmanlarda SQL `AnyAsync` çalıştırır.
- Web opsiyoneldir ancak eklenen management UI antiforgery, BFF token isolation, approval timeline ve güvenli mutation feedback sağlar.
- Gerçek cihazlarda manuel responsive browser smoke otomatik compliance kanıtı değildir ve README’de açıkça belirtilir.
