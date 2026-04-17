namespace SpaceBattle.Lib
{
    // ─── ЛР №5. Обработка исключений через дерево решений ───────────────────

    /// <summary>
    /// Ищет и применяет обработчик исключения по дереву решений:
    ///   первый уровень — иерархия типов команды,
    ///   второй уровень — иерархия типов исключения.
    /// Ключ в IoC: "Exception.{CommandTypeName}.{ExceptionTypeName}".
    /// </summary>
    public static class ExceptionHandler
    {
        /// <summary>
        /// Возвращает команду-обработчик или null, если подходящего нет.
        /// </summary>
        public static ICommand? Find(ICommand cmd, Exception ex)
        {
            foreach (var cmdType in GetTypeHierarchy(cmd.GetType()))
            foreach (var exType  in GetTypeHierarchy(ex.GetType()))
            {
                var key = $"Exception.{cmdType.Name}.{exType.Name}";
                try { return IoC.Resolve<ICommand>(key, cmd, ex); }
                catch (InvalidOperationException) { /* пробуем следующий */ }
            }
            return null;
        }

        /// <summary>
        /// Регистрирует обработчик в текущем Scope.
        /// </summary>
        public static void Register<TCmd, TEx>(Func<ICommand, Exception, ICommand> factory)
            where TCmd : ICommand
            where TEx  : Exception
        {
            IoC.Resolve<ICommand>(
                "IoC.Register",
                $"Exception.{typeof(TCmd).Name}.{typeof(TEx).Name}",
                (Func<object[], object>)(args => factory((ICommand)args[0], (Exception)args[1])))
            .Execute();
        }

        private static IEnumerable<Type> GetTypeHierarchy(Type t)
        {
            // Сначала конкретный тип, затем база, затем интерфейсы
            var current = t;
            while (current != null && current != typeof(object))
            {
                yield return current;
                current = current.BaseType;
            }
            foreach (var iface in t.GetInterfaces())
                yield return iface;
        }
    }

    // ─── Декоратор «кроме» ───────────────────────────────────────────────────

    /// <summary>
    /// ЛР №5. Обёртка над другим обработчиком: пропускает выполнение,
    /// если тип исключения совпадает или является подтипом TException.
    /// Позволяет строить правила вида «для всех, кроме NetworkException».
    /// </summary>
    public class ExceptHandler<TException> : ICommand
        where TException : Exception
    {
        private readonly ICommand _inner;
        private readonly ICommand _cmd;
        private readonly Exception _ex;

        public ExceptHandler(ICommand inner, ICommand cmd, Exception ex)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cmd   = cmd;
            _ex    = ex;
        }

        public void Execute()
        {
            if (_ex is TException) return; // «кроме» — пропускаем
            _inner.Execute();
        }
    }
}
