using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Nodes;



namespace Leigod_Auto_Pause.Injection
{
    internal class LeigodInjector

    {   
        public static int PickFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);//创建一个回环地址随机分配端口

            listener.Start();

            try
            {
                IPEndPoint ep = (IPEndPoint)listener.LocalEndpoint;
                int port = ep.Port;
                return port;
            }
            finally
            {
                listener.Stop();
            }

        }

        public static async Task<bool> InjectAsync(string leigodDir, string? tryJsPath)
        {

            Process[] alive = Process.GetProcessesByName("leigod");
            if (alive.Length > 0)
            {
                Console.WriteLine($"雷神已经在运行，请先完全退出。");
                return false;
            }

            string launcher = Path.Combine(leigodDir, "leigod_launcher.exe");
            if (!File.Exists(launcher))
            {
                Console.WriteLine($"找不到入口程序：{launcher}");
                return false;
            }

            int port = PickFreePort();
            Console.WriteLine($"端口：127.0.0.1:{port}");


            var startInfo = new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = leigodDir,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add($"--inspect-brk=127.0.0.1:{port}");

            Process process = Process.Start(startInfo)!;
            Console.WriteLine($" 已启动 pid={process.Id}");

            string? wsUrl = await WaitWsUrlAsync(port);
            if (wsUrl is null)
            {
                Console.WriteLine($"无法获取地址。");
                Kill(process);
                return false;
            }
            /*  "method": "Debugger.paused",
              "params": {
                "callFrames": [
                  {
                    "callFrameId": "0",
                    "functionName": "foo",
                    "url": "file:///.../main.js",
                    "location": { "scriptId": "5", "lineNumber": 10, "columnNumber": 2 },
                    ...
                  },
                  ...
                ],
                "reason": "breakpoint",
                ...
              }
            }*/
            await using var cdp = await CdpClient.ConnectAsync(wsUrl);
            Console.WriteLine("已连上端口");
            await cdp.SendAsync("Runtime.enable");
            await cdp.SendAsync("Debugger.enable");
            await cdp.SendAsync("Runtime.runIfWaitingForDebugger");//让目标跑起来                                                                
            JsonObject? paused = await cdp.WaitEventAsync("Debugger.paused", TimeSpan.FromSeconds(5));
            if (paused is null)
            {
                Console.WriteLine("尚未等到目标");
                Kill(process);
                return false;
            }
            JsonNode? frame = paused["params"]?["callFrames"]?[0]; //这一坨我是看难受了
            string callFrameId = (string?)frame?["callFrameId"] ?? "";
            string url = (string?)frame?["url"] ?? "?";
            int line = (int?)frame?["location"]?["lineNumber"] ?? -1;
            Console.WriteLine($"停在 {url}:{line}");


            if (tryJsPath is not null)
            {
                string jsSource = await File.ReadAllTextAsync(tryJsPath);
                Console.WriteLine($"注入字符");

                string expression = jsSource + "\n; 'Hachimi'"; 
                JsonNode? result = await cdp.SendAsync("Debugger.evaluateOnCallFrame", new JsonObject
                {
                    ["callFrameId"] = callFrameId,
                    ["expression"] = expression,
                    ["returnByValue"] = true,
                });
                string text = (string?)result?["result"]?["value"] ?? "(no value)";
                if (result?["exceptionDetails"] is not null)
                {
                    text = "EXCEPTION: " + result["exceptionDetails"]?.ToJsonString();
                }

                Console.WriteLine("结果：" + text);

                if (!text.StartsWith("Hachimi", StringComparison.Ordinal))
                {
                    Console.WriteLine("注入失败");
                    Kill(process);
                    return false;
                }
                
            }


            await cdp.SendAsync("Debugger.resume");
            Console.WriteLine("已放行");
            return true;

        }
        private static async Task<string?> WaitWsUrlAsync(int port)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            for (int i = 0; i < 200; i++)
            {
                try
                {
                    string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list");
                    if (JsonNode.Parse(json) is JsonArray arr)
                    {
                        foreach (JsonNode? node in arr)
                        {
                            string? ws = (string?)node?["webSocketDebuggerUrl"];
                            if (!string.IsNullOrEmpty(ws)) return ws;
                        }
                    }
                }
                catch
                {
                    // 端口还没起来，正常
                }

                await Task.Delay(100);
            }

            return null;
        }
        private static void Kill(Process p)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }

    }

}