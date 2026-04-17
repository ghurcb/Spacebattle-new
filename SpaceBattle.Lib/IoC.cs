using System.Collections.Concurrent;

namespace SpaceBattle.Lib
{
    public interface IScope
    {
        IScope? Parent { get; }
        bool TryResolve(string dependency, object[] args, out object? result);
        void Register(string dependency, Func<object[], object> strategy);
    }

    public class Scope : IScope
    {
        private readonly ConcurrentDictionary<string, Func<object[], object>> _strategies = new();
        public IScope? Parent { get; }
        public Scope(IScope? parent) => Parent = parent;

        public bool TryResolve(string dependency, object[] args, out object? result)
        {
            if (_strategies.TryGetValue(dependency, out var strategy))
            { result = strategy(args); return true; }
            result = null; return false;
        }

        public void Register(string dependency, Func<object[], object> strategy)
            => _strategies[dependency] = strategy;
    }

    public static class IoC
    {
        private static readonly ThreadLocal<IScope?> _currentScope = new(() => _rootScope);
        private static IScope _rootScope = new Scope(null);

        public static T Resolve<T>(string dependency, params object[] args)
        {
            var scope = _currentScope.Value;
            while (scope != null)
            {
                if (scope.TryResolve(dependency, args, out var result))
                    return (T)result!;
                scope = scope.Parent;
            }
            throw new InvalidOperationException($"Зависимость '{dependency}' не зарегистрирована.");
        }

        public static IScope GetCurrentScope() => _currentScope.Value ?? _rootScope;
        public static void SetCurrentScope(IScope scope) => _currentScope.Value = scope;
        public static void SetRootScope(IScope scope) => _rootScope = scope;
    }

    public class RegisterDependencyCommand : ICommand
    {
        private readonly string _dependency;
        private readonly Func<object[], object> _strategy;
        private readonly IScope _scope;

        public RegisterDependencyCommand(string dependency, Func<object[], object> strategy, IScope scope)
        { _dependency = dependency; _strategy = strategy; _scope = scope; }

        public void Execute() => _scope.Register(_dependency, _strategy);
    }

    /// <summary>
    /// Инициализирует корневой Scope со всеми базовыми и расширенными стратегиями:
    /// IoC.Register, Scopes.New, Scopes.Current.Set,
    /// ЛР №4: MacroCommand.Create, LongOperation.Create, IoC.ConflictResolver,
    /// ЛР №9: Object.Create (AutoWiring).
    /// </summary>
    public static class IoCInitializer
    {
        public static void Initialize()
        {
            var rootScope = new Scope(null);

            // ── Базовые стратегии ─────────────────────────────────────────
            rootScope.Register("IoC.Register", args =>
            {
                var dep      = (string)args[0];
                var strategy = (Func<object[], object>)args[1];
                return new RegisterDependencyCommand(dep, strategy, IoC.GetCurrentScope());
            });

            rootScope.Register("Scopes.New", args =>
            {
                var parent = args.Length > 0 ? (IScope)args[0] : IoC.GetCurrentScope();
                return new Scope(parent);
            });

            rootScope.Register("Scopes.Current.Set", args =>
            {
                var scope = (IScope)args[0];
                return new ActionCommand(() => IoC.SetCurrentScope(scope));
            });

            // ── ЛР №4: MacroCommand.Create ────────────────────────────────
            // args[0] = string[] — список имён зависимостей
            // возвращает MacroCommand из разрешённых ICommand
            rootScope.Register("MacroCommand.Create", args =>
            {
                var names = (IEnumerable<string>)args[0];
                var cmds  = names.Select(n => IoC.Resolve<ICommand>(n)).ToList();
                return new MacroCommand(cmds);
            });

            // ── ЛР №4: LongOperation.Create ──────────────────────────────
            // args[0] = string имя операции, args[1..] = параметры
            // создаёт BridgeCommand с начальной EmptyCommand
            rootScope.Register("LongOperation.Create", args =>
            {
                var bridge = new BridgeCommand(new EmptyCommand());
                if (args.Length > 0)
                {
                    var opName = (string)args[0];
                    try
                    {
                        var inner = IoC.Resolve<ICommand>(opName, args.Skip(1).ToArray());
                        bridge.Inject(inner);
                    }
                    catch (InvalidOperationException) { /* операция не найдена — стартуем с EmptyCommand */ }
                }
                return bridge;
            });

            // ── ЛР №4: IoC.ConflictResolver ──────────────────────────────
            // Вызывается при конфликте имён; по умолчанию разрешает через суффикс ".v2"
            rootScope.Register("IoC.ConflictResolver", args =>
            {
                var name      = (string)args[0];
                var resolved  = name + ".v2";
                return resolved;
            });

            // ── ЛР №9: Object.Create (AutoWiring) ────────────────────────
            // args[0] = полное или короткое имя типа
            rootScope.Register("Object.Create", args =>
            {
                var typeName = (string)args[0];
                var type = Type.GetType(typeName)
                           ?? AppDomain.CurrentDomain.GetAssemblies()
                               .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                               .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

                if (type == null)
                    throw new InvalidOperationException($"Тип '{typeName}' не найден.");

                var ctor = type.GetConstructors()
                               .OrderByDescending(c => c.GetParameters().Length)
                               .First();

                var ctorArgs = ctor.GetParameters().Select(p =>
                {
                    try { return IoC.Resolve<object>(p.ParameterType.Name); }
                    catch { return p.HasDefaultValue ? p.DefaultValue! : throw new InvalidOperationException($"Не удалось разрешить параметр '{p.Name}'."); }
                }).ToArray();

                return Activator.CreateInstance(type, ctorArgs)!;
            });

            // ── ЛР №8: Adapter.Create (через AdapterGenerator) ───────────
            // args[0] = имя интерфейса, args[1] = IUObject
            rootScope.Register("Adapter.Create", args =>
            {
                var ifaceName = (string)args[0];
                var target    = (IUObject)args[1];

                var iface = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.IsInterface && (t.Name == ifaceName || t.FullName == ifaceName));

                if (iface == null)
                    throw new InvalidOperationException($"Интерфейс '{ifaceName}' не найден.");

                return AdapterGenerator.CreateAdapter(iface, target);
            });

            IoC.SetRootScope(rootScope);
            IoC.SetCurrentScope(rootScope);
        }
    }
}
