using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Leigod_Auto_Pause.Injection
{
    public sealed class CdpClient
    {
        private readonly ClientWebSocket _ws= new();
        private int _nextId;//请求编号
        private readonly List<JsonObject> _backlog = new();

        private CdpClient()
        {
         
        }

        //连上调试端口。
        public static async Task<CdpClient>  ConnectAsync(string wsUrl ,CancellationToken ct =default)
        {
            var client = new CdpClient();
            await client._ws.ConnectAsync(new Uri(wsUrl), ct);
            return client;

        }

        //发包
        /*{
        "id": 1,                              
        "method": "Debugger.setBreakpoint",   
        "params": { ... }                     
}       */

        public async Task<JsonNode> SendAsync(string method, JsonObject parameters = null, CancellationToken ct = default)
        {
            int id = ++_nextId;
            var req = new JsonObject
            {
                ["id"] = id,
                ["method"] = method,
            };
            if (parameters != null)
            {
                req["params"] = parameters;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(req.ToJsonString());
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

            while (true)
            {
                JsonObject msg = await ReceiveAsync(ct);
                if (msg == null)
                {
                    throw new Exception("WebSocket closed");
                }
                if ((int?)msg["id"] == id)
                {//如果id对得上就确定是我们的 进入
                    if (msg["error"] is JsonObject error)
                    {//如果有错误就抛出异常
                        throw new Exception($"CDP Error: {error["message"]}");
                    }
                    return msg["result"];
                }
                _backlog.Add(msg); //查找method 包

            }
        }

        public async Task<JsonObject?> WaitEventAsync(string method, TimeSpan timeout, CancellationToken ct = default)
        {
            for (int i = 0; i < _backlog.Count; i++)
            {//取出method
                if ((string?)_backlog[i]["method"] == method)
                {
                    JsonObject hit = _backlog[i];
                    _backlog.RemoveAt(i);
                    return hit;
                }

            }

            DateTime deadline = DateTime.UtcNow + timeout;
            //如果没来就等
            while (true)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return null;
                }
                JsonObject? msg;
                try
                {
                    msg= await ReceiveAsync(ct).WaitAsync(remaining, ct);
                }
                catch
                {
                    return null;
                }
              if(msg == null)
                {
                    return null;
                }
                if ((string?)msg["method"] == method)
                {
                    return msg;
                }
                _backlog.Add(msg);
            }
        }

        private async Task<JsonObject> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            var ms = new MemoryStream();
            while (true)
            {

                //收到数据包
                WebSocketReceiveResult result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }
             try
                {
                    byte[] bytes = ms.ToArray();
                    JsonNode? node = JsonNode.Parse(bytes);
                    JsonObject? obj = node as JsonObject;
                    return obj;
                }
                catch
                {
                    return null;
                
                }
  
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch
            {
            }
            _ws.Dispose();
        }


    }
}
/*
 偽りなど　どこかに
いつの間にか置き忘れてきた
(だけどひとつの確かなこと) 確かに
長い長い瞬間ときにあなたがいて 良かったと思うの
(長い長い瞬間ときを君の傍で 良かったと思うよ)

春の風のように 暖かく流れていく
(春の湊に 風はそっと)
こんな時間を人は 幸せと呼ぶのだろう
(時は静かに 流れ落ちて)
噓の下手な君の 強がりとか弱音は
(噓はいらない 気持ち一つ)
とっくに聞き飽きたから 暗い朝を照らしていこう
(そしてあなたと 朝を照らしていきましょう)
*/