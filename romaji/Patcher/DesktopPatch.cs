using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DesktopPatch
{
    public static void Apply(string original, string helper, string output)
    {
        Contract.Require(original, "ui");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(original)));
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(helper)));
        using var app = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { AssemblyResolver = resolver });
        using var addon = AssemblyDefinition.ReadAssembly(helper);
        var window = app.MainModule.GetType("VRCLS.UI.Views.MainWindow");
        var constructor = window.Methods.Single(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 1);
        var hook = app.MainModule.ImportReference(addon.MainModule.GetType("VRCLS.Romaji.DesktopRomaji").Methods.Single(m => m.Name == "Attach"));
        var il = constructor.Body.GetILProcessor();
        var ret = constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret);
        ret.OpCode = OpCodes.Ldarg_0;
        var arg = Instruction.Create(OpCodes.Ldarg_1);
        var call = Instruction.Create(OpCodes.Call, hook);
        il.InsertAfter(ret, arg);
        il.InsertAfter(arg, call);
        il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
        var expected = MicOriginalPatch.PatchUi(app.MainModule, addon).Concat(new[] { constructor }).Select(m => m.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        app.Write(output);
        using var verified = AssemblyDefinition.ReadAssembly(output);
        using var untouched = AssemblyDefinition.ReadAssembly(original);
        IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
        string Body(MethodDefinition m) => !m.HasBody ? "" : string.Join("\n", m.Body.Instructions.Select(i => i.ToString()));
        var oldMethods = Types(untouched.MainModule.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var changed = Types(verified.MainModule.Types).SelectMany(t => t.Methods).Where(m => Body(m) != Body(oldMethods[m.FullName])).ToArray();
        if (!changed.Select(m => m.FullName).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(expected)) throw new InvalidOperationException("Unexpected UI changes.");
        Console.WriteLine($"Desktop and microphone routing hooks added; {oldMethods.Count - expected.Length} other UI method bodies unchanged.");
    }
}
