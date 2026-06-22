using System;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

// Minimal decompiler so you can READ Big Ambitions' API.
//
//   dotnet run -- "<path>\Managed\BigAmbitions.dll"                 -> whole module (pipe to a file)
//   dotnet run -- "<path>\Managed\BigAmbitions.dll" GameManager     -> a single type
//   dotnet run -- "<path>\Managed\BigAmbitions.dll" A,B,C           -> several types (comma-separated)
//
// Tip: BigAmbitions.dll is ~212k lines decompiled. Dump it to a file and grep, e.g.
//   dotnet run -- "...\BigAmbitions.dll" > BA.cs
//   grep -n "RegisterModItem" BA.cs
//
// Single-type mode resolves only types whose references are reachable from the same folder; if a
// type fails to resolve, dump the whole module instead.

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: dotnet run -- <assembly.dll> [TypeName[,TypeName...]]");
            return 1;
        }

        string file = args[0];
        var settings = new DecompilerSettings(LanguageVersion.CSharp10_0);
        var decompiler = new CSharpDecompiler(file, settings);

        if (args.Length >= 2)
        {
            foreach (string typeName in args[1].Split(','))
            {
                var name = new FullTypeName(typeName.Trim());
                try { Console.WriteLine(decompiler.DecompileTypeAsString(name)); }
                catch (Exception e) { Console.WriteLine($"// FAILED {typeName}: {e.Message}"); }
                Console.WriteLine("\n// ======================================\n");
            }
        }
        else
        {
            Console.WriteLine(decompiler.DecompileWholeModuleAsString());
        }

        return 0;
    }
}
