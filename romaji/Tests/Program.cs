using VRCLS.Core.Models;
using VRCLS.Romaji;
using VRCLS.Services.VR;
using System.Reflection;
if(args.FirstOrDefault()=="manifest")
{
    var launch=Environment.GetEnvironmentVariable("VRCLS_ROMAJI_LAUNCHER");
    var path=(string)typeof(SteamVROverlayService).GetMethod("EnsureSteamVrManifestWritten",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
    using var json=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
    if(System.IO.Path.GetDirectoryName(path)!=System.IO.Path.GetDirectoryName(launch) || json.RootElement.GetProperty("applications")[0].GetProperty("binary_path_windows").GetString()!=launch) throw new Exception("Unstable SteamVR launch target");
    return;
}
var s=new SteamVROverlayService(new AppConfig { SteamVrAutoLaunch=false });
var item=new SteamVrOverlayTranscript { Source="Mic", Original="你好",Translation="こんにちは。",Timestamp=DateTime.UtcNow };
s.UpdatePanel(new SteamVrOverlayPanelState { MicTargetLanguage="ja", Transcripts=[item] });
for(int i=0;i<200 && VrSelfOverlay.GetText(s).Romaji!="konnichiwa.";i++) await Task.Delay(25);
if(VrSelfOverlay.GetText(s).Romaji!="konnichiwa.") throw new Exception("Romaji runtime hook or dictionary missing.");
s.UpdatePanel(new SteamVrOverlayPanelState { Transcripts=[new SteamVrOverlayTranscript {Source="Desktop",Translation="other"}] });
if(VrSelfOverlay.GetText(s).Translation!=item.Translation) throw new Exception("Pinned speech lost.");
var frame=new AudioDataEventArgs([.5f]);
if(QuestMicGate.FilterFrame(frame,true,true).AudioData[0]!=0 || QuestMicGate.FilterFrame(frame,true,false)!=frame || QuestMicGate.FilterFrame(frame,false,true)!=frame) throw new Exception("Mic gate failed.");
foreach (var mode in new[] { VoiceTriggerMode.Continuous, VoiceTriggerMode.HotkeyToggle, VoiceTriggerMode.PressToTalk })
foreach (var xDown in new[] { false, true })
foreach (var vState in new short[] { 0, 1, short.MinValue, -32767 })
{
    bool expected = xDown || (mode == VoiceTriggerMode.Continuous && vState < 0);
    bool muted = QuestMicGate.ShouldMute((int)mode, xDown, vState);
    if (muted != expected || QuestMicGate.FilterFrame(frame, true, muted).AudioData[0] != (expected ? 0f : .5f)
        || QuestMicGate.FilterFrame(frame, false, muted) != frame)
        throw new Exception($"Keyboard/Quest mic filtering failed for {mode}, X={xDown}, V={vState}");
}
if(Assembly.Load("VRCLS").GetType("VRCLS.UI.Views.MainWindow")==null) throw new Exception("Cannot load UI.");
Console.WriteLine("Fork smoke check passed.");
