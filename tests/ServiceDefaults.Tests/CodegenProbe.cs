using System.Reflection;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>Temporary probe — deleted after use.</summary>
public class CodegenProbe
{
    [Fact]
    public void Print_codegen_surface()
    {
        List<string> lines = [];

        PropertyInfo codegen = typeof(Wolverine.WolverineOptions)
            .GetProperties().First(p => p.Name == "CodeGeneration");
        lines.Add($"WolverineOptions.CodeGeneration : {codegen.PropertyType.FullName}");

        foreach (PropertyInfo property in codegen.PropertyType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            lines.Add($"  .{property.Name} : {property.PropertyType.Name}");
        }

        Type? mode = codegen.PropertyType.Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "TypeLoadMode");
        if (mode is not null)
        {
            lines.Add($"TypeLoadMode values: {string.Join(", ", Enum.GetNames(mode))}");
        }

        throw new Xunit.Sdk.XunitException(string.Join("\n", lines));
    }
}
