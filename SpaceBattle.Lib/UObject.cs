namespace SpaceBattle.Lib
{
    public class PropertyNotFoundException : Exception
    {
        public PropertyNotFoundException(string msg) : base(msg) { }
    }

    public class PropertyReadOnlyException : Exception
    {
        public PropertyReadOnlyException(string msg) : base(msg) { }
    }

    public interface IUObject
    {
        object GetProperty(string key);
        void SetProperty(string key, object value);
    }

    public class UObject : IUObject
    {
        private readonly Dictionary<string, object> _properties = new();

        public object GetProperty(string key)
        {
            if (!_properties.ContainsKey(key))
                throw new PropertyNotFoundException($"Свойство '{key}' не найдено.");
            return _properties[key];
        }

        public void SetProperty(string key, object value) => _properties[key] = value;
    }

    public interface IMovable
    {
        Vector Position { get; set; }
        Vector Velocity { get; }
    }

    public interface IRotatable
    {
        int Angle { get; set; }
        int AngularVelocity { get; }
    }

    public interface IShootable
    {
        Vector Position { get; }
        int Angle { get; }
    }

    public class MovableAdapter : IMovable
    {
        private readonly IUObject _obj;
        public MovableAdapter(IUObject obj) => _obj = obj;
        public Vector Position
        {
            get => (Vector)_obj.GetProperty("Position");
            set => _obj.SetProperty("Position", value);
        }
        public Vector Velocity => (Vector)_obj.GetProperty("Velocity");
    }

    public class RotatableAdapter : IRotatable
    {
        private readonly IUObject _obj;
        public RotatableAdapter(IUObject obj) => _obj = obj;
        public int Angle
        {
            get => (int)_obj.GetProperty("Angle");
            set => _obj.SetProperty("Angle", value);
        }
        public int AngularVelocity => (int)_obj.GetProperty("AngularVelocity");
    }
}
