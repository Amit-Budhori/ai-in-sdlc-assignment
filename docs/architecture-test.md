# Architecture Test

## What it tests

**Rule**: `InvoiceApp.Domain` must not depend on `Microsoft.AspNetCore` or on `InvoiceApp.Api`.

This encodes the core structural constraint from `AGENTS.md`:
> Business rules go in the Domain project, never in controllers.

If a developer adds an `[ApiController]` attribute, an `IActionResult` return type, or any
other web-framework type to the Domain project, the test fails immediately. The Domain assembly
stays independently testable — no web host, no DI container, no HTTP context needed.

A third heuristic test flags controllers that have grown too many public methods, which is an early
sign that business logic has leaked out of the Domain.

## Location

`backend/tests/InvoiceApp.Tests/ArchitectureTests.cs`

## Command to run

```bash
dotnet test backend/InvoiceApp.sln --filter "FullyQualifiedName~ArchitectureTests"
```

To run all tests including this one:

```bash
dotnet test backend/InvoiceApp.sln
```

## Expected output (passing)

```
Test run for InvoiceApp.Tests.dll
Passed  InvoiceApp.Tests.ArchitectureTests.Domain_Assembly_ContainsTypes_SanityCheck
Passed  InvoiceApp.Tests.ArchitectureTests.Domain_ShouldNot_DependOnAspNetCore
Passed  InvoiceApp.Tests.ArchitectureTests.Domain_ShouldNot_DependOnInfrastructureOrApi
Passed  InvoiceApp.Tests.ArchitectureTests.Api_Controllers_ShouldNot_ContainBusinessLogic_Heuristic

Test Run Successful.
Total tests: 4 architecture (+ 7 existing) = 11
```

Verified on 2026-10-31: `dotnet test backend/InvoiceApp.sln` → 11/11 passing.
Two issues were found and fixed during verification: (1) `Program` is implicit under
top-level statements, so `InMemoryInvoiceRepository` is used as the Api-assembly
anchor type; (2) the controller heuristic originally matched `record` types nested
in the Controllers namespace — fixed by filtering to `ControllerBase` subclasses.
The sanity-check test guards against the rule passing vacuously on an empty type set.

## Why NetArchTest

NetArchTest.Rules loads compiled assemblies and inspects their IL-level dependency graphs.
It catches violations that grep-based checks miss (e.g. a type added by a transitive reference,
or a `using` added in a nested file that wasn't searched). It integrates with xUnit so it runs
in the same `dotnet test` invocation as the unit tests and fails CI on the same pass.

## What this does not cover

- It does not prevent a controller from duplicating a business rule in an inline lambda — that
  requires code review.
- It does not check that every endpoint has a test (measured separately via coverage).
- When the outbox worker and PayEx client are added (target state), new rules should be added
  to verify that the worker does not import web-specific namespaces and that the PayEx client
  lives in an `Infrastructure` project, not in `Domain`.
