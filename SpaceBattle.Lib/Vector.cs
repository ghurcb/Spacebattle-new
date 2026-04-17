namespace SpaceBattle.Lib
{
    public class Vector
    {
        public int X { get; }
        public int Y { get; }

        public Vector(int x, int y) { X = x; Y = y; }

        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y);

        public override bool Equals(object? obj) => obj is Vector v && X == v.X && Y == v.Y;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";
    }
}
