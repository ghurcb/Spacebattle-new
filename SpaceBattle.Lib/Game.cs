using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpaceBattle.Lib
{
    // ─── ЛР №8. Игра как команда ─────────────────────────────────────────────

    /// <summary>
    /// ЛР №8. Игровая партия реализует ICommand.
    /// Execute() = один квант времени:
    ///   1. Устанавливает game Scope текущим для потока.
    ///   2. Обрабатывает команды из incomingQueue до исчерпания кванта.
    ///   3. Выполняет CollisionCommand.
    ///   4. Восстанавливает предыдущий Scope.
    ///   5. Кладёт себя обратно в serverQueue (цикличность).
    /// </summary>
    public class Game : ICommand
    {
        private readonly string _gameId;
        private readonly IScope _gameScope;
        private readonly BlockingCollection<ICommand> _incomingQueue;
        private readonly BlockingCollection<ICommand> _serverQueue;
        private readonly GameSpace _gameSpace;
        private readonly int _quantumMs;
        private volatile bool _running = true;

        public string GameId => _gameId;
        public IScope GameScope => _gameScope;
        public BlockingCollection<ICommand> IncomingQueue => _incomingQueue;
        public GameSpace GameSpace => _gameSpace;

        public Game(
            string gameId,
            IScope gameScope,
            BlockingCollection<ICommand> incomingQueue,
            BlockingCollection<ICommand> serverQueue,
            GameSpace gameSpace,
            int quantumMs = 50)
        {
            _gameId        = gameId        ?? throw new ArgumentNullException(nameof(gameId));
            _gameScope     = gameScope     ?? throw new ArgumentNullException(nameof(gameScope));
            _incomingQueue = incomingQueue ?? throw new ArgumentNullException(nameof(incomingQueue));
            _serverQueue   = serverQueue   ?? throw new ArgumentNullException(nameof(serverQueue));
            _gameSpace     = gameSpace     ?? throw new ArgumentNullException(nameof(gameSpace));
            _quantumMs     = quantumMs;
        }

        /// <summary>Останавливает игру — больше не ставит себя в очередь.</summary>
        public void Stop() => _running = false;
        public bool IsRunning => _running;

        public void Execute()
        {
            if (!_running) return;

            var prevScope = IoC.GetCurrentScope();
            // 1. Устанавливаем Scope игры (ЛР №8: 6.7.3)
            IoC.SetCurrentScope(_gameScope);
            try
            {
                var sw = Stopwatch.StartNew();

                // 2. Обрабатываем команды в рамках кванта
                while (sw.ElapsedMilliseconds < _quantumMs
                       && _incomingQueue.TryTake(out var cmd, 0))
                {
                    try { cmd.Execute(); }
                    catch (Exception ex)
                    {
                        var handler = ExceptionHandler.Find(cmd, ex);
                        if (handler != null)
                            try { handler.Execute(); } catch { /* protect game loop */ }
                        else
                            Console.Error.WriteLine($"[GAME {_gameId}] Необработанное исключение в {cmd.GetType().Name}: {ex.Message}");
                    }
                }

                // 3. Проверяем коллизии
                try { new CollisionCommand(_gameSpace).Execute(); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GAME {_gameId}] Ошибка коллизий: {ex.Message}");
                }
            }
            finally
            {
                // 4. Восстанавливаем Scope
                IoC.SetCurrentScope(prevScope);
            }

            // 5. Цикличность: кладём себя обратно
            if (_running)
                _serverQueue.Add(this);
        }
    }

    // ─── ЛР №8. Жизненный цикл игр через IoC ─────────────────────────────────

    /// <summary>
    /// ЛР №8. Регистрирует в корневом Scope стратегии управления жизненным циклом игр:
    ///   "Игра.Создать новую"  — создаёт Game + изолированный Scope (ЛР №8: 6.7.4)
    ///   "Игра.Удалить игру"   — останавливает Game и очищает ресурсы (ЛР №8: 6.7.5)
    ///   "Игра.GetQueue"       — возвращает incomingQueue игры по её id
    /// </summary>
    public class GameLifecycle
    {
        private readonly Dictionary<string, Game> _games = new();
        private readonly Dictionary<string, ServerThread> _threads;
        private readonly int _quantumMs;

        public IReadOnlyDictionary<string, Game> ActiveGames => _games;

        public GameLifecycle(Dictionary<string, ServerThread> threads, int quantumMs = 50)
        {
            _threads   = threads   ?? throw new ArgumentNullException(nameof(threads));
            _quantumMs = quantumMs;
        }

        /// <summary>Регистрирует все игровые стратегии в переданном Scope.</summary>
        public void RegisterStrategies(IScope scope)
        {
            // ── ЛР №8: 6.7.4 — "Игра.Создать новую" ──────────────────────
            // args[0] = GameSpace, args[1] (optional) = threadKey string
            scope.Register("Игра.Создать новую", args =>
            {
                var gameSpace   = (GameSpace)args[0];
                var threadKey   = args.Length > 1 ? (string)args[1] : _threads.Keys.First();
                var serverQueue = _threads[threadKey].GetQueue();

                var gameId      = Guid.NewGuid().ToString("N")[..8];
                var gameScope   = (IScope)new Scope(IoC.GetCurrentScope());
                var incoming    = new BlockingCollection<ICommand>();

                var game = new Game(gameId, gameScope, incoming, serverQueue, gameSpace, _quantumMs);
                _games[gameId] = game;

                // Регистрируем игровую очередь доступной по имени
                scope.Register($"Game.{gameId}.Queue", _ => incoming);

                // Запускаем первый квант
                serverQueue.Add(game);

                Console.WriteLine($"[GameLifecycle] Создана игра {gameId} в потоке {threadKey}.");
                return gameId;
            });

            // ── ЛР №8: 6.7.5 — "Игра.Удалить игру" ───────────────────────
            scope.Register("Игра.Удалить игру", args =>
            {
                var gameId = (string)args[0];
                if (_games.TryGetValue(gameId, out var game))
                {
                    game.Stop();
                    _games.Remove(gameId);
                    Console.WriteLine($"[GameLifecycle] Игра {gameId} удалена.");
                }
                return (object)"ok";
            });

            // ── Вспомогательная: получить очередь игры ─────────────────────
            scope.Register("Игра.GetQueue", args =>
            {
                var gameId = (string)args[0];
                return _games.TryGetValue(gameId, out var g)
                    ? (object)g.IncomingQueue
                    : throw new InvalidOperationException($"Игра '{gameId}' не найдена.");
            });
        }

        /// <summary>
        /// Создаёт стандартную игру с готовым GameSpace и возвращает её game_id.
        /// Удобный метод для Program.cs.
        /// </summary>
        public string CreateGame(GameSpace gameSpace, string? threadKey = null)
        {
            var key      = threadKey ?? _threads.Keys.First();
            var serverQ  = _threads[key].GetQueue();
            var gameId   = Guid.NewGuid().ToString("N")[..8];
            var scope    = new Scope(IoC.GetCurrentScope());
            var incoming = new BlockingCollection<ICommand>();

            var game = new Game(gameId, scope, incoming, serverQ, gameSpace, _quantumMs);
            _games[gameId] = game;
            serverQ.Add(game);

            Console.WriteLine($"[GameLifecycle] Создана игра {gameId} в потоке {key}.");
            return gameId;
        }

        /// <summary>Возвращает incomingQueue для указанного game_id или null.</summary>
        public BlockingCollection<ICommand>? GetQueue(string gameId)
            => _games.TryGetValue(gameId, out var g) ? g.IncomingQueue : null;

        /// <summary>Словарь всех очередей входящих команд (game_id → queue).</summary>
        public Dictionary<string, BlockingCollection<ICommand>> GetAllQueues()
            => _games.ToDictionary(kv => kv.Key, kv => kv.Value.IncomingQueue);
    }
}
