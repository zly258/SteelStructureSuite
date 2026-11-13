using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>
    /// 走 AutoCAD 核心控制台（accoreconsole.exe）的 DXF 导出。
    ///
    /// 背景：图纸里含 AEC（Architecture / Building Model）对象时，
    /// 在已打开的 AutoCAD 进程内调用 <c>Database.DxfOut</c> 会让
    /// aecbase.dbx 的 ARX 模块抛出 c0000027 未处理异常，直接带崩整个 CAD 进程。
    /// 改到独立的子进程里导出，异常只会杀掉那个子进程，
    /// 用户当前会话不受影响，单个文件失败也能继续处理后面的文件。
    /// </summary>
    public static class DxfOutOfProcessConverter
    {
        private const int TimeoutMs = 180000;

        /// <summary>把 DWG 版本号映射成 DXFOUT 命令认识的版本关键字。</summary>
        public static string GetVersionToken(string versionKey)
        {
            switch (versionKey)
            {
                case "AC1009": return "R12";
                case "AC1015": return "2000";
                case "AC1018": return "2004";
                case "AC1800": return "2004";
                case "AC1021": return "2007";
                case "AC1024": return "2010";
                case "AC1027": return "2013";
                case "AC1032": return "2018";
                default: return "2018";
            }
        }

        /// <summary>
        /// 用核心控制台把一张图纸导出成 DXF。
        /// </summary>
        /// <returns>null 表示成功，否则返回失败原因。</returns>
        public static string Convert(string sourcePath, string outputPath, string versionToken)
        {
            string exe = FindCoreConsole();
            if (exe == null) return "未找到 accoreconsole.exe，无法在独立进程中导出 DXF。";
            if (!File.Exists(sourcePath)) return "源文件不存在：" + sourcePath;

            string outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (string.IsNullOrEmpty(outDir)) outDir = ".";
            Directory.CreateDirectory(outDir);

            string workDir = Path.Combine(Path.GetTempPath(), "sss-dxf-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(workDir);
            string script = Path.Combine(workDir, "convert.scr");
            // 先写到临时文件，成功后再替换目标，避免留下半截 DXF。
            string tempOut = Path.Combine(outDir, Path.GetFileNameWithoutExtension(outputPath) + ".sstmp.dxf");

            try
            {
                // DXFOUT 依次询问：输出文件、精度（其中 [版本(V)] 可以先选版本）、版本。
                // 实测顺序：文件名 -> 输入精度 <16> -> （V）-> 输入版本 -> 再回精度。
                var sb = new StringBuilder();
                sb.AppendLine("DXFOUT");
                sb.AppendLine(Path.GetFullPath(tempOut));
                sb.AppendLine("V");
                sb.AppendLine(versionToken ?? "2018");
                sb.AppendLine("16");
                sb.AppendLine();
                File.WriteAllText(script, sb.ToString(), Encoding.Default);

                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = BuildArguments(sourcePath, script),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workDir
                };

                using (var proc = new Process { StartInfo = startInfo })
                {
                    proc.Start();
                    // 必须把输出读走，否则管道写满后核心控制台会卡住。
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(TimeoutMs))
                    {
                        try { proc.Kill(); }
                        catch (Exception) { }
                        return "导出 DXF 超时（" + (TimeoutMs / 1000) + " 秒）。";
                    }
                    try { outTask.Wait(5000); } catch (Exception) { }
                    try { errTask.Wait(5000); } catch (Exception) { }
                }

                if (!File.Exists(tempOut)) return "核心控制台未生成 DXF 文件。";
                var info = new FileInfo(tempOut);
                if (info.Length == 0) return "核心控制台生成的 DXF 文件为空。";
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(tempOut, outputPath);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                try { if (File.Exists(tempOut)) File.Delete(tempOut); }
                catch (Exception) { }
                try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); }
                catch (Exception) { }
            }
        }

        private static string BuildArguments(string sourcePath, string script)
        {
            return "\"/i\" \"" + sourcePath + "\" \"/s\" \"" + script + "\" \"/l\" \"en-US\"";
        }

        /// <summary>核心控制台与当前 AutoCAD 同目录，按当前安装位置查找。</summary>
        private static string FindCoreConsole()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                string exe = proc.MainModule.FileName;
                string dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir)) return null;
                string core = Path.Combine(dir, "accoreconsole.exe");
                return File.Exists(core) ? core : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
