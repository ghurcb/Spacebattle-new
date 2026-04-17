using System.Reflection;

namespace SpaceBattle.Lib
{
    // ─── ЛР №10. Автоматическая регистрация в IoC ───────────────────────────

    /// <summary>
    /// Помечает класс для автоматической регистрации в IoC-контейнере.
    /// Класс должен иметь конструктор без параметров или с параметрами,
    /// разрешаемыми через IoC.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class IoCAutoRegisterAttribute : Attribute
    {
        /// <summary>Имя зависимости в IoC.</summary>
        public string DependencyName { get; }
        public IoCAutoRegisterAttribute(string dependencyName) => DependencyName = dependencyName;
    }

    /// <summary>
    /// ЛР №10. Сканирует все типы в сборке SpaceBattle.Lib (и переданных сборках),
    /// находит классы с [IoCAutoRegister] и регистрирует их стратегии в переданном Scope.
    /// Вызывается один раз при старте — явных вызовов IoC.Register не требуется.
    /// </summary>
    public static class AutoRegistrar
    {
        /// <summary>
        /// Регистрирует все помеченные классы в указанном Scope.
        /// Сканирует сборку Lib и все переданные дополнительные сборки.
        /// </summary>
        public static void RegisterAll(IScope scope, params Assembly[] extraAssemblies)
        {
            var assemblies = new[] { typeof(AutoRegistrar).Assembly }
                .Concat(extraAssemblies)
                .Distinct();

            foreach (var asm in assemblies)
            {
                IEnumerable<Type> types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    var attr = type.GetCustomAttribute<IoCAutoRegisterAttribute>();
                    if (attr == null) continue;

                    var captured = type; // захват для лямбды
                    scope.Register(attr.DependencyName, args =>
                        Activator.CreateInstance(captured, args) ?? throw new InvalidOperationException(
                            $"Не удалось создать экземпляр {captured.Name}."));
                }
            }
        }
    }
}
