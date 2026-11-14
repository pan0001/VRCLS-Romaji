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
            "ui" => digest is "99DA989C1CEA927668F57FEF087C22ACAEFABC3143701DF8586360650ED5FD7A"
                or "19FF68CE221464C8D2CE2BB0DA7DB14A998474944F38FDE933CD646608BF8778",
            "core" => digest is "7644AC75BC9963BE4DD1C149BB5BD0011F9B2290349341E462B7A5EA27582ECD"
                or "A1FBCEF7E75EA4B09B1A7F686E562514D7606D51A44B2C4D086CF8DEB3C24E31",
            "services" => digest is "A6CBC0D1E518B317C85D08CEC84FFC26BC03CFCBB6DC43C04EA229799F9BB059"
                or "53EA7F88F20DCEF456257830EDEFBD732E6BA26AFF3AC43A695A9C35A9315DC9",
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
            Body(MicOriginalPatch.MainViewModel(a.MainModule).Methods.Single(m => m.Name == "GetEffectiveMode"));
            Body(MicOriginalPatch.RecognitionResult(a.MainModule));
        } else if(role == "services") {
            var languages = MicOriginalPatch.RecognitionLanguages(a.MainModule);
            Body(languages);
            foreach (var method in Types(new[] { languages.DeclaringType }).SelectMany(type => type.Methods).Where(m => m.HasBody &&
                (m.Name.Contains("g__CreateRecognitionRequest") || m.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.FullName == languages.FullName)))) Body(method);
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
