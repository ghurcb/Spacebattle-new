# SpaceBattle Server — ВКР ОмГТУ, группа ФИТ-211

Сервер для проверки лабораторных работ по курсу «ООАиП».
Реализует игровое пространство с космическими кораблями и снарядами,
обрабатывает управляющие команды по HTTP, соответствует принципам SOLID + IoC.

## Требования
- .NET 8.0 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
- Доступ к интернету для первого `dotnet restore`

## Быстрый старт

### Запуск сервера
```
cd SpaceBattle
dotnet run --project SpaceBattle.Server [кол-во_потоков] [путь_к_конфигу]
```
Пример:
```
dotnet run --project SpaceBattle.Server 4 config.json
```
После запуска откройте http://localhost:8080 — встроенный web-интерфейс с визуализацией.

### Запуск тестов (76 тестов, 100% покрытие)
```
dotnet run --project SpaceBattle.Tests
```

## Структура проекта

| Файл | Лабораторная | Что реализует |
|------|-------------|---------------|
| ICommand.cs | Все | Интерфейс ICommand, EmptyCommand, ActionCommand |
| Commands.cs | ЛР 1-3 | MoveCommand, RotateCommand, FireCommand, StartMoveCommand, EndMoveCommand, CollisionCommand, MacroCommand, BridgeCommand |
| ExceptionHandler.cs | ЛР 5 | Дерево решений, ExceptHandler<T> «кроме» |
| ServerThread.cs | ЛР 6 | ServerThread, HardStopCommand, SoftStopCommand, StartServerCommand, StopServerCommand |
| InterpretCommand.cs | ЛР 7 | Отдельный класс интерпретации (не inline в Endpoint) |
| Game.cs | ЛР 8 | Game:ICommand, квант времени, GameLifecycle (создать/удалить игру через IoC) |
| AdapterGenerator.cs | ЛР 9-10 | Reflection.Emit генерация адаптеров, AutoWiring конструкторов |
| AutoRegistrar.cs | ЛР 10 | [IoCAutoRegister], автосканирование сборки |
| IoC.cs | ЛР 4,8,9 | Scope, ThreadLocal, MacroCommand.Create, LongOperation.Create, IoC.ConflictResolver, Object.Create, Adapter.Create |
| Endpoint.cs | ЛР 7 | HTTP GET/state, POST /, game_id маршрутизация, встроенный UI |
| GameSpace.cs | ЛР 3 | GameSpace, GetSnapshot(), LabWorkEvaluator |
| UObject.cs | ЛР 1 | UObject, интерфейсы IMovable/IRotatable/IShootable, адаптеры |
| Vector.cs | ЛР 1 | Vector (2D, иммутабельный) |
| Configuration.cs | — | JSON конфигурация, GameConfiguration, ConfigurationLoader |

## HTTP API

| Метод | URL | Описание |
|-------|-----|----------|
| GET | / или /ui | Web-визуализатор |
| GET | /state | Состояние пространства (JSON) |
| POST | / | Отправить команду (JSON) |

### Формат команды (POST /)
```json
{ "type": "start_movement", "gameId": "game1", "gameItemId": "ship-1-1",
  "parameters": { "vx": 5, "vy": 3 } }
```
Поддерживаемые типы: `start_movement`, `stop_movement`, `rotate`, `move`, `fire`.

## Конфигурация (config.json)
```json
{
  "fieldWidth": 800, "fieldHeight": 600,
  "playersCount": 2, "shipsPerPlayer": 3,
  "timeQuantumMs": 50,
  "ships": [
    { "id": "ship-1-1", "playerId": 1, "x": 100, "y": 150, "angle": 0 }
  ],
  "criteria": [
    { "name": "Корабль переместился", "objectId": "ship-1-1",
      "property": "Position.X", "operator": "greater_than",
      "expectedValue": 100, "weight": 1.0 }
  ]
}
```
