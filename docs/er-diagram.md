# ER Diyagramı

GitHub bu Mermaid bloğunu görsel ER diyagramı olarak render eder.

```mermaid
erDiagram
    USER {
        uniqueidentifier Id PK
        string FirstName
        string LastName
        string Email UK
        string PasswordHash
        int Role
        uniqueidentifier DepartmentId FK
        bool IsActive
        datetimeoffset CreatedDate
        bool IsDeleted
        datetimeoffset DeletedDate
        uniqueidentifier DeletedByUserId
    }
    DEPARTMENT {
        uniqueidentifier Id PK
        string Name UK
        uniqueidentifier ManagerId FK
        datetimeoffset CreatedDate
        bool IsDeleted
        datetimeoffset DeletedDate
        uniqueidentifier DeletedByUserId
    }
    CORPORATE_REQUEST {
        uniqueidentifier Id PK
        string Title
        string Description
        int RequestType
        decimal Amount
        int Status
        uniqueidentifier CreatedByUserId FK
        uniqueidentifier DepartmentId FK
        datetimeoffset CreatedDate
        datetimeoffset UpdatedDate
        datetimeoffset SubmittedDate
        datetimeoffset CompletedDate
        rowversion RowVersion
        bool IsDeleted
        datetimeoffset DeletedDate
        uniqueidentifier DeletedByUserId
    }
    REQUEST_APPROVAL {
        uniqueidentifier Id PK
        uniqueidentifier RequestId FK
        uniqueidentifier ApprovedByUserId FK
        int ApprovalLevel
        int Status
        string Description
        datetimeoffset ActionDate
    }
    REFRESH_TOKEN {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        string TokenHash UK
        datetimeoffset ExpiresAt
        datetimeoffset CreatedAt
        datetimeoffset RevokedAt
        uniqueidentifier ReplacedByTokenId FK
        string CreatedByIp
        string RevokedByIp
    }
    AUDIT_LOG {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        string EntityName
        uniqueidentifier EntityId
        string Action
        string OldValues
        string NewValues
        datetimeoffset CreatedDate
    }
    DEPARTMENT o|--o{ USER : contains
    USER o|--o{ DEPARTMENT : manages
    USER ||--o{ CORPORATE_REQUEST : creates
    DEPARTMENT ||--o{ CORPORATE_REQUEST : owns_snapshot
    CORPORATE_REQUEST ||--o{ REQUEST_APPROVAL : records
    USER ||--o{ REQUEST_APPROVAL : decides
    USER ||--o{ REFRESH_TOKEN : owns
    REFRESH_TOKEN o|--o{ REFRESH_TOKEN : replaces
    USER o|--o{ AUDIT_LOG : acts
```

User, Department ve CorporateRequest fiziksel silinmez; global query filter normal sorgularda `IsDeleted=true` kayıtları dışlar. RequestApproval ve AuditLog tarihçe olarak korunur. `CorporateRequest.RowVersion` SQL Server optimistic concurrency token’ıdır.
