using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal static class StartMenuHelper
    {
        public static void InstallOrUpdate()
        {
            try
            {
                string programs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs");
                if (!Directory.Exists(programs))
                    Directory.CreateDirectory(programs);

                string lnkPath = Path.Combine(programs, "hdr2sdr.lnk");
                string exe = Application.ExecutablePath;
                string workDir = Path.GetDirectoryName(exe);
                string ico = Path.Combine(workDir, "hdr2sdr.ico");
                if (!File.Exists(ico))
                    ico = exe;

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null)
                    return;
                object shell = Activator.CreateInstance(t);
                object shortcut = t.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { lnkPath });

                Type st = shortcut.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "hdr2sdr - HDR / SDR / luminosit\u00e9" });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { ico + ",0" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

                Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
            catch
            {
            }
        }
    }
}
