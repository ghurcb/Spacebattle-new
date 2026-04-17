using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SpaceBattle.Lib
{
    public class GameMessage
    {
        public string Type       { get; set; } = "";
        public string GameId     { get; set; } = "";
        public string GameItemId { get; set; } = "";
        public Dictionary<string, JsonElement>? Parameters { get; set; }
    }

    /// <summary>
    /// ЛР №7. HTTP Endpoint.
    /// Принимает POST / и GET /state.
    /// Для каждого POST создаёт InterpretCommand и кладёт его в очередь
    /// ServerThread (по game_id). Сам НЕ интерпретирует — за это отвечает InterpretCommand.
    /// GET /  (или /ui) — встроенный WEB-интерфейс.
    /// </summary>
    public class GameEndpoint
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly GameSpace _gameSpace;
        private readonly Dictionary<string, ServerThread> _threads;

        // Очереди входящих команд для каждого game_id (заполняются GameLifecycle)
        private readonly Dictionary<string, BlockingCollection<ICommand>> _gameQueues;

        private const string UiHtml = @"<!DOCTYPE html>
<html lang='ru'>
<head>
<meta charset='UTF-8'>
<title>SpaceBattle</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{background:#0a0a1a;color:#e0e0ff;font-family:'Segoe UI',sans-serif;display:flex;flex-direction:column;align-items:center;padding:20px}
h1{font-size:1.3rem;margin-bottom:12px;color:#7eb8ff;letter-spacing:2px;text-transform:uppercase}
canvas{display:block;background:#05050f;border:2px solid #2a4a7a;border-radius:4px}
.panel{margin-top:12px;display:flex;gap:8px;flex-wrap:wrap;justify-content:center;align-items:center}
select,input[type=number]{background:#111630;border:1px solid #2a4a7a;color:#c0d8ff;padding:5px 8px;border-radius:4px;font-size:.85rem}
button{background:#1a3a6a;border:1px solid #3a6aaa;color:#c0d8ff;padding:6px 14px;border-radius:4px;cursor:pointer;font-size:.85rem}
button:hover{background:#2a5a9a}
label{font-size:.82rem;color:#8090b0}
#status{margin-top:8px;font-size:.78rem;color:#607090}
#log{margin-top:8px;width:100%;max-width:820px;background:#050510;border:1px solid #1a2a4a;border-radius:4px;padding:8px;font-size:.75rem;color:#4a8a6a;height:80px;overflow-y:auto;font-family:monospace}
</style>
</head>
<body>
<h1>&#128640; SpaceBattle</h1>
<canvas id='c' width='800' height='600'></canvas>
<div class='panel'>
  <label>Корабль:</label><select id='ship'></select>
  <label>vx:</label><input type='number' id='vx' value='5' style='width:55px'>
  <label>vy:</label><input type='number' id='vy' value='0' style='width:55px'>
  <button onclick=""cmd('start_movement')"">&#9654; Движение</button>
  <button onclick=""cmd('stop_movement')"">&#9632; Стоп</button>
  <label>&#8635;:</label><input type='number' id='av' value='15' style='width:55px'>
  <button onclick=""cmd('rotate')"">&#8635; Поворот</button>
  <button onclick=""cmd('fire')"">&#128165; Огонь</button>
  <button onclick=""cmd('move')"">&#9654;| Шаг</button>
</div>
<div id='status'>Подключение...</div>
<div id='log'></div>
<script>
const cv=document.getElementById('c'),cx=cv.getContext('2d');
let st=null;
const stars=Array.from({length:80},()=>({x:Math.random()*800,y:Math.random()*600,r:Math.random()*1.2+0.3}));
function draw(){
  cx.clearRect(0,0,800,600);
  cx.strokeStyle='#0d1a2a';cx.lineWidth=1;
  for(let x=0;x<=800;x+=80){cx.beginPath();cx.moveTo(x,0);cx.lineTo(x,600);cx.stroke();}
  for(let y=0;y<=600;y+=60){cx.beginPath();cx.moveTo(0,y);cx.lineTo(800,y);cx.stroke();}
  cx.fillStyle='#ffffff22';
  stars.forEach(s=>{cx.beginPath();cx.arc(s.x,s.y,s.r,0,Math.PI*2);cx.fill();});
  if(!st)return;
  st.objects.forEach(o=>{
    const proj=o.type==='Projectile';
    const col=o.id.startsWith('ship-1')?'#4499ff':o.id.startsWith('ship-2')?'#ff6644':'#ffee44';
    cx.save();cx.translate(o.x,o.y);cx.rotate((o.angle||0)*Math.PI/180);
    if(proj){
      cx.fillStyle='#ffee44';cx.shadowColor='#ffaa00';cx.shadowBlur=8;
      cx.beginPath();cx.arc(0,0,4,0,Math.PI*2);cx.fill();
    }else{
      cx.fillStyle=col;cx.shadowColor=col;cx.shadowBlur=12;
      cx.beginPath();cx.moveTo(14,0);cx.lineTo(-8,-7);cx.lineTo(-4,0);cx.lineTo(-8,7);cx.closePath();cx.fill();
      cx.fillStyle='#ffffff66';cx.font='bold 9px monospace';
      cx.rotate(-(o.angle||0)*Math.PI/180);cx.fillText(o.id,12,-10);
    }
    cx.restore();
  });
}
async function poll(){
  try{
    const r=await fetch('/state');st=await r.json();
    document.getElementById('status').textContent=`Объектов: ${st.objects.length} | Поле: ${st.width}x${st.height} | ${new Date().toLocaleTimeString()}`;
    const sel=document.getElementById('ship'),cur=sel.value;
    const ids=st.objects.filter(o=>o.type==='Spaceship').map(o=>o.id);
    sel.innerHTML=ids.map(id=>`<option value='${id}'>${id}</option>`).join('');
    if(ids.includes(cur))sel.value=cur;
    draw();
  }catch{document.getElementById('status').textContent='Сервер недоступен';}
}
async function cmd(type){
  const id=document.getElementById('ship').value;
  const b={type,gameItemId:id,gameId:'game1',parameters:{}};
  if(type==='start_movement'){b.parameters.vx=+document.getElementById('vx').value||0;b.parameters.vy=+document.getElementById('vy').value||0;}
  else if(type==='rotate'){b.parameters.angularVelocity=+document.getElementById('av').value||0;}
  try{
    const r=await fetch('/',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(b)});
    const res=await r.json();
    const box=document.getElementById('log');
    box.textContent=new Date().toLocaleTimeString()+' '+type+' '+id+': '+JSON.stringify(res)+'\n'+box.textContent;
  }catch(e){document.getElementById('log').textContent='Ошибка: '+e.message;}
}
setInterval(poll,800);poll();
</script>
</body>
</html>";

        public GameEndpoint(
            string prefix,
            GameSpace gameSpace,
            Dictionary<string, ServerThread> threads,
            Dictionary<string, BlockingCollection<ICommand>>? gameQueues = null)
        {
            _listener   = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _gameSpace  = gameSpace  ?? throw new ArgumentNullException(nameof(gameSpace));
            _threads    = threads    ?? throw new ArgumentNullException(nameof(threads));
            _gameQueues = gameQueues ?? new Dictionary<string, BlockingCollection<ICommand>>();
        }

        public void Start() { _listener.Start(); Task.Run(() => Listen(_cts.Token)); }
        public void Stop()  { _cts.Cancel(); _listener.Stop(); }

        private async Task Listen(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => Handle(ctx));
                }
                catch (HttpListenerException) when (token.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"]  = "*";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

                if (ctx.Request.HttpMethod == "OPTIONS")
                { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                var path = ctx.Request.Url?.AbsolutePath ?? "/";

                // ── Web UI ──────────────────────────────────────────────
                if (ctx.Request.HttpMethod == "GET" && (path == "/" || path == "/ui"))
                {
                    var buf = Encoding.UTF8.GetBytes(UiHtml);
                    ctx.Response.StatusCode    = 200;
                    ctx.Response.ContentType   = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = buf.Length;
                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                    ctx.Response.Close();
                    return;
                }

                // ── Состояние пространства ──────────────────────────────
                if (ctx.Request.HttpMethod == "GET" && path == "/state")
                {
                    Respond(ctx, 200, _gameSpace.GetState().ToJson());
                    return;
                }

                // ── Принять команду ─────────────────────────────────────
                if (ctx.Request.HttpMethod == "POST")
                {
                    using var sr  = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    var json      = sr.ReadToEnd();

                    // Предварительно читаем game_id для маршрутизации
                    var preview = JsonSerializer.Deserialize<GameMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (preview == null) { Respond(ctx, 400, "{\"error\":\"Invalid JSON\"}"); return; }

                    // ЛР №7: создаём InterpretCommand, кладём в очередь ServerThread
                    var interpretCmd = new InterpretCommand(json, _gameQueues, _gameSpace);

                    // Маршрутизация по game_id → конкретный поток
                    // Если game_id есть в _threads — направляем туда, иначе в первый
                    ServerThread? target;
                    if (!_threads.TryGetValue(preview.GameId ?? "", out target))
                        target = _threads.Values.FirstOrDefault();

                    if (target == null) { Respond(ctx, 503, "{\"error\":\"No threads\"}"); return; }

                    target.GetQueue().Add(interpretCmd);
                    Respond(ctx, 200, "{\"status\":\"accepted\"}");
                    return;
                }

                Respond(ctx, 404, "{\"error\":\"Not found\"}");
            }
            catch (Exception ex) { Respond(ctx, 400, $"{{\"error\":\"{ex.Message}\"}}"); }
        }

        private static void Respond(HttpListenerContext ctx, int status, string body)
        {
            ctx.Response.StatusCode    = status;
            ctx.Response.ContentType   = "application/json; charset=utf-8";
            var buf = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
    }
}
