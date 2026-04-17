using System.Text.Json;

namespace SpaceBattle.Lib
{
    public class GameConfiguration
    {
        public int FieldWidth { get; set; } = 800;
        public int FieldHeight { get; set; } = 600;
        public int PlayersCount { get; set; } = 2;
        public int ShipsPerPlayer { get; set; } = 3;
        public int InitialFuel { get; set; } = 100;
        public int TimeQuantumMs { get; set; } = 50;
        public List<ShipConfig> Ships { get; set; } = new();
        public List<EvaluationCriterion> Criteria { get; set; } = new();
    }

    public class ShipConfig
    {
        public string Id { get; set; } = "";
        public int PlayerId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Angle { get; set; }
    }

    public static class ConfigurationLoader
    {
        public static GameConfiguration LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GameConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new GameConfiguration();
        }

        public static GameConfiguration CreateDefault()
        {
            return new GameConfiguration
            {
                FieldWidth = 800,
                FieldHeight = 600,
                PlayersCount = 2,
                ShipsPerPlayer = 3,
                InitialFuel = 100,
                TimeQuantumMs = 50,
                Ships = new List<ShipConfig>
                {
                    new() { Id = "ship-1-1", PlayerId = 1, X = 100, Y = 150, Angle = 0 },
                    new() { Id = "ship-1-2", PlayerId = 1, X = 100, Y = 300, Angle = 0 },
                    new() { Id = "ship-1-3", PlayerId = 1, X = 100, Y = 450, Angle = 0 },
                    new() { Id = "ship-2-1", PlayerId = 2, X = 700, Y = 150, Angle = 180 },
                    new() { Id = "ship-2-2", PlayerId = 2, X = 700, Y = 300, Angle = 180 },
                    new() { Id = "ship-2-3", PlayerId = 2, X = 700, Y = 450, Angle = 180 }
                },
                Criteria = new List<EvaluationCriterion>
                {
                    new() { Name = "Корабль переместился", Type = "property_check", ObjectId = "ship-1-1", Property = "Position.X", Operator = "greater_than", ExpectedValue = 100, Weight = 1.0 }
                }
            };
        }

        public static void SaveToFile(GameConfiguration config, string path)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
