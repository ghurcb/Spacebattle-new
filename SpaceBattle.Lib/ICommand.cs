namespace SpaceBattle.Lib
{
    /// <summary>Базовый интерфейс команды (паттерн «Команда»).</summary>
    public interface ICommand
    {
        void Execute();
    }

    /// <summary>Команда-заглушка; ничего не делает (используется в EndMoveCommand).</summary>
    public class EmptyCommand : ICommand
    {
        public void Execute() { }
    }

    /// <summary>Оборачивает произвольный Action в ICommand.</summary>
    public class ActionCommand : ICommand
    {
        private readonly Action _action;
        public ActionCommand(Action action) => _action = action ?? throw new ArgumentNullException(nameof(action));
        public void Execute() => _action();
    }
}
