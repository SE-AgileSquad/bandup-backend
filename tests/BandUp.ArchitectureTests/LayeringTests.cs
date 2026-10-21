using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Shouldly;
using NetArchTestResult = NetArchTest.Rules.TestResult;

namespace BandUp.ArchitectureTests;

public sealed class LayeringTests
{
    private const string RepositoryRootMarker = "BandUp.sln";

    private static readonly string[] ForbiddenFrameworkPackages =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "StackExchange.Redis",
        "RabbitMQ.Client",
    ];

    private static readonly Dictionary<string, string[]> LayeringRules = new(StringComparer.Ordinal)
    {
        ["BandUp.Domain"] = [],
        ["BandUp.Contracts"] = [],
        ["BandUp.Application"] = ["BandUp.Domain"],
        ["BandUp.Infrastructure"] = ["BandUp.Application", "BandUp.Contracts", "BandUp.Domain"],
        ["BandUp.Api"] = ["BandUp.Application", "BandUp.Contracts", "BandUp.Infrastructure"],
        ["BandUp.Worker"] = ["BandUp.Application", "BandUp.Contracts", "BandUp.Infrastructure"],
    };

    [Fact]
    public void INF_08_Domain_References_No_Other_Project()
    {
        BandUpReferencesOf("BandUp.Domain").ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Contracts_References_No_Other_Project()
    {
        BandUpReferencesOf("BandUp.Contracts").ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Application_References_No_Project_Other_Than_Domain()
    {
        BandUpReferencesOf("BandUp.Application")
            .Except(["BandUp.Domain"], StringComparer.Ordinal)
            .ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Infrastructure_Does_Not_Reference_Api_Or_Worker()
    {
        string[] referenced = BandUpReferencesOf("BandUp.Infrastructure");

        referenced.ShouldNotContain("BandUp.Api");
        referenced.ShouldNotContain("BandUp.Worker");
    }

    [Fact]
    public void INF_08_Api_And_Worker_Do_Not_Reference_Each_Other()
    {
        BandUpReferencesOf("BandUp.Api").ShouldNotContain("BandUp.Worker");
        BandUpReferencesOf("BandUp.Worker").ShouldNotContain("BandUp.Api");
    }

    [Fact]
    public void INF_08_Domain_References_No_Framework_Assemblies()
    {
        string[] violations = AssemblyReferencesOf("BandUp.Domain")
            .Where(IsForbiddenFrameworkAssembly)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Domain_References_No_Dependency_Injection_Assemblies()
    {
        string[] violations = AssemblyReferencesOf("BandUp.Domain")
            .Where(name => name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal))
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Application_References_No_Framework_Assemblies()
    {
        string[] violations = AssemblyReferencesOf("BandUp.Application")
            .Where(IsForbiddenFrameworkAssembly)
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void INF_08_Domain_Types_Do_Not_Depend_On_Framework_Types()
    {
        NetArchTestResult result = Types.InAssembly(LoadAssembly("BandUp.Domain"))
            .Should()
            .NotHaveDependencyOnAny(ForbiddenFrameworkPackages)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DescribeFailures(result));
    }

    [Fact]
    public void INF_08_Application_Types_Do_Not_Depend_On_Framework_Types()
    {
        NetArchTestResult result = Types.InAssembly(LoadAssembly("BandUp.Application"))
            .Should()
            .NotHaveDependencyOnAny(ForbiddenFrameworkPackages)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DescribeFailures(result));
    }

    [Fact]
    public void INF_08_Project_Files_Declare_Exactly_The_Layering_References()
    {
        string root = FindRepositoryRoot();
        List<string> violations = [];

        foreach ((string project, string[] allowed) in LayeringRules)
        {
            string[] declared = DeclaredProjectReferences(Path.Combine(root, "src", project, $"{project}.csproj"));

            violations.AddRange(declared.Except(allowed, StringComparer.Ordinal).Select(extra => $"{project} -> {extra}"));
            violations.AddRange(allowed.Except(declared, StringComparer.Ordinal).Select(missing => $"{project} is missing -> {missing}"));
        }

        violations.ShouldBeEmpty();
    }

    private static bool IsForbiddenFrameworkAssembly(string name) =>
        ForbiddenFrameworkPackages.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static string[] BandUpReferencesOf(string assemblyName) =>
        AssemblyReferencesOf(assemblyName)
            .Where(name => name.StartsWith("BandUp.", StringComparison.Ordinal))
            .ToArray();

    private static string[] AssemblyReferencesOf(string assemblyName) =>
        LoadAssembly(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    private static Assembly LoadAssembly(string assemblyName) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll"));

    private static string[] DeclaredProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include))
            .Where(name => name.Length > 0)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryRootMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate '{RepositoryRootMarker}' above '{AppContext.BaseDirectory}'.");
    }

    private static string DescribeFailures(NetArchTestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Types with forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
