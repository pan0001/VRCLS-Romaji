using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

try {
if (args.Length == 3 && args[0] == "check") { Contract.Require(args[1],args[2]); return; }
if (args.Length == 3 && args[0] == "contract") { Console.WriteLine(Contract.Get(args[1], args[2])); return; }
if (args.Length == 4 && args[0] == "desktop") { DesktopPatch.Apply(args[1], args[2], args[3]); return; }
bool direct = args.Length == 4 && args[0] == "in-place";
if(direct) args=args.Skip(1).ToArray();
if (args.Length != 3) throw new ArgumentException("Usage: Patcher original-services.dll romaji.dll output-services.dll");
var original = Path.GetFullPath(args[0]);
var helper = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (original.Equals(output, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Use a staging output.");
Contract.Require(original, "services");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(original));
resolver.AddSearchDirectory(Path.GetDirectoryName(helper));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
using var assembly = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { AssemblyResolver = resolver });
using var extension = AssemblyDefinition.ReadAssembly(helper, new ReaderParameters { AssemblyResolver = resolver });
var module = assembly.MainModule;
var service = module.GetType("VRCLS.Services.VR.SteamVROverlayService");
var hooks = extension.MainModule.GetType("VRCLS.Romaji.OverlayRomaji");
if (assembly.Name.HasPublicKey) throw new InvalidOperationException("Cannot modify a signed assembly.");
foreach (var name in new[] { "_lock", "_panelDirty", "_disposed" })
    if (!service.Fields.Any(f => f.Name == name)) throw new InvalidOperationException($"Missing required field: {name}");
var build = service.Methods.Single(m => m.Name == "BuildTranscriptBlocks" && m.Parameters.Count == 3 && m.IsStatic);
var update = service.Methods.Single(m => m.Name == "UpdatePanel" && m.Parameters.Count == 1 && !m.IsStatic);
var prepare = module.ImportReference(hooks.Methods.Single(m => m.Name == "Prepare"));
var decorate = module.ImportReference(hooks.Methods.Single(m => m.Name == "Decorate"));
var vrHooks = extension.MainModule.GetType("VRCLS.Romaji.VrSelfOverlay");
void AddEntryHook(string methodName, string hookName)
{
    var method = service.Methods.Single(m => m.Name == methodName);
    var hook = module.ImportReference(vrHooks.Methods.Single(m => m.Name == hookName));
    var body = method.Body.GetILProcessor();
    var start = method.Body.Instructions[0];
    body.InsertBefore(start, Instruction.Create(OpCodes.Ldarg_0));
    body.InsertBefore(start, Instruction.Create(OpCodes.Call, hook));
}
// Delay is entered outside the service lock, after the original frame has completed.
var loop = service.Methods.Single(m => m.Name == "NoteLoopStepEntered");
var loopHook = module.ImportReference(vrHooks.Methods.Single(m => m.Name == "OnLoopStep"));
var loopIl = loop.Body.GetILProcessor();
var loopStart = loop.Body.Instructions[0];
loopIl.InsertBefore(loopStart, Instruction.Create(OpCodes.Ldarg_0));
loopIl.InsertBefore(loopStart, Instruction.Create(OpCodes.Ldarg_1));
loopIl.InsertBefore(loopStart, Instruction.Create(OpCodes.Call, loopHook));
var delay = service.NestedTypes.Single(t => t.Name == "SteamVrLoopStep").Fields.Single(f => f.Name == "Delay");
if (Convert.ToInt32(delay.Constant) != 8) throw new InvalidOperationException("Loop step contract changed.");
AddEntryHook("CleanupFailedInit", "Release");
// Release only after the original event loop has stopped, before its OpenVR shutdown.
AddEntryHook("DestroyGenerationNativeResourcesLocked", "Release");

if(!direct) {
var manifest = service.Methods.Single(m => m.Name == "EnsureSteamVrManifestWritten");
var bridge = extension.MainModule.GetType("VRCLS.Romaji.ForkBridge");
var proc = manifest.Body.Instructions.Single(i => i.Operand is MethodReference m && m.FullName == "System.String System.Environment::get_ProcessPath()");
proc.Operand = module.ImportReference(bridge.Methods.Single(m => m.Name == "LauncherPath"));
var manifestDir = manifest.Body.Instructions.First(i => i.Operand is MethodReference m && m.FullName == "System.String System.AppContext::get_BaseDirectory()");
manifestDir.Operand = module.ImportReference(bridge.Methods.Single(m => m.Name == "ManifestDirectory"));
}
MicOriginalPatch.PatchService(module, extension);
var pipeline = module.GetType("VRCLS.Services.Audio.AudioPipelineService");
var audioMethod = pipeline.Methods.Single(m => m.Name == "OnAudioDataAvailable");
var audioIl = audioMethod.Body.GetILProcessor();
var audioStart = audioMethod.Body.Instructions[0];
var filter = module.ImportReference(extension.MainModule.GetType("VRCLS.Romaji.QuestMicGate").Methods.Single(m => m.Name == "Filter"));
audioIl.InsertBefore(audioStart, Instruction.Create(OpCodes.Ldarg_2));
audioIl.InsertBefore(audioStart, Instruction.Create(OpCodes.Ldarg_0));
audioIl.InsertBefore(audioStart, Instruction.Create(OpCodes.Call, filter));
audioIl.InsertBefore(audioStart, Instruction.Create(OpCodes.Starg, audioMethod.Parameters[1]));

// Preserve the installed implementation and all of its fixes. Only add entry/return hooks.
var updateIl = update.Body.GetILProcessor();
var first = update.Body.Instructions[0];
updateIl.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_1));
updateIl.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
updateIl.InsertBefore(first, Instruction.Create(OpCodes.Call, prepare));
update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 2);

