using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MicOriginalPatch
{
    internal static MethodDefinition RecognitionLanguages(ModuleDefinition module) =>
        module.GetType("VRCLS.Services.Recognition.FastApiRecognitionService").Methods.Single(m => m.Name == "GetEffectiveLanguagesForRequest");

    internal static TypeDefinition MainViewModel(ModuleDefinition module) => module.GetType("VRCLS.UI.ViewModels.MainViewModel");

    internal static MethodDefinition RecognitionResult(ModuleDefinition module) =>
        MainViewModel(module).NestedTypes.Single(t => t.Name.StartsWith("<HandleRecognitionResultAsync>d__", StringComparison.Ordinal))
            .Methods.Single(m => m.Name == "MoveNext");

    private static MethodReference Hook(ModuleDefinition module, AssemblyDefinition addon, string name) =>
        module.ImportReference(addon.MainModule.GetType("VRCLS.Romaji.MicOriginal").Methods.Single(m => m.Name == name));

    internal static void PatchService(ModuleDefinition module, AssemblyDefinition addon)
    {
        var call = RecognitionLanguages(module).Body.Instructions.Single(i => i.Operand is MethodReference method &&
            method.Name == "ResolveEnabledTargetLanguages");
        call.OpCode = OpCodes.Call;
        call.Operand = Hook(module, addon, "RecognitionTargets");
    }

    internal static MethodDefinition[] PatchUi(ModuleDefinition module, AssemblyDefinition addon)
    {
        var mode = MainViewModel(module).Methods.Single(m => m.Name == "GetEffectiveMode");
        var il = mode.Body.GetILProcessor();
        var entry = mode.Body.Instructions[0];
        // The original "control" path skips translation, clears supplied translations and
        // keeps the recognized words in the original slot. Command handling is independent.
        il.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_1));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Call, Hook(module, addon, "Applies")));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Brfalse, entry));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Ldstr, "control"));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Ret));

        var result = RecognitionResult(module);
        var state = result.DeclaringType;
        var currentMode = state.Fields.Single(f => f.Name.StartsWith("<currentMode>", StringComparison.Ordinal));
        var original = state.Fields.Single(f => f.Name.StartsWith("<original>", StringComparison.Ordinal));
        var instructions = result.Body.Instructions;
        var shutdownCheck = instructions.Single(i => i.Operand is MethodReference m && m.Name == "get_OscShutdown");
        var formatCall = instructions.Single(i => i.Operand is MethodReference m && m.Name == "FormatOscMixedChatboxMessage");
        var formatting = shutdownCheck.Next.Next;
        var storeMessage = formatCall.Next;
        if (shutdownCheck.Next.OpCode.FlowControl != FlowControl.Cond_Branch || storeMessage.OpCode.Code != Code.Stloc_S)
            throw new InvalidOperationException("Unexpected chatbox formatting block.");

        // New instructions lengthen jumps over the entire formatting/sending block.
        // Widen only branches whose distance could grow across the insertion.
        foreach (var branch in instructions.Where(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
        {
            var destination = (Instruction)branch.Operand;
            if (branch.Offset < formatting.Offset && destination.Offset >= formatting.Offset)
            {
                if (branch.OpCode != OpCodes.Brtrue_S) throw new InvalidOperationException("Unexpected branch before chatbox formatting.");
                branch.OpCode = OpCodes.Brtrue;
            }
        }
        var equals = (MethodReference)instructions.First(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "System.String" && m.Name == "op_Equality").Operand;
        il = result.Body.GetILProcessor();
        // Use the mode captured for THIS utterance so a setting change during a pending
        // translation cannot change its chatbox format. Keep the host's OSC guards/throttle.
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Ldfld, currentMode));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Ldstr, "control"));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Call, equals));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Brfalse, formatting));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Ldfld, original));
        il.InsertBefore(formatting, Instruction.Create(OpCodes.Br, storeMessage));
        return new[] { mode, result };
    }
}
