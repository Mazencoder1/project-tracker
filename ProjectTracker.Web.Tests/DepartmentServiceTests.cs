using ProjectTracker.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ProjectTracker.Web.Tests;

public class DepartmentServiceTests
{
    private static DepartmentService Service(Dictionary<string, string?> values) =>
        new(new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Env()));

    [Fact]
    public void Without_settings_the_default_list_is_used()
    {
        var departments = Service([]).GetDepartments();

        Assert.NotEmpty(departments);
        Assert.All(departments, d => Assert.Equal(d.Id, d.Name));
    }

    [Fact]
    public void The_list_comes_from_appsettings_without_duplicates()
    {
        var departments = Service(new()
        {
            ["Departments:List:0"] = "المالية",
            ["Departments:List:1"] = "التقنية",
            ["Departments:List:2"] = "المالية",
        }).GetDepartments();

        Assert.Equal(["المالية", "التقنية"], departments.Select(d => d.Name));
    }

    [Fact]
    public void The_environment_variable_wins_over_the_file()
    {
        var departments = Service(new()
        {
            ["DEPARTMENTS"] = "أ, ب",
            ["Departments:List:0"] = "المالية",
        }).GetDepartments();

        Assert.Equal(["أ", "ب"], departments.Select(d => d.Name));
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
