using System.Collections.Concurrent;
using System.Text.Json;

namespace SpaceBattle.Lib
{
    // ─── ЛР №7. InterpretCommand ─────────────────────────────────────────────

    /// <summary>
    /// ЛР №7. Команда интерпретации входящего сообщения.
    /// GameEndpoint помещает InterpretCommand в очередь ServerThread,
    /// а сама интерпретация (и создание игровой команды) происходит
    /// ВНУТРИ потока — это гарантирует thread-safety без блокировок.
    /// </summary>
    public class InterpretCommand : ICommand
    {
        private readonly string _json;
        // Словарь game_id → очередь входящих команд игры
        private readonly Dictionary<string, BlockingCollection<ICommand>> _gameQueues;
        // Fallback-очередь: используется когда game_id не найден
        private readonly BlockingCollection<ICommand>? _fallbackQueue;
        private readonly GameSpace _gameSpace;

        public InterpretCommand(
            string json,
            Dictionary<string, BlockingCollection<ICommand>> gameQueues,
            GameSpace gameSpace,
            BlockingCollection<ICommand>? fallbackQueue = null)
        {
            _json          = json        ?? throw new ArgumentNullException(nameof(json));
            _gameQueues    = gameQueues  ?? throw new ArgumentNullException(nameof(gameQueues));
            _gameSpace     = gameSpace   ?? throw new ArgumentNullException(nameof(gameSpace));
            _fallbackQueue = fallbackQueue;
        }

        public void Execute()
        {
            GameMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<GameMessage>(_json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[InterpretCommand] Ошибка разбора JSON: {ex.Message}");
                return;
            }

            if (msg == null) return;

            // Создаём команду — сначала пробуем IoC, потом встроенный интерпретатор
            ICommand cmd;
            try
            {
                cmd = IoC.Resolve<ICommand>($"Command.{msg.Type}", msg, _gameSpace);
            }
            catch (InvalidOperationException)
            {
                try   { cmd = BuiltinInterpret(msg); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[InterpretCommand] Неизвестная команда '{msg.Type}': {ex.Message}");
                    return;
                }
            }

            // Маршрутизация по game_id (ЛР №7: 6.6)
            var gameId = msg.GameId ?? "";
            if (_gameQueues.TryGetValue(gameId, out var queue))
            {
                queue.Add(cmd);
                return;
            }

            // Fallback 1: первая доступная игровая очередь
            var first = _gameQueues.Values.FirstOrDefault();
            if (first != null) { first.Add(cmd); return; }

            // Fallback 2: явно переданная fallback-очередь (для обратной совместимости)
            _fallbackQueue?.Add(cmd);
        }

        // ── Встроенный интерпретатор ─────────────────────────────────────────

        private ICommand BuiltinInterpret(GameMessage msg)
        {
            var obj = _gameSpace.GetObject(
                msg.GameItemId ?? throw new InvalidOperationException("gameItemId не указан."));

            return msg.Type switch
            {
                "start_movement" => BuildStartMove(obj, msg),
                "stop_movement"  => new EndMoveCommand(GetOrCreateBridge(obj)),
                "fire"           => new FireCommand(obj, _gameSpace),
                "rotate"         => BuildRotate(obj, msg),
                "move"           => new MoveCommand(new MovableAdapter(obj)),
                _ => throw new InvalidOperationException($"Неизвестный тип команды: '{msg.Type}'.")
            };
        }

        private ICommand BuildStartMove(IUObject obj, GameMessage msg)
        {
            int vx = 0, vy = 0;
            if (msg.Parameters != null)
            {
                if (msg.Parameters.TryGetValue("vx", out var ex)) vx = ex.GetInt32();
                if (msg.Parameters.TryGetValue("vy", out var ey)) vy = ey.GetInt32();
            }
            return new StartMoveCommand(obj, new Vector(vx, vy), GetOrCreateBridge(obj));
        }

        private ICommand BuildRotate(IUObject obj, GameMessage msg)
        {
            int av = 0;
            if (msg.Parameters != null
                && msg.Parameters.TryGetValue("angularVelocity", out var e))
                av = e.GetInt32();
            return new ActionCommand(() =>
            {
                obj.SetProperty("AngularVelocity", av);
                new RotateCommand(new RotatableAdapter(obj)).Execute();
            });
        }

        private static BridgeCommand GetOrCreateBridge(IUObject obj)
        {
            try   { return (BridgeCommand)obj.GetProperty("_bridge"); }
            catch
            {
                var b = new BridgeCommand(new EmptyCommand());
                obj.SetProperty("_bridge", b);
                return b;
            }
        }
    }
}
