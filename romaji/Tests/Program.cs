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
if(Assembly.Load("VRCLS").GetType("VRCLS.UI.Views.MainWindow")==null) throw new Exception("Cannot load UI.");
Console.WriteLine("Fork smoke check passed.");
