using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;

internal static class Program
{
    static string Signature(string root){
        var s=new StringBuilder();
        var names=new List<string>{"VRCLS.exe","VRCLS.Core.dll","VRCLS.Services.dll","VRCLS.dll","VRCLS.deps.json","VRCLS.runtimeconfig.json","VRCLS.Romaji.dll","Kawazu.dll","LibNMeCab.dll","LibNMeCab.IpaDicBin.dll","romaji-dic/sys.dic","romaji-dic/char.bin","romaji-dic/matrix.bin","romaji-dic/unk.dic","romaji-dic/AUTHORS","romaji-dic/COPYING"};
        foreach(var n in names){var f=new FileInfo(Path.Combine(root,n));s.Append(f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"-").Append(';');}
        return s.ToString();
    }
    static int Main(){
        var dir=AppDomain.CurrentDomain.BaseDirectory;
        bool owner;
        using(var mutex=new Mutex(true,@"Local\VRCLS-Romaji-InPlace-Observer",out owner)){
            if(!owner)return 0;
            string seen=null,done=null;
            var stableSince=DateTime.UtcNow;var retryAt=DateTime.MinValue;
            while(File.Exists(Path.Combine(dir,"auto.enabled"))){
                try{
                    var root=File.ReadAllText(Path.Combine(dir,"source.txt")).Trim();
                    var sig=Signature(root);
                    if(sig!=seen){seen=sig;stableSince=DateTime.UtcNow;retryAt=DateTime.MinValue;}
                    else if(done!=sig && DateTime.UtcNow>=retryAt && File.Exists(Path.Combine(root,"VRCLS.exe")) && (DateTime.UtcNow-stableSince).TotalSeconds>=15){
                        bool busy=false;
                        foreach(var p in Process.GetProcessesByName("VRCLS"))using(p){try{busy|=string.Equals(p.MainModule.FileName,Path.Combine(root,"VRCLS.exe"),StringComparison.OrdinalIgnoreCase);}catch{busy=true;}}
                        if(!busy){
                            var ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
                            using(var p=Process.Start(new ProcessStartInfo(ps,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(dir,"Repair.ps1")+"\""){WorkingDirectory=dir,UseShellExecute=false,CreateNoWindow=true})){
                                p.WaitForExit();
                                // Check our own writes once more; a racing updater must not be acknowledged by mistake.
                                if(p.ExitCode==0)done=sig;else retryAt=DateTime.UtcNow.AddMinutes(5);
                            }
                        }
                    }
                }catch(IOException){}catch(UnauthorizedAccessException){}
                Thread.Sleep(3000);
            }
        }
        return 0;
    }
}
