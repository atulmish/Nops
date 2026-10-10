using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Nops.Core.Tests.Crypto;

public class BouncyCastleApiDiscoveryTests
{
    private static readonly Assembly BcAssembly = LoadBcAssembly();

    private static Assembly LoadBcAssembly()
    {
        // Find BouncyCastle.Cryptography assembly by searching loaded assemblies
        var bcAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.Contains("BouncyCastle", StringComparison.OrdinalIgnoreCase) == true);

        foreach (var a in bcAssemblies)
            Console.WriteLine($"Loaded: {a.GetName().Name}");

        // Try to load it explicitly if not already loaded
        try
        {
            return Assembly.Load("BouncyCastle.Cryptography");
        }
        catch
        {
            throw new Exception("BouncyCastle.Cryptography assembly not found");
        }
    }

    [Fact]
    public void BouncyCastle_AesGcmAvailable()
    {
        var gcmTypes = BcAssembly.GetTypes()
            .Where(t => t.Name.Contains("Gcm", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine($"Found {gcmTypes.Length} GCM-related types:");
        foreach (var t in gcmTypes)
        {
            Console.WriteLine($"  - {t.FullName}");

            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length > 0)
                Console.WriteLine($"    Ctor params: {string.Join(", ", ctors[0].GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}");
        }

        Assert.True(gcmTypes.Length > 0, "Expected GCM types in BouncyCastle");
    }

    [Fact]
    public void BouncyCastle_AesEngineAvailable()
    {
        var aesType = BcAssembly.GetType("Org.BouncyCastle.Crypto.Engines.AesEngine");

        Assert.NotNull(aesType);
        Console.WriteLine($"AesEngine found: {aesType?.FullName}");

        var blockSizeMethod = aesType!.GetMethod("GetBlockSize");
        Assert.NotNull(blockSizeMethod);
        Console.WriteLine($"  GetBlockSize returns: {blockSizeMethod.ReturnType.Name}");
    }

    [Fact]
    public void BouncyCastle_GcmBlockCipherAvailable()
    {
        var gcmType = BcAssembly.GetType("Org.BouncyCastle.Crypto.Modes.GcmBlockCipher");

        Assert.NotNull(gcmType);
        Console.WriteLine($"GcmBlockCipher found: {gcmType.FullName}");

        var initMethods = gcmType!.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Init" && m.GetParameters().Length >= 1)
            .ToArray();

        Console.WriteLine($"  Init overloads: {initMethods.Length}");
        foreach (var m in initMethods.Take(3))
        {
            var @params = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"    - {m.ReturnType.Name} Init({@params})");
        }

        var processMethods = gcmType!.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "ProcessBlock")
            .ToArray();

        Console.WriteLine($"  ProcessBlock overloads: {processMethods.Length}");
        foreach (var m in processMethods.Take(2))
        {
            var @params = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"    - {m.ReturnType.Name} ProcessBlock({@params})");
        }
    }

    [Fact]
    public void BouncyCastle_PgpTypesAvailable()
    {
        var assembly = BcAssembly;

        // Look for PGP-related types
        var pgpTypes = assembly.GetTypes()
            .Where(t => t.Name.StartsWith("Pgp", StringComparison.Ordinal))
            .ToArray();

        Console.WriteLine($"Found {pgpTypes.Length} Pgp types (showing first 10):");
        foreach (var t in pgpTypes.Take(10))
            Console.WriteLine($"  - {t.FullName}");

        // Look for armor-related types  
        var armorTypes = assembly.GetTypes()
            .Where(t => t.Name.Contains("Armored", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine($"Found {armorTypes.Length} Armored types:");
        foreach (var t in armorTypes.Take(5))
            Console.WriteLine($"  - {t.FullName}");

        Assert.True(pgpTypes.Length > 0, "Expected PGP types in BouncyCastle");
    }

    [Fact]
    public void Spike_Conclusion_DocumentFinding()
    {
        Console.WriteLine("=== BOUNCYCASTLE API DISCOVERY ===");
        Console.WriteLine("GcmBlockCipher: Org.BouncyCastle.Crypto.Modes.GcmBlockCipher");
        Console.WriteLine("AesEngine: Org.BouncyCastle.Crypto.Engines.AesEngine");
        Console.WriteLine("PGP types: Org.BouncyCastle.OpenPgp.* namespace");
        Console.WriteLine("==================================");

        Assert.True(true, "Spike complete - see console output");
    }
}
