using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Program
{
    [STAThread] static int Main()
    {
        try {
            var dir=AppDomain.CurrentDomain.BaseDirectory;
            var ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            var info=new ProcessStartInfo(ps,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(dir,"Manage.ps1")+"\" -Action Launch") {WorkingDirectory=dir,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            using(var p=Process.Start(info)){var error=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0) throw new Exception(error);return 0;}
        } catch(Exception ex){MessageBox.Show(ex.Message,"VRCLS Romaji",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}
