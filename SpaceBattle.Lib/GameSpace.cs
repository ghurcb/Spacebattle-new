using System.Text.Json;

namespace SpaceBattle.Lib
{
    public class GameSpace
    {
        private readonly Dictionary<string, IUObject> _objects = new();
        public int Width  { get; }
        public int Height { get; }

        public GameSpace(int width, int height) { Width = width; Height = height; }

        public void AddObject(string id, IUObject obj) => _objects[id] = obj;
        public void RemoveObject(string id)             => _objects.Remove(id);

        public IUObject GetObject(string id)
            => _objects.TryGetValue(id, out var o)
               ? o
               : throw new KeyNotFoundException($"'{id}' не найден.");

        public IReadOnlyDictionary<string, IUObject> GetAllObjects() => _objects;

        /// <summary>
        /// ЛР №3. Возвращает снимок (shallow copy) словаря объектов.
        /// CollisionCommand работает на снимке, чтобы не обнаруживать
        /// одну коллизию дважды из-за изменения состояния в процессе обработки.
        /// </summary>
        public Dictionary<string, IUObject> GetSnapshot()
            => new Dictionary<string, IUObject>(_objects);

        public GameState GetState()
        {
            var state = new GameState { Width = Width, Height = Height, Objects = new() };
            foreach (var (id, obj) in _objects)
            {
                try
                {
                    var pos = (Vector)obj.GetProperty("Position");
                    state.Objects.Add(new ObjectState
                    {
                        Id    = id,
                        Type  = (string)obj.GetProperty("Type"),
                        X     = pos.X,
                        Y     = pos.Y,
                        Angle = (int)obj.GetProperty("Angle")
                    });
                }
                catch { /* пропускаем объекты без обязательных свойств */ }
            }
            return state;
        }
    }

    public class GameState
    {
        public int Width   { get; set; }
        public int Height  { get; set; }
        public List<ObjectState> Objects { get; set; } = new();
        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public class ObjectState
    {
        public string Id    { get; set; } = "";
        public string Type  { get; set; } = "";
        public int X        { get; set; }
        public int Y        { get; set; }
        public int Angle    { get; set; }
    }

    public class EvaluationResult
    {
        public bool   Passed  { get; set; }
        public double Score   { get; set; }
        public List<CriterionResult> Details { get; set; } = new();
    }

    public class CriterionResult
    {
        public string Name      { get; set; } = "";
        public bool   Satisfied { get; set; }
        public string Message   { get; set; } = "";
    }

    public class EvaluationCriterion
    {
        public string Name          { get; set; } = "";
        public string Type          { get; set; } = "";
        public string ObjectId      { get; set; } = "";
        public string Property      { get; set; } = "";
        public string Operator      { get; set; } = "";
        public double ExpectedValue { get; set; }
        public double Weight        { get; set; } = 1.0;
    }

    public class LabWorkEvaluator
    {
        private readonly GameSpace _gameSpace;
        private readonly List<EvaluationCriterion> _criteria;

        public LabWorkEvaluator(GameSpace gs, List<EvaluationCriterion> criteria)
        { _gameSpace = gs; _criteria = criteria; }

        public EvaluationResult Evaluate()
        {
            var result = new EvaluationResult();
            double total = 0, achieved = 0;
            foreach (var c in _criteria)
            {
                total += c.Weight;
                var cr = Check(c);
                result.Details.Add(cr);
                if (cr.Satisfied) achieved += c.Weight;
            }
            result.Score  = total > 0 ? achieved / total * 100 : 0;
            result.Passed = result.Score >= 60;
            return result;
        }

        private CriterionResult Check(EvaluationCriterion c)
        {
            try
            {
                var obj = _gameSpace.GetObject(c.ObjectId);
                var val = GetVal(obj, c.Property);
                bool ok = c.Operator switch
                {
                    "equals"       => Math.Abs(val - c.ExpectedValue) < 0.001,
                    "greater_than" => val > c.ExpectedValue,
                    "less_than"    => val < c.ExpectedValue,
                    "not_equals"   => Math.Abs(val - c.ExpectedValue) >= 0.001,
                    _              => false
                };
                return new CriterionResult
                {
                    Name      = c.Name,
                    Satisfied = ok,
                    Message   = ok ? "OK" : $"Ожидалось: {c.Operator} {c.ExpectedValue}, получено: {val}"
                };
            }
            catch (Exception ex)
            { return new CriterionResult { Name = c.Name, Satisfied = false, Message = ex.Message }; }
        }

        private static double GetVal(IUObject obj, string prop)
        {
            var parts = prop.Split('.');
            if (parts.Length == 2 && parts[0] == "Position")
            {
                var p = (Vector)obj.GetProperty("Position");
                return parts[1] == "X" ? p.X : p.Y;
            }
            return Convert.ToDouble(obj.GetProperty(prop));
        }
    }
}
