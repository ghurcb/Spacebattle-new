using System.Collections.Concurrent;

namespace SpaceBattle.Lib
{
    // ─── ЛР №6. Многопоточный сервер ────────────────────────────────────────

    /// <summary>
    /// Поток-обработчик команд. Извлекает ICommand из BlockingCollection и выполняет их.
    /// При исключении из команды ищет обработчик через ExceptionHandler; если не найден — логирует.
    /// Флаг _executing позволяет SoftStop дождаться завершения текущей команды.
    /// </summary>
    public class ServerThread
    {
        private readonly BlockingCollection<ICommand> _queue;
        private readonly Thread _thread;
        private volatile bool _stop;
        private volatile bool _executing;

        public ServerThread(BlockingCollection<ICommand> queue)
        {
            _queue  = queue ?? throw new ArgumentNullException(nameof(queue));
            _thread = new Thread(Run) { IsBackground = true };
        }

        public void Start()  => _thread.Start();
        public void Stop()   => _stop = true;

        /// <summary>Возвращает текущую очередь команд.</summary>
        public BlockingCollection<ICommand> GetQueue() => _queue;

        /// <summary>Истина, если поток прямо сейчас выполняет команду.</summary>
        public bool IsExecuting => _executing;

        internal Thread GetThread() => _thread;

        private void Run()
        {
            while (!_stop)
            {
                try
                {
                    if (_queue.TryTake(out var cmd, TimeSpan.FromMilliseconds(100)))
                    {
                        _executing = true;
                        try
                        {
                            cmd.Execute();
                        }
                        catch (Exception ex)
                        {
                            var handler = ExceptionHandler.Find(cmd, ex);
                            if (handler != null)
                                try { handler.Execute(); } catch { /* защита от исключений в обработчике */ }
                            else
                                Console.Error.WriteLine($"[ERROR] {cmd.GetType().Name}: {ex.Message}");
                        }
                        finally
                        {
                            _executing = false;
                        }
                    }
                }
                catch (InvalidOperationException) { break; }
            }
        }
    }

    // ─── Команды остановки ───────────────────────────────────────────────────

    /// <summary>
    /// ЛР №6. Немедленная остановка потока. Перед остановкой проверяет,
    /// что команда выполняется в контексте целевого потока.
    /// </summary>
    public class HardStopCommand : ICommand
    {
        private readonly ServerThread _serverThread;
        private readonly Action? _onStop;

        public HardStopCommand(ServerThread serverThread, Action? onStop = null)
        {
            _serverThread = serverThread ?? throw new ArgumentNullException(nameof(serverThread));
            _onStop = onStop;
        }

        public void Execute()
        {
            // Проверка контекста: команда должна выполняться в потоке целевого ServerThread
            if (Thread.CurrentThread != _serverThread.GetThread())
                throw new InvalidOperationException("HardStop выполняется вне контекста целевого потока.");

            _serverThread.Stop();
            _onStop?.Invoke();
        }
    }

    /// <summary>
    /// ЛР №6. Мягкая остановка: останавливает поток, только когда очередь пуста
    /// И поток не выполняет текущую команду. Иначе повторно добавляет себя в конец очереди.
    /// </summary>
    public class SoftStopCommand : ICommand
    {
        private readonly ServerThread _serverThread;
        private readonly Action? _onStop;

        public SoftStopCommand(ServerThread serverThread, Action? onStop = null)
        {
            _serverThread = serverThread ?? throw new ArgumentNullException(nameof(serverThread));
            _onStop = onStop;
        }

        public void Execute()
        {
            // Проверка контекста: команда должна выполняться в потоке целевого ServerThread
            if (Thread.CurrentThread != _serverThread.GetThread())
                throw new InvalidOperationException("SoftStop выполняется вне контекста целевого потока.");

            if (_serverThread.GetQueue().Count == 0)
            {
                // Очередь пуста — останавливаем поток
                _serverThread.Stop();
                _onStop?.Invoke();
            }
            else
            {
                // Ещё есть команды — откладываем остановку в конец очереди
                _serverThread.GetQueue().Add(new SoftStopCommand(_serverThread, _onStop));
            }
        }
    }

    // ─── Управление жизненным циклом сервера ─────────────────────────────────

    /// <summary>
    /// Создаёт и запускает заданное количество ServerThread,
    /// назначая каждому уникальный строковый идентификатор.
    /// </summary>
    public class StartServerCommand : ICommand
    {
        private readonly int _threadCount;
        private readonly Dictionary<string, ServerThread> _threads = new();

        public StartServerCommand(int threadCount) => _threadCount = threadCount;
        public Dictionary<string, ServerThread> GetThreads() => _threads;

        public void Execute()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Запуск сервера...");
            for (int i = 0; i < _threadCount; i++)
            {
                string id = $"Thread-{i}";
                var st = new ServerThread(new BlockingCollection<ICommand>());
                _threads[id] = st;
                st.Start();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   {id} запущен.");
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Сервер запущен. Потоков: {_threadCount}.");
        }
    }

    /// <summary>Отправляет HardStop в каждый поток и ожидает его завершения.</summary>
    public class StopServerCommand : ICommand
    {
        private readonly Dictionary<string, ServerThread> _threads;
        public StopServerCommand(Dictionary<string, ServerThread> threads) => _threads = threads;

        public void Execute()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Остановка сервера...");
            foreach (var (id, thread) in _threads)
            {
                var evt = new ManualResetEventSlim(false);
                thread.GetQueue().Add(new ActionCommand(() =>
                {
                    thread.Stop();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   {id} остановлен.");
                    evt.Set();
                }));
                evt.Wait(TimeSpan.FromSeconds(10));
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Сервер остановлен.");
        }
    }
}
