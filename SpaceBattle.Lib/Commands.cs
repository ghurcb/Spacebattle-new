namespace SpaceBattle.Lib
{
    // ─── Базовые команды движения/поворота ───────────────────────────────────

    /// <summary>
    /// ЛР №1. Сдвигает объект: Position += Velocity.
    /// </summary>
    public class MoveCommand : ICommand
    {
        private readonly IMovable _target;
        public MoveCommand(IMovable target) => _target = target ?? throw new ArgumentNullException(nameof(target));
        public void Execute() => _target.Position = _target.Position + _target.Velocity;
    }

    /// <summary>
    /// ЛР №1. Поворачивает объект: Angle += AngularVelocity.
    /// </summary>
    public class RotateCommand : ICommand
    {
        private readonly IRotatable _target;
        public RotateCommand(IRotatable target) => _target = target ?? throw new ArgumentNullException(nameof(target));
        public void Execute() => _target.Angle = _target.Angle + _target.AngularVelocity;
    }

    // ─── ЛР №1. Выстрел ──────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт объект-торпеду в GameSpace с позицией и скоростью,
    /// рассчитанными по текущему углу стрелка.
    /// </summary>
    public class FireCommand : ICommand
    {
        private readonly IUObject _shooter;
        private readonly GameSpace _gameSpace;

        public FireCommand(IUObject shooter, GameSpace gameSpace)
        {
            _shooter = shooter ?? throw new ArgumentNullException(nameof(shooter));
            _gameSpace = gameSpace ?? throw new ArgumentNullException(nameof(gameSpace));
        }

        public void Execute()
        {
            var pos    = (Vector)_shooter.GetProperty("Position");
            var angle  = (int)_shooter.GetProperty("Angle");
            var rad    = angle * Math.PI / 180.0;
            var vx     = (int)(5 * Math.Cos(rad));
            var vy     = (int)(5 * Math.Sin(rad));

            var torpedo = new UObject();
            torpedo.SetProperty("Type",            "Projectile");
            torpedo.SetProperty("Position",        new Vector(pos.X, pos.Y));
            torpedo.SetProperty("Velocity",        new Vector(vx, vy));
            torpedo.SetProperty("Angle",           angle);
            torpedo.SetProperty("AngularVelocity", 0);

            _gameSpace.AddObject($"torpedo-{Guid.NewGuid():N}", torpedo);
        }
    }

    // ─── ЛР №2. Длительные операции (паттерн Bridge) ────────────────────────

    /// <summary>
    /// MacroCommand: выполняет список команд последовательно.
    /// </summary>
    public class MacroCommand : ICommand
    {
        private readonly List<ICommand> _commands;
        public MacroCommand(List<ICommand> commands) => _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        public void Execute() { foreach (var c in _commands) c.Execute(); }
    }

    /// <summary>
    /// BridgeCommand: делегирует вызов вложенной команде; команда может быть
    /// заменена «на лету» через Inject() без остановки потока.
    /// </summary>
    public class BridgeCommand : ICommand
    {
        private ICommand _inner;
        public BridgeCommand(ICommand inner) => _inner = inner ?? new EmptyCommand();
        public void Inject(ICommand other) => _inner = other ?? throw new ArgumentNullException(nameof(other));
        public void Execute() => _inner.Execute();
    }

    /// <summary>
    /// ЛР №2. Начало длительного движения.
    /// Устанавливает скорость объекта и внедряет MoveCommand в BridgeCommand,
    /// который уже находится в очереди ServerThread для циклического повторения.
    /// </summary>
    public class StartMoveCommand : ICommand
    {
        private readonly IUObject _obj;
        private readonly Vector _velocity;
        private readonly BridgeCommand _bridge;

        public StartMoveCommand(IUObject obj, Vector velocity, BridgeCommand bridge)
        {
            _obj      = obj      ?? throw new ArgumentNullException(nameof(obj));
            _bridge   = bridge   ?? throw new ArgumentNullException(nameof(bridge));
            _velocity = velocity;
        }

        public void Execute()
        {
            _obj.SetProperty("Velocity", _velocity);
            _bridge.Inject(new MoveCommand(new MovableAdapter(_obj)));
        }
    }

    /// <summary>
    /// ЛР №2. Окончание длительного движения.
    /// Заменяет MoveCommand внутри BridgeCommand на EmptyCommand —
    /// корабль останавливается без удаления Bridge из очереди.
    /// </summary>
    public class EndMoveCommand : ICommand
    {
        private readonly BridgeCommand _bridge;
        public EndMoveCommand(BridgeCommand bridge) => _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        public void Execute() => _bridge.Inject(new EmptyCommand());
    }

    // ─── ЛР №3. Коллизии ─────────────────────────────────────────────────────

    /// <summary>
    /// ЛР №3. Проверяет коллизии AABB всех объектов GameSpace на снимке состояния.
    /// Для каждой пары с пересечением разрешает обработчик через IoC по ключу
    /// "Collision.TypeA.TypeB" и выполняет его как MacroCommand.
    /// </summary>
    public class CollisionCommand : ICommand
    {
        private const int ObjectRadius = 10; // пикселей — настраивается через IoC
        private readonly GameSpace _gameSpace;

        public CollisionCommand(GameSpace gameSpace) =>
            _gameSpace = gameSpace ?? throw new ArgumentNullException(nameof(gameSpace));

        public void Execute()
        {
            var snapshot = _gameSpace.GetSnapshot();
            var ids      = snapshot.Keys.ToList();
            var handlers = new List<ICommand>();

            for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                var a = snapshot[ids[i]];
                var b = snapshot[ids[j]];
                if (!Intersects(a, b)) continue;

                var ta = GetTypeName(a);
                var tb = GetTypeName(b);
                // Попробуем оба порядка ключа
                foreach (var key in new[] { $"Collision.{ta}.{tb}", $"Collision.{tb}.{ta}" })
                {
                    try
                    {
                        handlers.Add(IoC.Resolve<ICommand>(key, a, b, ids[i], ids[j]));
                        break;
                    }
                    catch (InvalidOperationException) { /* обработчик не зарегистрирован */ }
                }
            }

            if (handlers.Count > 0)
                new MacroCommand(handlers).Execute();
        }

        private static bool Intersects(IUObject a, IUObject b)
        {
            try
            {
                var pa = (Vector)a.GetProperty("Position");
                var pb = (Vector)b.GetProperty("Position");
                var dx = pa.X - pb.X;
                var dy = pa.Y - pb.Y;
                return dx * dx + dy * dy <= (2 * ObjectRadius) * (2 * ObjectRadius);
            }
            catch { return false; }
        }

        private static string GetTypeName(IUObject obj)
        {
            try { return (string)obj.GetProperty("Type"); }
            catch { return "Unknown"; }
        }
    }
}
