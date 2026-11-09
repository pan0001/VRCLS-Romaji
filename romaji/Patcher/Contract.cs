using Mono.Cecil;
using System.Security.Cryptography;
using System.Text;

// A build may change outside these integration points. Fingerprint the IL and
// private state we depend on, not the MVID, product revision, or complete file.
internal static class Contract
{
    internal static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    internal static void Require(string path, string role)
    {
        var digest = Get(path, role);
        bool supported = role switch
        {
            "ui" => digest == "E4845E0D7293B9D15FE02617CB1973D3F6E3FF7F4BE92BD7BD616FADC2C2A3D0",
            "core" => digest is "7644AC75BC9963BE4DD1C149BB5BD0011F9B2290349341E462B7A5EA27582ECD"
                or "A1FBCEF7E75EA4B09B1A7F686E562514D7606D51A44B2C4D086CF8DEB3C24E31",
            "services" => digest is "FAF001FFF982CBD24737E46687CC52C574A36CE3B44A295424F5C202FB633EFF"
                or "092ECC5F32937F4A4DC513B41CC12B81D1BD69A590F7ED52582621A1E3037937",
            _ => false
        };
        if (!supported) throw new InvalidOperationException("Unsupported " + role + " integration contract. The previous working generation is preserved.");
    }
    internal static string Get(string path, string role)
    {
        using var a = AssemblyDefinition.ReadAssembly(path);
        var lines = new List<string>();
        void Body(MethodDefinition m) {
            lines.Add(m.FullName + "\n" + string.Join("\n", m.Body.Instructions.Select(i => i.ToString())));
            lines.Add(string.Join(";",m.Body.ExceptionHandlers.Select(h => $"{h.HandlerType}:{h.TryStart?.Offset}:{h.TryEnd?.Offset}:{h.HandlerStart?.Offset}:{h.HandlerEnd?.Offset}:{h.CatchType}")));
        }
        IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[]{t}.Concat(Types(t.NestedTypes)));
        if (role == "core") {
            foreach(var t in a.MainModule.Types.Where(t => t.Name == "AppConfig" || t.Name.StartsWith("SteamVr") || t.Name == "AudioDataEventArgs" || t.Name == "TranscriptLanguageMask" || t.Name == "TranscriptItem")) {
                lines.Add(t.FullName);
                lines.AddRange(t.Methods.Where(m=>m.IsPublic).Select(m=>m.FullName));
                lines.AddRange(t.Fields.Where(f=>f.IsPublic).Select(f=>f.FullName+":"+f.Constant));
            }
        } else if(role == "ui") {
            Body(a.MainModule.GetType("VRCLS.UI.Views.MainWindow").Methods.Single(m=>m.IsConstructor && !m.IsStatic && m.Parameters.Count==1));
        } else if(role == "services") {
            var t=a.MainModule.GetType("VRCLS.Services.VR.SteamVROverlayService");
            foreach(var n in new[]{"BuildTranscriptBlocks","UpdatePanel","CleanupFailedInit","DestroyGenerationNativeResourcesLocked","NoteLoopStepEntered","EnsureSteamVrManifestWritten"}) Body(t.Methods.Single(m=>m.Name==n));
            foreach(var n in new[]{"_lock","_panelDirty","_disposed","_overlayHandle","_desiredVisible","_config","_panelTransformInDeviceSpace","_panelTrackedDeviceIndex"}) lines.Add(t.Fields.Single(f=>f.Name==n).FullName);
            foreach(var f in t.NestedTypes.Single(x=>x.Name=="SteamVrLoopStep").Fields.Where(f=>f.HasConstant)) lines.Add(f.FullName+":"+f.Constant);
            foreach(var m in Types(new[]{t}).SelectMany(x=>x.Methods).Where(m=>m.HasBody && m.Body.Instructions.Any(i=>i.Operand is MethodReference mr && mr.Name=="NoteLoopStepEntered"))) Body(m);
            var audio=a.MainModule.GetType("VRCLS.Services.Audio.AudioPipelineService");
            lines.Add(audio.Fields.Single(f=>f.Name=="_sourceName").FullName);
            Body(audio.Methods.Single(m=>m.Name=="OnAudioDataAvailable"));
            foreach(var v in a.MainModule.Types.Where(x=>x.Namespace=="Valve.VR")) {
                lines.AddRange(v.Fields.Select(f=>f.FullName+":"+f.Constant));
                lines.AddRange(v.Methods.Where(m=>m.IsPublic).Select(m=>m.FullName));
            }
        } else throw new ArgumentException(role);
        return Hash(Encoding.UTF8.GetBytes(string.Join("\n",lines.OrderBy(s=>s,StringComparer.Ordinal))));
    }
}
