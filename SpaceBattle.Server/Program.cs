using SpaceBattle.Lib;
using System.Collections.Concurrent;

namespace SpaceBattle.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Загрузка Roslyn из директории приложения (для работы с Roslyn без NuGet)
            AppDomain.CurrentDomain.AssemblyResolve += (_, ea) =>
            {
                var name = new System.Reflection.AssemblyName(ea.Name).Name;
                var dir  = AppDomain.CurrentDomain.BaseDirectory;
                var dll  = System.IO.Path.Combine(dir, name + ".dll");
                return System.IO.File.Exists(dll)
                    ? System.Reflection.Assembly.LoadFrom(dll) : null;
            };

            int    threadCount = args.Length > 0 && int.TryParse(args[0], out var tc) ? tc : 4;
            string configPath  = args.Length > 1 ? args[1] : "config.json";

            Console.WriteLine($"[{Now()}] ══════════════════════════════════════════");
            Console.WriteLine($"[{Now()}] SpaceBattle Server запускается...");
            Console.WriteLine($"[{Now()}] ══════════════════════════════════════════");

            // ── 1. IoC + AutoRegistrar ─────────────────────────────────────
            IoCInitializer.Initialize();
            AutoRegistrar.RegisterAll(IoC.GetCurrentScope());
            Console.WriteLine($"[{Now()}] IoC инициализирован, зависимости зарегистрированы.");

            // ── 2. Конфигурация ────────────────────────────────────────────
            GameConfiguration config;
            if (File.Exists(configPath))
            {
                config = ConfigurationLoader.LoadFromFile(configPath);
                Console.WriteLine($"[{Now()}] Конфигурация: {configPath}");
            }
            else
            {
                config = ConfigurationLoader.CreateDefault();
                ConfigurationLoader.SaveToFile(config, configPath);
                Console.WriteLine($"[{Now()}] Создана конфигурация по умолчанию: {configPath}");
            }

            // ── 3. Игровое пространство ────────────────────────────────────
            var gameSpace = BuildGameSpace(config);
            Console.WriteLine($"[{Now()}] Игровое пространство: {config.FieldWidth}×{config.FieldHeight}, объектов: {config.Ships.Count}");

            // ── 4. Регистрация обработчиков коллизий в IoC ────────────────
            RegisterCollisionHandlers();

            // ── 5. Серверные потоки ────────────────────────────────────────
            var startCmd = new StartServerCommand(threadCount);
            startCmd.Execute();
            var threads = startCmd.GetThreads();

            // ── 6. GameLifecycle: стратегии «Игра.Создать» / «Удалить» ────
            var lifecycle = new GameLifecycle(threads, config.TimeQuantumMs);
            lifecycle.RegisterStrategies(IoC.GetCurrentScope());
            Console.WriteLine($"[{Now()}] GameLifecycle зарегистрирован.");

            // ── 7. Создаём игру «game1» по умолчанию ──────────────────────
            var gameId = lifecycle.CreateGame(gameSpace, threads.Keys.First());
            // Делаем её доступной под фиксированным именем "game1" тоже
            var gameQueues = lifecycle.GetAllQueues();
            gameQueues["game1"] = lifecycle.GetQueue(gameId)!;
            Console.WriteLine($"[{Now()}] Игра создана: id={gameId} (также доступна как 'game1').");

            // ── 8. HTTP Endpoint ───────────────────────────────────────────
            const string prefix = "http://localhost:8080/";
            var endpoint = new GameEndpoint(prefix, gameSpace, threads, gameQueues);
            endpoint.Start();
            Console.WriteLine($"[{Now()}] HTTP Endpoint: {prefix}");
            Console.WriteLine($"[{Now()}] *** Визуализация: откройте {prefix} в браузере ***");

            // ── Информация ─────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine($"[{Now()}] ══════════════════════════════════════════");
            Console.WriteLine($"[{Now()}] Сервер готов. Потоков: {threadCount}, квант: {config.TimeQuantumMs} мс.");
            Console.WriteLine($"[{Now()}] ══════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("  GET  /state                — состояние пространства");
            Console.WriteLine("  GET  /                     — web-визуализатор");
            Console.WriteLine("  POST /  {JSON}             — отправить команду");
            Console.WriteLine();
            Console.WriteLine("  Пример: {\"type\":\"start_movement\",\"gameId\":\"game1\",\"gameItemId\":\"ship-1-1\",\"parameters\":{\"vx\":5,\"vy\":0}}");
            Console.WriteLine();
            Console.WriteLine("Нажмите Ctrl+C или любую клавишу для остановки.");

            // ── Ожидание ───────────────────────────────────────────────────
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(endpoint, threads, lifecycle, gameSpace, config);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Shutdown(endpoint, threads, lifecycle, gameSpace, config);
                Environment.Exit(0);
            };
            try { Console.ReadKey(true); }
            catch { Thread.Sleep(Timeout.Infinite); }

            Shutdown(endpoint, threads, lifecycle, gameSpace, config);
        }

        private static void Shutdown(
            GameEndpoint endpoint,
            Dictionary<string, ServerThread> threads,
            GameLifecycle lifecycle,
            GameSpace gameSpace,
            GameConfiguration config)
        {
            Console.WriteLine($"\n[{Now()}] Остановка...");
            endpoint.Stop();
            new StopServerCommand(threads).Execute();

            // Оценка результатов лабораторной работы
            if (config.Criteria.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"[{Now()}] ══ Оценка лабораторной работы ══");
                var eval   = new LabWorkEvaluator(gameSpace, config.Criteria);
                var result = eval.Evaluate();
                Console.WriteLine($"[{Now()}] Результат: {(result.Passed ? "✓ ЗАЧТЕНО" : "✗ НЕ ЗАЧТЕНО")}  ({result.Score:F1}%)");
                foreach (var d in result.Details)
                    Console.WriteLine($"  [{(d.Satisfied ? "+" : "-")}] {d.Name}: {d.Message}");
            }
            Console.WriteLine($"[{Now()}] Сервер остановлен.");
        }

        private static void RegisterCollisionHandlers()
        {
            // Spaceship + Projectile → урон кораблю, удалить снаряд
            IoC.Resolve<ICommand>("IoC.Register", "Collision.Spaceship.Projectile",
                (Func<object[], object>)(args =>
                {
                    var spaceship  = (IUObject)args[0];
                    var projectile = (IUObject)args[1];
                    var projId     = args.Length > 3 ? (string)args[3] : "";
                    // Используем GameSpace из IoC если возможно, иначе пропускаем
                    return new ActionCommand(() =>
                    {
                        // Снижаем здоровье корабля
                        try
                        {
                            var hp = spaceship.TryGetInt("Health", 100);
                            spaceship.SetProperty("Health", hp - 25);
                            Console.WriteLine($"[Collision] Корабль получил 25 урона. HP: {hp - 25}");
                        }
                        catch { /* Health не задан — игнорируем */ }
                        // Отмечаем снаряд для удаления
                        try { projectile.SetProperty("_destroy", true); } catch { }
                    });
                })).Execute();

            // Projectile + Spaceship (обратный порядок)
            IoC.Resolve<ICommand>("IoC.Register", "Collision.Projectile.Spaceship",
                (Func<object[], object>)(args =>
                {
                    // Меняем местами и делегируем
                    var reordered = new object[] { args[1], args[0], args[3], args[2] };
                    return IoC.Resolve<ICommand>("Collision.Spaceship.Projectile", reordered);
                })).Execute();
        }

        private static GameSpace BuildGameSpace(GameConfiguration config)
        {
            var gs = new GameSpace(config.FieldWidth, config.FieldHeight);
            foreach (var sc in config.Ships)
            {
                var ship = new UObject();
                ship.SetProperty("Type",            "Spaceship");
                ship.SetProperty("Position",        new Vector(sc.X, sc.Y));
                ship.SetProperty("Velocity",        new Vector(0, 0));
                ship.SetProperty("Angle",           sc.Angle);
                ship.SetProperty("AngularVelocity", 0);
                ship.SetProperty("Fuel",            config.InitialFuel);
                ship.SetProperty("Health",          100);
                ship.SetProperty("PlayerId",        sc.PlayerId);
                gs.AddObject(sc.Id, ship);
            }
            return gs;
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss");
    }

    // Удобный extension для чтения Int с дефолтом
    internal static class UObjectExt
    {
        public static int TryGetInt(this IUObject obj, string key, int def)
        {
            try { return (int)obj.GetProperty(key); }
            catch { return def; }
        }
    }
}
