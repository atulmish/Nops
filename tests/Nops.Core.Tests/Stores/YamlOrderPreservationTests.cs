using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Nops.Core.Tests.Stores;

public class YamlOrderPreservationTests
{
    private static readonly Assembly YamlAssembly = LoadYamlAssembly();

    private static Assembly LoadYamlAssembly()
    {
        try
        {
            return Assembly.Load("YamlDotNet");
        }
        catch
        {
            throw new Exception("YamlDotNet assembly not found");
        }
    }

    [Fact]
    public void YamlDotNet_YamlStreamAvailable()
    {
        var yamlStreamType = YamlAssembly.GetType("YamlDotNet.RepresentationModel.YamlStream");
        Assert.NotNull(yamlStreamType);
        Console.WriteLine($"YamlStream found: {yamlStreamType.FullName}");

        // Check Load method signature (use overload resolution)
        var loadMethods = yamlStreamType!.GetMethods()
            .Where(m => m.Name == "Load")
            .ToArray();

        Console.WriteLine($"  Load overloads ({loadMethods.Length}):");
        foreach (var m in loadMethods)
        {
            var @params = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"    - {m.ReturnType.Name} Load({@params})");
        }
    }

    [Fact]
    public void YamlDotNet_YamlMappingNodeHasOrder()
    {
        var mappingType = YamlAssembly.GetType("YamlDotNet.RepresentationModel.YamlMappingNode");
        Assert.NotNull(mappingType);
        Console.WriteLine($"YamlMappingNode found: {mappingType.FullName}");

        // Find members that suggest ordering (Keys, GetEnumerator, etc.)
        var members = mappingType!.GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.MemberType == MemberTypes.Property ||
                       m.MemberType == MemberTypes.Method)
            .ToArray();

        Console.WriteLine($"  Public members ({members.Length}):");
        foreach (var m in members.Take(15))
            Console.WriteLine($"    - {m.Name} ({m.MemberType})");

        // Look for indexer or key-related properties
        var keyProps = mappingType!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
                       p.Name.Contains("Entry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine($"  Key-related properties ({keyProps.Length}):");
        foreach (var p in keyProps)
            Console.WriteLine($"    - {p.Name} : {p.PropertyType.Name}");
    }

    [Fact]
    public void YamlDotNet_YamlSequenceNodeAvailable()
    {
        var seqType = YamlAssembly.GetType("YamlDotNet.RepresentationModel.YamlSequenceNode");
        Assert.NotNull(seqType);
        Console.WriteLine($"YamlSequenceNode found: {seqType.FullName}");

        // Check for Count/GetEnumerator to see if it's list-like
        var countProp = seqType!.GetProperty("Count");
        Console.WriteLine($"  Count property: {countProp?.PropertyType.Name ?? "none"}");
    }

    [Fact]
    public void YamlDotNet_YamlScalarNodeAvailable()
    {
        var scalarType = YamlAssembly.GetType("YamlDotNet.RepresentationModel.YamlScalarNode");
        Assert.NotNull(scalarType);
        Console.WriteLine($"YamlScalarNode found: {scalarType.FullName}");

        // Check for Value/Comment properties
        var valueProp = scalarType!.GetProperty("Value");
        var commentProp = scalarType.GetProperty("Comment");

        Console.WriteLine($"  Value property: {valueProp?.PropertyType.Name ?? "none"}");
        Console.WriteLine($"  Comment property: {commentProp?.PropertyType.Name ?? "none"}");
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== YAML ORDER PRESERVATION ===");
        Console.WriteLine("YamlStream + YamlMappingNode for ordered parsing");
        Console.WriteLine("YamlSequenceNode for arrays (list-like)");
        Console.WriteLine("YamlScalarNode.Value for scalar values");
        Console.WriteLine("Need runtime test to confirm order preservation");
        Console.WriteLine("===============================");

        Assert.True(true, "Spike complete - see console output");
    }
}
