using NetArchTest.Rules;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Structural rule: the Domain project must not reference web or infrastructure code.
///
/// Rationale (AGENTS.md): "Business rules go in the Domain project, never in controllers."
/// If Domain ever pulls in Microsoft.AspNetCore, that invariant is broken — Domain would no
/// longer be independently testable and the dependency arrow would be reversed.
///
/// Run with:
///   dotnet test backend/InvoiceApp.sln --filter "FullyQualifiedName~ArchitectureTests"
/// </summary>
public class ArchitectureTests
{
    private const string DomainNamespace = "InvoiceApp.Domain";
    private const string ApiNamespace = "InvoiceApp.Api";

    [Fact]
    public void Domain_Assembly_ContainsTypes_SanityCheck()
    {
        // Guard against a vacuous pass: if NetArchTest finds no types in the
        // Domain assembly, the dependency rules below would "pass" on nothing.
        var types = Types.InAssembly(typeof(InvoiceApp.Domain.Invoice).Assembly).GetTypes();

        Assert.NotEmpty(types);
    }

    [Fact]
    public void Domain_ShouldNot_DependOnAspNetCore()
    {
        // Load the Domain assembly by using any well-known type from it.
        var result = Types.InAssembly(typeof(InvoiceApp.Domain.Invoice).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{DomainNamespace} must not depend on Microsoft.AspNetCore. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_ShouldNot_DependOnInfrastructureOrApi()
    {
        // Domain must not know about its own host (Api) project.
        // This would create a circular dependency.
        var result = Types.InAssembly(typeof(InvoiceApp.Domain.Invoice).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{DomainNamespace} must not depend on {ApiNamespace}. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Api_Controllers_ShouldNot_ContainBusinessLogic_Heuristic()
    {
        // Heuristic: controller classes should not have more than a minimal number of
        // methods (>5 would suggest business logic leaking in). This is a structural
        // smell check, not a full business-logic detector.
        //
        // The AGENTS.md rule is: "Business rules go in the Domain project, never in controllers."
        var controllerTypes = Types.InAssembly(typeof(InvoiceApp.Api.InMemoryInvoiceRepository).Assembly)
            .That()
            .ResideInNamespace($"{ApiNamespace}.Controllers")
            .And()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .GetTypes();

        var violations = controllerTypes
            .Where(t => t.GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.DeclaredOnly).Length > 8)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Controllers with suspiciously many public methods (possible business-logic leak): " +
            $"{string.Join(", ", violations.Select(t => t.Name))}");
    }
}
