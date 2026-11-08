using System.Security.Principal;
using System.Diagnostics;
using System.Text.Json;
using Leigod_Auto_Pause.Injection;
using System.Text;
using System;
using static System.Runtime.InteropServices.JavaScript.JSType;

class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);


    static readonly string[] jsDownloadUrls =
   {
        "https://gitee.com/assortest/Leigod_Auto_Pause/raw/main/main.js", // 主节点
        "https://raw.githubusercontent.com/assortest/Leigod_Auto_Pause/refs/heads/main/main.js"          // 备用节点
    };
    static readonly string _configFolderPath = Path.Combine(Environment.GetFolderPath
              (Environment.SpecialFolder.ApplicationData)
              , "LeigodPatcher");

    public static async Task<byte[]> DownloadJsFileAsync(TimeSpan? timeout = null)
    { //用于拉取文件内容
        using (var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) })
        {

            client.DefaultRequestHeaders.Add("User-Agent", "Leigod Auto Pause Patch Tool");
            foreach (var url in jsDownloadUrls)
            {
                try
                {

                    Console.WriteLine($"尝试从此处下载: {url}");

                    byte[] fileBytes = await client.GetByteArrayAsync(url); //采用二进制防止编码问题
                    if (fileBytes != null && fileBytes.Length > 0)
                    {
                        return fileBytes; // 成功下载，返回文件内容
                    }
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"从 {url} 下载文件失败: {ex.Message}");

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.ToString());
                    Console.ResetColor();



                }
            }
            throw new Exception("所有下载节点均失败，请检查网络连接或稍后再试。");
        }
    }



    static async Task Main(string[] args)
    {
        bool console = false;
        foreach (string arg in args)
        {
            if (arg.Equals("--console", StringComparison.OrdinalIgnoreCase))
            {
                console = true;
            }
        }
        StartLog(console);
        try
        {
            await RunInjectAsync(console);

        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, "出错：\n\n" + ex.Message, "Leigod Auto Pause", 0x10);
        }
        finally
        {
            try { Console.Out.Flush(); } catch { }
        }



    }

    static async Task RunInjectAsync(bool console)
    {
        string leigodDir = AppContext.BaseDirectory;
        string launcher = Path.Combine(leigodDir, "leigod_launcher.exe");
        if (!File.Exists(launcher))
        {
            Console.WriteLine("找不到雷神启动器");
            MessageBox(IntPtr.Zero,
                "找不到 leigod_launcher.exe。\n\n请把本程序放进雷神加速器的安装目录后再运行。",
                "Leigod Auto Pause", 0x10);
            return;
        }
        string? error = null;
        try
        {
            Console.WriteLine("准备插件");
            string tryJs = await EnsurePluginAsync();

            Console.WriteLine("注入");
            bool ok = await LeigodInjector.InjectAsync(leigodDir, tryJs);
            if (!ok)
            {
                error = "注入没有成功（可能雷神已经在运行，或端口被占用）";
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        if (error is null)
        {
            Console.WriteLine("完成，雷神已启动");
        }
        else
        {
            Console.WriteLine("[!] " + error);
            MessageBox(IntPtr.Zero,
                "打开失败：\n\n" + error + "\n\n将改为正常启动雷神（不带插件）。",
                "Leigod Auto Pause", 0x10);

            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                UseShellExecute = true,
            });
        }

        if (console)
        {
            Console.WriteLine("按任意键退出");
            Console.ReadKey();
        }


    }


    static async Task<string> EnsurePluginAsync()
    {
        string dir = Path.Combine(_configFolderPath, "plugin");
        Directory.CreateDirectory(dir);

        string cache = Path.Combine(dir, "try.js");
        string tmp = cache + ".tmp";
        if (File.Exists(cache))
        {
            try
            {
                byte[] bytes = await DownloadJsFileAsync(TimeSpan.FromSeconds(5));
                if (string.Equals(GetBytesSha256(bytes), GetFileSha256(cache)))
                {
                    Console.WriteLine("插件已是最新");
                    return cache;
                }
                else
                {
                    await File.WriteAllBytesAsync(tmp, bytes);
                    File.Move(tmp, cache, true);
                    Console.WriteLine("插件已更新");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("[插件] 网络不可用，用本地缓存：" + ex.Message);
            }

            return cache;
        }

        Console.WriteLine("插件首次运行，正在下载...");
        byte[] data = await DownloadJsFileAsync(TimeSpan.FromSeconds(15));
        await File.WriteAllBytesAsync(tmp, data);
        File.Move(tmp, cache, true);
        return cache;

    }







    public static string GetFileSha256(string filePath)
    { //用于哈希计算
        using var sha256 = System.Security.Cryptography.SHA256.Create();//创建SHA256实例
        {
            using var stream = File.OpenRead(filePath);//打开文件流
            {
                byte[] hashBytes = sha256.ComputeHash(stream);//计算文件的哈希值

                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();//将字节数组转换为十六进制字符串
            }
        }
    }
    public static string GetBytesSha256(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(data);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }



    public static bool IsRunningAsAdmin()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) //返回当前Windows用户的标识
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);//将身份包装成可进行角色检查的对象
            return principal.IsInRole(WindowsBuiltInRole.Administrator);//检查当前用户是否为管理员
        }
    }





    static void StartLog(bool console)
    {
        if (console && GetConsoleWindow() == IntPtr.Zero) AllocConsole();
        try
        {
            string dir = Path.Combine(_configFolderPath, "logs");
            Directory.CreateDirectory(dir);

            var file = new StreamWriter(Path.Combine(dir, "inject.log"), false,
                                        new UTF8Encoding(false))
            { AutoFlush = true };

            file.WriteLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

            Console.SetOut(new Tee(Console.Out, file));   // 两份都写
        }
        catch { }
    }
}