var buildIl = build.Body.GetILProcessor();
var returns = build.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray();
if (returns.Length != 1) throw new InvalidOperationException("Unexpected subtitle method shape.");
foreach (var ret in returns)
{
    // Mutate the old ret so any existing branch to it also runs the hook.
    ret.OpCode = OpCodes.Ldarg_0;
    var mask = Instruction.Create(OpCodes.Ldarg_1);
    var call = Instruction.Create(OpCodes.Call, decorate);
    buildIl.InsertAfter(ret, mask);
    buildIl.InsertAfter(mask, call);
    buildIl.InsertAfter(call, Instruction.Create(OpCodes.Ret));
}
build.Body.MaxStackSize = Math.Max(build.Body.MaxStackSize, 3);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
assembly.Write(output);
using var verified = AssemblyDefinition.ReadAssembly(output);
if (verified.MainModule.Types.Count != module.Types.Count) throw new InvalidOperationException("Unexpected type changes.");
using var untouched = AssemblyDefinition.ReadAssembly(original);
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));
string Body(MethodDefinition m) => !m.HasBody ? "" : string.Join("\n", m.Body.Instructions.Select(i => i.ToString()));
var oldMethods = AllTypes(untouched.MainModule.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
var changed = AllTypes(verified.MainModule.Types).SelectMany(t => t.Methods)
    .Where(m => Body(m) != Body(oldMethods[m.FullName])).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
if (!changed.SequenceEqual(new[] { "BuildTranscriptBlocks", "CleanupFailedInit", "DestroyGenerationNativeResourcesLocked", "EnsureSteamVrManifestWritten", "GetEffectiveLanguagesForRequest", "NoteLoopStepEntered", "OnAudioDataAvailable", "UpdatePanel" }.Where(n=>!direct || n!="EnsureSteamVrManifestWritten")))
    throw new InvalidOperationException("Unexpected method changes: " + string.Join(", ", changed));
Console.WriteLine($"Verified {oldMethods.Count - changed.Length} other method bodies unchanged.");
Console.WriteLine($"Patched {changed.Length} methods; original version {assembly.Name.Version} preserved. Output SHA256: {Contract.Hash(File.ReadAllBytes(output))}");

} catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
