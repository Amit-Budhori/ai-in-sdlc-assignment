# As-Built Diagrams

These diagrams describe the Invoice Approval service **as the code stands today**,
not the planned production deployment described in `ops/README.md`.

---

## Container View (C4 Level 2)

```mermaid
C4Container
    title Invoice Approval — as-built container view

    Person(approver, "Approver / Admin", "Internal staff member who reviews invoices")
    Person_Ext(admin, "Admin", "Internal staff with delete rights")

    System_Boundary(invoice_system, "Invoice Approval Service") {
        Container(web_ui, "Web UI", "Static HTML/JS/CSS", "Single-page app served from wwwroot; signs in users, lists and submits invoices, triggers approve/reject")
        Container(api, "ASP.NET Core 8 API", ".NET 8 / C#", "Handles sign-in, invoice CRUD and approval decisions. Enforces role-based access via SessionAuthMiddleware")
        ContainerDb(in_memory, "In-Memory Store", "ConcurrentDictionary (in-process)", "Holds all invoice state for the lifetime of the process; wiped on restart")
    }

    System_Ext(github_actions, "GitHub Actions", "CI pipeline; builds, tests and scans on every PR and push to main")

    Rel(approver, web_ui, "Opens browser", "HTTP")
    Rel(web_ui, api, "REST calls", "HTTP/JSON, Bearer token")
    Rel(api, in_memory, "Read / write invoices", "In-process method call")
    Rel(github_actions, api, "Build + test + scan", "dotnet build / dotnet test")
```

### Citation list

| Element | Source file / symbol |
|---------|---------------------|
| Web UI container | `backend/src/InvoiceApp.Api/wwwroot/` (index.html, app.js, styles.css) |
| ASP.NET Core API | `backend/src/InvoiceApp.Api/Program.cs` — `var app = builder.Build()` |
| SessionAuthMiddleware | `backend/src/InvoiceApp.Api/Auth/SessionAuthMiddleware.cs` — `InvokeAsync` |
| Role enforcement | `backend/src/InvoiceApp.Api/Auth/RequireRoleAttribute.cs` |
| In-Memory Store | `backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs` — `private readonly List<Invoice> _invoices` |
| GitHub Actions CI | `.github/workflows/ci.yml` |
| `IInvoiceRepository` interface | `backend/src/InvoiceApp.Domain/IInvoiceRepository.cs` |
| `ApprovalService` | `backend/src/InvoiceApp.Domain/ApprovalService.cs` |
| Singleton registration | `Program.cs` line `builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>()` |

### Inferred items (not explicit in code, reasoned from context)

| Item | What was inferred and why |
|------|--------------------------|
| "Browser" as the only client | No mobile SDK, CLI client, or machine-to-machine caller is present. The UI is `index.html` served from `wwwroot`, so the only client type is a browser. |
| HTTP (not HTTPS) for local dev | `launchSettings.json` exposes only port 5000 without TLS; no `UseHttpsRedirection` in `Program.cs`. HTTPS is left to the host in production. |
| No message broker or external system | `ops/README.md` says Azure Service Bus and PayEx are **planned**, not present. No `HttpClient` or Service Bus SDK reference appears in any `.csproj`. |
| Lock-based concurrency in InMemoryInvoiceRepository | The actual `InMemoryInvoiceRepository` in the repository uses a `private readonly object _lock` with `lock(_lock)` blocks rather than `ConcurrentDictionary`. |

---

## Approval Path Sequence Diagram

```mermaid
sequenceDiagram
    autonumber
    actor User as Approver (browser)
    participant UI as Web UI<br/>(wwwroot/app.js)
    participant MW as SessionAuthMiddleware<br/>(Auth/SessionAuthMiddleware.cs)
    participant AC as ApprovalsController<br/>(Controllers/ApprovalsController.cs)
    participant SVC as ApprovalService<br/>(Domain/ApprovalService.cs)
    participant REPO as InMemoryInvoiceRepository<br/>(InMemoryInvoiceRepository.cs)

    User->>UI: Click "Approve" on an invoice
    UI->>MW: POST /api/approvals/{invoiceId}/approve<br/>Authorization: Bearer {token}
    MW->>MW: Validate token via SessionStore.Get(token)
    alt token invalid
        MW-->>UI: 401 Unauthorized
    else token valid
        MW->>AC: Forward request with UserSession in HttpContext.Items
        AC->>SVC: Approve(invoiceId, session.Username)
        SVC->>REPO: GetById(invoiceId)
        REPO-->>SVC: Invoice (or null)
        alt invoice not found
            SVC-->>AC: throws KeyNotFoundException
            AC-->>UI: 404 Not Found
        else invoice not in PendingApproval
            SVC-->>AC: throws InvalidOperationException
            AC-->>UI: 409 Conflict
        else invoice is PendingApproval
            SVC->>SVC: Set Status=Approved, DecidedBy, DecidedAt
            SVC->>REPO: Update(invoice)
            REPO-->>SVC: (void)
            SVC-->>AC: Updated Invoice
            AC-->>UI: 200 OK { invoice JSON }
            UI-->>User: Invoice shows "Approved"
        end
    end
```

### Citation list (sequence diagram)

| Step | Source |
|------|--------|
| Bearer token validation | `SessionAuthMiddleware.InvokeAsync` — checks `Authorization` header, calls `_sessions.Get(token)` |
| 401 path | `SessionAuthMiddleware.cs` lines `context.Response.StatusCode = 401; return;` |
| Session forwarded | `context.Items[HttpContextExtensions.SessionKey] = session` |
| `[RequireRole]` on controller | `ApprovalsController.cs` — class-level `[RequireRole("Approver", "Admin")]` |
| `Approve(invoiceId, username)` call | `ApprovalsController.Approve` — `_approvals.Approve(invoiceId, HttpContext.GetSession().Username)` |
| `EnsurePending` guard | `ApprovalService.EnsurePending` — throws `InvalidOperationException` if not `PendingApproval` |
| `KeyNotFoundException` | `ApprovalService.Find` — `?? throw new KeyNotFoundException(...)` |
| Error mapping | `ApiControllerBase.Handle` — catches `KeyNotFoundException→404`, `InvalidOperationException→409`, `ArgumentException→400` |
| Repository update | `InMemoryInvoiceRepository.Update` — `lock (_lock) { _invoices[index] = invoice; }` |
