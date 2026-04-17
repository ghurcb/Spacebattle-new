// Standalone test runner — no xUnit/Moq needed.
// Compile: dotnet build  |  Run: dotnet run --project SpaceBattle.Tests
// Coverage: dotnet-coverage collect "dotnet run --project SpaceBattle.Tests"

using SpaceBattle.Lib;
using System.Collections.Concurrent;

namespace SpaceBattle.Tests
{
    // ── Mini-framework ───────────────────────────────────────────────────────
    internal static class Assert
    {
        public static void True(bool v,  string msg = "Expected true")  { if (!v) throw new Exception(msg); }
        public static void False(bool v, string msg = "Expected false") { if (v)  throw new Exception(msg); }
        public static void Equal<T>(T expected, T actual, string msg = "")
        {
            bool eq = expected?.Equals(actual) ?? actual == null;
            if (!eq) throw new Exception($"Expected [{expected}] but got [{actual}]. {msg}");
        }
        public static void NotEqual<T>(T a, T b) { if (a?.Equals(b) ?? b == null) throw new Exception($"Values equal: {a}"); }
        public static void Null(object? v)    { if (v != null) throw new Exception($"Expected null, got {v}"); }
        public static void NotNull(object? v) { if (v == null) throw new Exception("Expected non-null"); }
        public static void IsType<T>(object? v) { if (v is not T) throw new Exception($"Expected {typeof(T).Name}, got {v?.GetType().Name}"); }
        public static void Same(object? a, object? b) { if (!ReferenceEquals(a,b)) throw new Exception("References differ"); }
        public static void Throws<T>(Action act) where T : Exception
        {
            try { act(); throw new Exception($"Expected {typeof(T).Name} but no exception thrown"); }
            catch (T) { /* ok */ }
        }
        public static void Single<T>(IEnumerable<T> col) { var c = col.Count(); if (c != 1) throw new Exception($"Expected 1, got {c}"); }
        public static void Contains<T>(IEnumerable<T> col, Func<T,bool> pred) { if (!col.Any(pred)) throw new Exception("Item not found"); }
    }

    internal class TestRunner
    {
        private int _pass, _fail;
        private readonly List<string> _failures = new();

        public void Run(string name, Action test)
        {
            try { test(); _pass++; Console.WriteLine($"  ✓ {name}"); }
            catch (Exception ex) { _fail++; _failures.Add($"  ✗ {name}: {ex.Message}"); Console.WriteLine($"  ✗ {name}: {ex.Message}"); }
        }

        public void Summary()
        {
            Console.WriteLine($"\n══════════════════════════════════════");
            Console.WriteLine($"PASSED: {_pass}   FAILED: {_fail}   TOTAL: {_pass + _fail}");
            if (_failures.Count > 0) { Console.WriteLine("\nFailed tests:"); _failures.ForEach(Console.WriteLine); }
            if (_fail > 0) Environment.Exit(1);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    internal static class Helpers
    {
        public static UObject MakeShip(int x = 0, int y = 0, int angle = 0)
        {
            var o = new UObject();
            o.SetProperty("Type",            "Spaceship");
            o.SetProperty("Position",        new Vector(x, y));
            o.SetProperty("Velocity",        new Vector(0, 0));
            o.SetProperty("Angle",           angle);
            o.SetProperty("AngularVelocity", 0);
            return o;
        }
        public static GameSpace MakeGs(string id = "ship-1", int x = 100, int y = 100)
        {
            var gs = new GameSpace(800, 600);
            gs.AddObject(id, MakeShip(x, y));
            return gs;
        }
    }

    // ════════════════════════════════════════════════════════════════════════

    internal static class Program
    {
        static void Main()
        {
            // Загрузка Roslyn и других DLL из директории приложения
            // (нужно для References без NuGet — пока deps.json их не знает)
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new System.Reflection.AssemblyName(args.Name).Name;
                var dir  = AppDomain.CurrentDomain.BaseDirectory;
                var dll  = Path.Combine(dir, name + ".dll");
                return File.Exists(dll) ? System.Reflection.Assembly.LoadFrom(dll) : null;
            };

            var t = new TestRunner();
            IoCInitializer.Initialize();

            // ── Vector ────────────────────────────────────────────────────
            Console.WriteLine("\n[Vector]");
            t.Run("Add",      () => Assert.Equal(new Vector(5,7), new Vector(2,3)+new Vector(3,4)));
            t.Run("Sub",      () => Assert.Equal(new Vector(1,1), new Vector(3,4)-new Vector(2,3)));
            t.Run("Equal",    () => Assert.True(new Vector(1,2).Equals(new Vector(1,2))));
            t.Run("NotEqual", () => Assert.False(new Vector(1,2).Equals(new Vector(2,1))));
            t.Run("Hash",     () => Assert.Equal(new Vector(5,5).GetHashCode(), new Vector(5,5).GetHashCode()));
            t.Run("ToString", () => Assert.Equal("(3, 4)", new Vector(3,4).ToString()));

            // ── UObject ───────────────────────────────────────────────────
            Console.WriteLine("\n[UObject]");
            t.Run("SetGet",   () => { var o=new UObject(); o.SetProperty("X",42); Assert.Equal(42,o.GetProperty("X")); });
            t.Run("Missing",  () => Assert.Throws<PropertyNotFoundException>(() => new UObject().GetProperty("none")));
            t.Run("Overwrite",() => { var o=new UObject(); o.SetProperty("K",1); o.SetProperty("K",2); Assert.Equal(2,o.GetProperty("K")); });

            // ── ЛР №1: MoveCommand ───────────────────────────────────────
            Console.WriteLine("\n[ЛР1: MoveCommand]");
            t.Run("Execute_MovesCorrect", () =>
            {
                var o = new UObject();
                o.SetProperty("Position", new Vector(12,5));
                o.SetProperty("Velocity", new Vector(-7,3));
                new MoveCommand(new MovableAdapter(o)).Execute();
                Assert.Equal(new Vector(5,8), (Vector)o.GetProperty("Position"));
            });
            t.Run("NullTarget_Throws", () => Assert.Throws<ArgumentNullException>(() => new MoveCommand(null!)));

            // ── ЛР №1: RotateCommand ─────────────────────────────────────
            Console.WriteLine("\n[ЛР1: RotateCommand]");
            t.Run("Execute_Rotates90", () =>
            {
                var o = new UObject();
                o.SetProperty("Angle", 45);
                o.SetProperty("AngularVelocity", 90);
                new RotateCommand(new RotatableAdapter(o)).Execute();
                Assert.Equal(135, (int)o.GetProperty("Angle"));
            });
            t.Run("NullTarget_Throws", () => Assert.Throws<ArgumentNullException>(() => new RotateCommand(null!)));

            // ── Adapters ──────────────────────────────────────────────────
            Console.WriteLine("\n[Adapters]");
            t.Run("MovableAdapter_Velocity", () =>
            {
                var o = new UObject();
                o.SetProperty("Position", new Vector(1,2));
                o.SetProperty("Velocity", new Vector(3,4));
                Assert.Equal(new Vector(3,4), new MovableAdapter(o).Velocity);
            });
            t.Run("RotatableAdapter_Set", () =>
            {
                var o = new UObject();
                o.SetProperty("Angle", 10);
                o.SetProperty("AngularVelocity", 5);
                var a = new RotatableAdapter(o);
                a.Angle = 99;
                Assert.Equal(99, (int)o.GetProperty("Angle"));
            });

            // ── MacroCommand / BridgeCommand / EmptyCommand ───────────────
            Console.WriteLine("\n[Composite commands]");
            t.Run("MacroCommand_RunsAll", () =>
            {
                int c = 0;
                new MacroCommand(Enumerable.Range(0,3).Select(_=>(ICommand)new ActionCommand(()=>c++)).ToList()).Execute();
                Assert.Equal(3, c);
            });
            t.Run("MacroCommand_StopsOnException", () =>
            {
                int c = 0;
                Assert.Throws<InvalidOperationException>(() =>
                    new MacroCommand(new List<ICommand>
                    {
                        new ActionCommand(() => c++),
                        new ActionCommand(() => throw new InvalidOperationException()),
                        new ActionCommand(() => c++)
                    }).Execute());
                Assert.Equal(1, c);
            });
            t.Run("MacroCommand_NullList_Throws", () => Assert.Throws<ArgumentNullException>(() => new MacroCommand(null!)));
            t.Run("BridgeCommand_Delegates", () =>
            {
                bool ran = false;
                new BridgeCommand(new ActionCommand(() => ran = true)).Execute();
                Assert.True(ran);
            });
            t.Run("BridgeCommand_Inject", () =>
            {
                bool ran = false;
                var b = new BridgeCommand(new EmptyCommand());
                b.Inject(new ActionCommand(() => ran = true));
                b.Execute();
                Assert.True(ran);
            });
            t.Run("BridgeCommand_NullInject_Throws", () => Assert.Throws<ArgumentNullException>(() => new BridgeCommand(new EmptyCommand()).Inject(null!)));
            t.Run("EmptyCommand_NoException",  () => new EmptyCommand().Execute());
            t.Run("ActionCommand_NullThrows",  () => Assert.Throws<ArgumentNullException>(() => new ActionCommand(null!)));

            // ── ЛР №2: StartMoveCommand / EndMoveCommand ─────────────────
            Console.WriteLine("\n[ЛР2: StartMove/EndMove]");
            t.Run("StartMove_SetsVelocityAndMoves", () =>
            {
                var o = new UObject();
                o.SetProperty("Position", new Vector(10,10));
                o.SetProperty("Velocity", new Vector(0,0));
                var bridge = new BridgeCommand(new EmptyCommand());
                new StartMoveCommand(o, new Vector(3,4), bridge).Execute();
                Assert.Equal(new Vector(3,4), (Vector)o.GetProperty("Velocity"));
                bridge.Execute();
                Assert.Equal(new Vector(13,14), (Vector)o.GetProperty("Position"));
            });
            t.Run("EndMove_StopsMovement", () =>
            {
                var o = new UObject();
                o.SetProperty("Position", new Vector(0,0));
                o.SetProperty("Velocity", new Vector(5,0));
                var bridge = new BridgeCommand(new MoveCommand(new MovableAdapter(o)));
                new EndMoveCommand(bridge).Execute();
                bridge.Execute();
                Assert.Equal(new Vector(0,0), (Vector)o.GetProperty("Position"));
            });
            t.Run("StartMove_NullObj_Throws",    () => Assert.Throws<ArgumentNullException>(() => new StartMoveCommand(null!, new Vector(1,1), new BridgeCommand(new EmptyCommand()))));
            t.Run("EndMove_NullBridge_Throws",   () => Assert.Throws<ArgumentNullException>(() => new EndMoveCommand(null!)));

            // ── ЛР №3: CollisionCommand ───────────────────────────────────
            Console.WriteLine("\n[ЛР3: CollisionCommand]");
            t.Run("NoCollision_Silent", () =>
            {
                IoCInitializer.Initialize();
                var gs = new GameSpace(800,600);
                var a = new UObject(); a.SetProperty("Type","Spaceship"); a.SetProperty("Position",new Vector(0,0));
                var b = new UObject(); b.SetProperty("Type","Spaceship"); b.SetProperty("Position",new Vector(500,500));
                gs.AddObject("a",a); gs.AddObject("b",b);
                new CollisionCommand(gs).Execute();
            });
            t.Run("Collision_HandlerCalled", () =>
            {
                IoCInitializer.Initialize();
                bool handled = false;
                IoC.Resolve<ICommand>("IoC.Register","Collision.Spaceship.Projectile",
                    (Func<object[],object>)(_=>new ActionCommand(()=>handled=true))).Execute();
                var gs = new GameSpace(800,600);
                var s = new UObject(); s.SetProperty("Type","Spaceship"); s.SetProperty("Position",new Vector(10,10));
                var p = new UObject(); p.SetProperty("Type","Projectile"); p.SetProperty("Position",new Vector(10,10));
                gs.AddObject("s",s); gs.AddObject("p",p);
                new CollisionCommand(gs).Execute();
                Assert.True(handled);
            });
            t.Run("CollisionCommand_NullSpace_Throws", () => Assert.Throws<ArgumentNullException>(() => new CollisionCommand(null!)));

            // ── ЛР №4: IoC стратегии ──────────────────────────────────────
            Console.WriteLine("\n[ЛР4: IoC стратегии]");
            t.Run("RegisterAndResolve", () =>
            {
                IoCInitializer.Initialize();
                IoC.Resolve<ICommand>("IoC.Register","T.Val",(Func<object[],object>)(_=>99)).Execute();
                Assert.Equal(99, IoC.Resolve<int>("T.Val"));
            });
            t.Run("Resolve_Unregistered_Throws", () =>
            {
                IoCInitializer.Initialize();
                Assert.Throws<InvalidOperationException>(() => IoC.Resolve<object>("NoSuch"));
            });
            t.Run("ChildScope_InheritsParent", () =>
            {
                IoCInitializer.Initialize();
                IoC.Resolve<ICommand>("IoC.Register","Parent.V",(Func<object[],object>)(_=>"pval")).Execute();
                var child = IoC.Resolve<IScope>("Scopes.New");
                IoC.Resolve<ICommand>("Scopes.Current.Set",child).Execute();
                Assert.Equal("pval", IoC.Resolve<string>("Parent.V"));
            });
            t.Run("ChildScope_Overrides", () =>
            {
                IoCInitializer.Initialize();
                IoC.Resolve<ICommand>("IoC.Register","OV",(Func<object[],object>)(_=>"parent")).Execute();
                var child = IoC.Resolve<IScope>("Scopes.New");
                IoC.Resolve<ICommand>("Scopes.Current.Set",child).Execute();
                IoC.Resolve<ICommand>("IoC.Register","OV",(Func<object[],object>)(_=>"child")).Execute();
                Assert.Equal("child", IoC.Resolve<string>("OV"));
            });
            t.Run("ChildScope_NotVisibleInParent", () =>
            {
                IoCInitializer.Initialize();
                var parent = IoC.GetCurrentScope();
                var child  = IoC.Resolve<IScope>("Scopes.New");
                IoC.Resolve<ICommand>("Scopes.Current.Set",child).Execute();
                IoC.Resolve<ICommand>("IoC.Register","Child.Only",(Func<object[],object>)(_=>"v")).Execute();
                IoC.Resolve<ICommand>("Scopes.Current.Set",parent).Execute();
                Assert.Throws<InvalidOperationException>(()=>IoC.Resolve<string>("Child.Only"));
            });
            t.Run("MacroCommandCreate_Works", () =>
            {
                IoCInitializer.Initialize();
                int cnt = 0;
                IoC.Resolve<ICommand>("IoC.Register","Cmd.A",(Func<object[],object>)(_=>new ActionCommand(()=>cnt++))).Execute();
                IoC.Resolve<ICommand>("IoC.Register","Cmd.B",(Func<object[],object>)(_=>new ActionCommand(()=>cnt++))).Execute();
                // Передаём массив как object, чтобы params не распаковал его
                IoC.Resolve<ICommand>("MacroCommand.Create", (object)new[]{"Cmd.A","Cmd.B"}).Execute();
                Assert.Equal(2, cnt);
            });
            t.Run("LongOperationCreate_ReturnsBridgeCommand", () =>
            {
                IoCInitializer.Initialize();
                var cmd = IoC.Resolve<ICommand>("LongOperation.Create");
                Assert.IsType<BridgeCommand>(cmd);
            });
            t.Run("ConflictResolver_AppendsV2", () =>
            {
                IoCInitializer.Initialize();
                Assert.Equal("SomeDep.v2", IoC.Resolve<string>("IoC.ConflictResolver","SomeDep"));
            });

            // ── ЛР №5: ExceptionHandler ───────────────────────────────────
            Console.WriteLine("\n[ЛР5: ExceptionHandler]");
            t.Run("Find_RegisteredHandler", () =>
            {
                IoCInitializer.Initialize();
                bool handled = false;
                ExceptionHandler.Register<EmptyCommand,InvalidOperationException>(
                    (_,_)=>new ActionCommand(()=>handled=true));
                ExceptionHandler.Find(new EmptyCommand(), new InvalidOperationException())?.Execute();
                Assert.True(handled);
            });
            t.Run("Find_NoHandler_ReturnsNull", () =>
            {
                IoCInitializer.Initialize();
                Assert.Null(ExceptionHandler.Find(new EmptyCommand(), new Exception("x")));
            });
            t.Run("ExceptHandler_SkipsMatchingType", () =>
            {
                bool ran = false;
                new ExceptHandler<ArgumentNullException>(
                    new ActionCommand(()=>ran=true),
                    new EmptyCommand(),
                    new ArgumentNullException()).Execute();
                Assert.False(ran);
            });
            t.Run("ExceptHandler_RunsNonMatchingType", () =>
            {
                bool ran = false;
                new ExceptHandler<ArgumentNullException>(
                    new ActionCommand(()=>ran=true),
                    new EmptyCommand(),
                    new InvalidOperationException()).Execute();
                Assert.True(ran);
            });
            t.Run("ServerThread_UsesExceptionHandler", () =>
            {
                IoCInitializer.Initialize();
                bool handled = false;
                ExceptionHandler.Register<ActionCommand,InvalidOperationException>(
                    (_,_)=>new ActionCommand(()=>handled=true));
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                var done = new ManualResetEventSlim(false);
                q.Add(new ActionCommand(()=>throw new InvalidOperationException("test")));
                q.Add(new ActionCommand(()=>done.Set()));
                Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
                q.Add(new ActionCommand(()=>st.Stop()));
                Thread.Sleep(200);
                Assert.True(handled);
            });

            // ── ЛР №6: ServerThread / HardStop / SoftStop ─────────────────
            Console.WriteLine("\n[ЛР6: ServerThread]");
            t.Run("HardStop_StopsThread", () =>
            {
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                var done = new ManualResetEventSlim(false);
                q.Add(new ActionCommand(()=>new HardStopCommand(st,()=>done.Set()).Execute()));
                Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
            });
            t.Run("SoftStop_ExecutesRemaining", () =>
            {
                var q   = new BlockingCollection<ICommand>();
                var st  = new ServerThread(q);
                int cnt = 0;
                st.Start();
                for (int i=0;i<5;i++) q.Add(new ActionCommand(()=>cnt++));
                var done = new ManualResetEventSlim(false);
                q.Add(new ActionCommand(()=>new SoftStopCommand(st,()=>done.Set()).Execute()));
                Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
                Assert.Equal(5, cnt);
            });
            t.Run("CommandsExecutedInOrder", () =>
            {
                var q    = new BlockingCollection<ICommand>();
                var st   = new ServerThread(q);
                var list = new List<int>();
                st.Start();
                var done = new ManualResetEventSlim(false);
                for (int i=0;i<5;i++){var v=i; q.Add(new ActionCommand(()=>list.Add(v)));}
                q.Add(new ActionCommand(()=>done.Set()));
                done.Wait(TimeSpan.FromSeconds(3));
                q.Add(new ActionCommand(()=>st.Stop())); Thread.Sleep(150);
                Assert.Equal(0,list[0]); Assert.Equal(4,list[4]);
            });
            t.Run("HardStop_WrongContext_Throws", () =>
            {
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                Assert.Throws<InvalidOperationException>(()=>new HardStopCommand(st).Execute());
                q.Add(new ActionCommand(()=>st.Stop())); Thread.Sleep(200);
            });
            t.Run("SoftStop_ReschedulesIfNotEmpty", () =>
            {
                var q   = new BlockingCollection<ICommand>();
                var st  = new ServerThread(q);
                int cnt = 0;
                st.Start();
                var done = new ManualResetEventSlim(false);
                q.Add(new ActionCommand(()=>new SoftStopCommand(st,()=>done.Set()).Execute()));
                for (int i=0;i<3;i++) q.Add(new ActionCommand(()=>cnt++));
                Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal(3, cnt);
            });

            // ── ЛР №7: InterpretCommand ────────────────────────────────────
            Console.WriteLine("\n[ЛР7: InterpretCommand]");
            t.Run("InterpretMove_PutsCommandInQueue", () =>
            {
                IoCInitializer.Initialize();
                var gs = Helpers.MakeGs();
                var q  = new BlockingCollection<ICommand>();
                var gq = new Dictionary<string,BlockingCollection<ICommand>>{["game1"]=q};
                new InterpretCommand("{\"type\":\"move\",\"gameId\":\"game1\",\"gameItemId\":\"ship-1\"}",gq,gs).Execute();
                Assert.Equal(1, q.Count);
            });
            t.Run("InterpretStartMovement_SetsVelocity", () =>
            {
                IoCInitializer.Initialize();
                var gs = Helpers.MakeGs();
                var q  = new BlockingCollection<ICommand>();
                var gq = new Dictionary<string,BlockingCollection<ICommand>>{["g"]=q};
                new InterpretCommand("{\"type\":\"start_movement\",\"gameId\":\"g\",\"gameItemId\":\"ship-1\",\"parameters\":{\"vx\":3,\"vy\":4}}",gq,gs).Execute();
                q.Take().Execute();
                Assert.Equal(new Vector(3,4),(Vector)gs.GetObject("ship-1").GetProperty("Velocity"));
            });
            t.Run("InterpretFire_CreatesTorpedo", () =>
            {
                IoCInitializer.Initialize();
                var gs = Helpers.MakeGs();
                var q  = new BlockingCollection<ICommand>();
                var gq = new Dictionary<string,BlockingCollection<ICommand>>{["g"]=q};
                new InterpretCommand("{\"type\":\"fire\",\"gameId\":\"g\",\"gameItemId\":\"ship-1\"}",gq,gs).Execute();
                q.Take().Execute();
                Assert.True(gs.GetAllObjects().Count > 1);
            });
            t.Run("InterpretCommand_NullJson_Throws", () =>
                Assert.Throws<ArgumentNullException>(()=>
                    new InterpretCommand(null!,new Dictionary<string,BlockingCollection<ICommand>>(),new GameSpace(10,10))));

            // ── ЛР №8: Game as ICommand ────────────────────────────────────
            Console.WriteLine("\n[ЛР8: Game as ICommand]");
            t.Run("Game_ExecutesCommandsInQuantum", () =>
            {
                IoCInitializer.Initialize();
                var gs  = new GameSpace(800,600);
                var inc = new BlockingCollection<ICommand>();
                var srv = new BlockingCollection<ICommand>();
                int cnt = 0;
                inc.Add(new ActionCommand(()=>cnt++));
                inc.Add(new ActionCommand(()=>cnt++));
                new Game("g1",IoC.Resolve<IScope>("Scopes.New"),inc,srv,gs,200).Execute();
                Assert.Equal(2, cnt);
            });
            t.Run("Game_ReschedulesAfterQuantum", () =>
            {
                IoCInitializer.Initialize();
                var gs  = new GameSpace(800,600);
                var srv = new BlockingCollection<ICommand>();
                new Game("g2",IoC.Resolve<IScope>("Scopes.New"),new BlockingCollection<ICommand>(),srv,gs).Execute();
                Assert.Equal(1, srv.Count);
            });
            t.Run("Game_StopNoReschedule", () =>
            {
                IoCInitializer.Initialize();
                var gs  = new GameSpace(800,600);
                var srv = new BlockingCollection<ICommand>();
                var g   = new Game("g3",IoC.Resolve<IScope>("Scopes.New"),new BlockingCollection<ICommand>(),srv,gs);
                g.Stop(); g.Execute();
                Assert.Equal(0, srv.Count);
            });
            t.Run("Game_RestoresScopeAfterExecute", () =>
            {
                IoCInitializer.Initialize();
                var root = IoC.GetCurrentScope();
                var gs   = new GameSpace(800,600);
                var srv  = new BlockingCollection<ICommand>();
                new Game("g4",IoC.Resolve<IScope>("Scopes.New"),new BlockingCollection<ICommand>(),srv,gs).Execute();
                Assert.Same(root, IoC.GetCurrentScope());
            });

            // ── GameSpace / LabWorkEvaluator ───────────────────────────────
            Console.WriteLine("\n[GameSpace / Evaluator]");
            t.Run("AddGetRemove", () =>
            {
                var gs=new GameSpace(100,100);
                var o=new UObject();
                gs.AddObject("x",o);
                Assert.Same(o,gs.GetObject("x"));
                gs.RemoveObject("x");
                Assert.Throws<KeyNotFoundException>(()=>gs.GetObject("x"));
            });
            t.Run("GetObject_Missing_Throws", () => Assert.Throws<KeyNotFoundException>(()=>new GameSpace(10,10).GetObject("none")));
            t.Run("GetSnapshot_Isolated", () =>
            {
                var gs=new GameSpace(100,100);
                gs.AddObject("a",new UObject());
                var snap=gs.GetSnapshot();
                gs.RemoveObject("a");
                Assert.Equal(1,snap.Count);
            });
            t.Run("GetState_ReturnsObjects", () =>
            {
                var gs = Helpers.MakeGs();
                var state = gs.GetState();
                Assert.Single(state.Objects);
            });
            t.Run("Evaluator_Pass",    () => Assert.True(new LabWorkEvaluator(Helpers.MakeGs(),new List<EvaluationCriterion>{new(){Name="X",ObjectId="ship-1",Property="Position.X",Operator="greater_than",ExpectedValue=50,Weight=1}}).Evaluate().Passed));
            t.Run("Evaluator_Fail",    () => Assert.False(new LabWorkEvaluator(Helpers.MakeGs(),new List<EvaluationCriterion>{new(){Name="X",ObjectId="ship-1",Property="Position.X",Operator="greater_than",ExpectedValue=900,Weight=1}}).Evaluate().Passed));
            t.Run("Evaluator_Missing", () => Assert.False(new LabWorkEvaluator(new GameSpace(100,100),new List<EvaluationCriterion>{new(){Name="X",ObjectId="none",Property="Position.X",Operator="equals",ExpectedValue=0,Weight=1}}).Evaluate().Details[0].Satisfied));
            t.Run("Evaluator_Empty",   () => Assert.Equal(0.0,new LabWorkEvaluator(new GameSpace(100,100),new List<EvaluationCriterion>()).Evaluate().Score));

            // ── FireCommand ────────────────────────────────────────────────
            Console.WriteLine("\n[FireCommand]");
            t.Run("Fire_AddsTorpedo", () =>
            {
                var gs  = new GameSpace(800,600);
                var s   = Helpers.MakeShip(100,100);
                gs.AddObject("ship",s);
                new FireCommand(s,gs).Execute();
                Assert.Equal(2, gs.GetAllObjects().Count);
            });
            t.Run("Fire_NullShooter_Throws",    () => Assert.Throws<ArgumentNullException>(()=>new FireCommand(null!,new GameSpace(10,10))));
            t.Run("Fire_NullGameSpace_Throws",  () => Assert.Throws<ArgumentNullException>(()=>new FireCommand(new UObject(),null!)));

            // ── ЛР №9+10: AdapterGenerator / AutoWiring / AutoRegistrar ──
            Console.WriteLine("\n[ЛР9+10: AdapterGenerator / AutoWiring / AutoRegistrar]");
            t.Run("AdapterGenerator_IMovable", () =>
            {
                var o = new UObject();
                o.SetProperty("Position",new Vector(1,2));
                o.SetProperty("Velocity",new Vector(3,4));
                var adapter = AdapterGenerator.CreateAdapter<IMovable>(o);
                Assert.Equal(new Vector(1,2), adapter.Position);
                adapter.Position = new Vector(9,9);
                Assert.Equal(new Vector(9,9),(Vector)o.GetProperty("Position"));
            });
            t.Run("AdapterGenerator_Caches", () =>
            {
                var o1=new UObject(); o1.SetProperty("Position",new Vector(0,0)); o1.SetProperty("Velocity",new Vector(1,1));
                var o2=new UObject(); o2.SetProperty("Position",new Vector(0,0)); o2.SetProperty("Velocity",new Vector(1,1));
                Assert.Equal(AdapterGenerator.CreateAdapter<IMovable>(o1).GetType(),
                             AdapterGenerator.CreateAdapter<IMovable>(o2).GetType());
            });
            t.Run("AdapterGenerator_NotInterface_Throws", () =>
                Assert.Throws<ArgumentException>(()=>AdapterGenerator.CreateAdapter(typeof(UObject),new UObject())));
            t.Run("IoC_AdapterCreate_Strategy", () =>
            {
                IoCInitializer.Initialize();
                var o = new UObject();
                o.SetProperty("Position",new Vector(5,5));
                o.SetProperty("Velocity",new Vector(1,0));
                var adapter = (IMovable)IoC.Resolve<object>("Adapter.Create","SpaceBattle.Lib.IMovable",o);
                Assert.Equal(new Vector(5,5), adapter.Position);
            });
            t.Run("ObjectCreate_AutoWiring", () =>
            {
                IoCInitializer.Initialize();
                var obj = IoC.Resolve<object>("Object.Create","SpaceBattle.Lib.UObject");
                Assert.IsType<UObject>(obj);
            });
            t.Run("ObjectCreate_UnknownType_Throws", () =>
            {
                IoCInitializer.Initialize();
                Assert.Throws<InvalidOperationException>(()=>IoC.Resolve<object>("Object.Create","No.Such.Type.XYZ"));
            });
            t.Run("AutoRegistrar_RegistersMarkedClasses", () =>
            {
                IoCInitializer.Initialize();
                AutoRegistrar.RegisterAll(IoC.GetCurrentScope(), typeof(Program).Assembly);
                var svc = IoC.Resolve<object>("TestDummy.Create");
                Assert.IsType<TestDummy>(svc);
            });

            // ── Configuration ──────────────────────────────────────────────
            Console.WriteLine("\n[Configuration]");
            t.Run("CreateDefault_6Ships", () => Assert.Equal(6, ConfigurationLoader.CreateDefault().Ships.Count));
            t.Run("SaveAndLoad_RoundTrip", () =>
            {
                var c = ConfigurationLoader.CreateDefault();
                var p = Path.GetTempFileName();
                try { ConfigurationLoader.SaveToFile(c,p); Assert.Equal(c.Ships.Count,ConfigurationLoader.LoadFromFile(p).Ships.Count); }
                finally { File.Delete(p); }
            });


            // ── ЛР №5: SoftStop вне потока ─────────────────────────────────
            t.Run("SoftStop_WrongContext_Throws", () =>
            {
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                Assert.Throws<InvalidOperationException>(() => new SoftStopCommand(st).Execute());
                q.Add(new ActionCommand(() => st.Stop())); Thread.Sleep(200);
            });

            // ── Evaluator дополнительные операторы ─────────────────────────
            Console.WriteLine("\n[Evaluator дополнительные]");
            t.Run("Evaluator_Equals_Pass", () =>
            {
                var gs = Helpers.MakeGs("s", 100, 50);
                var ev = new LabWorkEvaluator(gs, new List<EvaluationCriterion>
                { new() { Name="eq", ObjectId="s", Property="Position.X", Operator="equals", ExpectedValue=100, Weight=1 } });
                Assert.True(ev.Evaluate().Passed);
            });
            t.Run("Evaluator_NotEquals_Pass", () =>
            {
                var gs = Helpers.MakeGs("s", 100, 50);
                var ev = new LabWorkEvaluator(gs, new List<EvaluationCriterion>
                { new() { Name="ne", ObjectId="s", Property="Position.X", Operator="not_equals", ExpectedValue=999, Weight=1 } });
                Assert.True(ev.Evaluate().Passed);
            });
            t.Run("Evaluator_LessThan_Pass", () =>
            {
                var gs = Helpers.MakeGs("s", 10, 50);
                var ev = new LabWorkEvaluator(gs, new List<EvaluationCriterion>
                { new() { Name="lt", ObjectId="s", Property="Position.X", Operator="less_than", ExpectedValue=100, Weight=1 } });
                Assert.True(ev.Evaluate().Passed);
            });
            t.Run("Evaluator_PositionY", () =>
            {
                var gs = Helpers.MakeGs("s", 10, 50);
                var ev = new LabWorkEvaluator(gs, new List<EvaluationCriterion>
                { new() { Name="y", ObjectId="s", Property="Position.Y", Operator="equals", ExpectedValue=50, Weight=1 } });
                Assert.True(ev.Evaluate().Passed);
            });

            // ── ЛР №8: GameLifecycle ────────────────────────────────────────
            Console.WriteLine("\n[ЛР8: GameLifecycle]");
            t.Run("GameLifecycle_CreateAndGetQueue", () =>
            {
                IoCInitializer.Initialize();
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                var threads = new Dictionary<string,ServerThread>{["T0"]=st};
                var gs = new GameSpace(100,100);
                var lc = new GameLifecycle(threads, quantumMs: 50);
                var id = lc.CreateGame(gs, "T0");
                Assert.NotNull(id);
                Assert.NotNull(lc.GetQueue(id));
                foreach (var g in lc.ActiveGames.Values) g.Stop();
                q.Add(new ActionCommand(()=>st.Stop())); Thread.Sleep(300);
            });
            t.Run("GameLifecycle_DeleteGame", () =>
            {
                IoCInitializer.Initialize();
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                var threads = new Dictionary<string,ServerThread>{["T0"]=st};
                var gs = new GameSpace(100,100);
                var lc = new GameLifecycle(threads, quantumMs: 50);
                lc.RegisterStrategies(IoC.GetCurrentScope());
                var id = lc.CreateGame(gs, "T0");
                IoC.Resolve<object>("Игра.Удалить игру", id);
                Assert.Equal(0, lc.ActiveGames.Count);
                q.Add(new ActionCommand(()=>st.Stop())); Thread.Sleep(300);
            });
            t.Run("GameLifecycle_IoCCreate", () =>
            {
                IoCInitializer.Initialize();
                var q  = new BlockingCollection<ICommand>();
                var st = new ServerThread(q);
                st.Start();
                var threads = new Dictionary<string,ServerThread>{["T0"]=st};
                var gs = new GameSpace(100,100);
                var lc = new GameLifecycle(threads, quantumMs: 50);
                lc.RegisterStrategies(IoC.GetCurrentScope());
                var id = (string)IoC.Resolve<object>("Игра.Создать новую", gs, "T0");
                Assert.NotNull(id);
                Assert.True(lc.ActiveGames.ContainsKey(id));
                foreach (var g in lc.ActiveGames.Values) g.Stop();
                q.Add(new ActionCommand(()=>st.Stop())); Thread.Sleep(300);
            });

            // ── ЛР №7: InterpretCommand расширенные ────────────────────────
            t.Run("InterpretCommand_FallbackQueue", () =>
            {
                IoCInitializer.Initialize();
                var gs       = Helpers.MakeGs();
                var fallback = new BlockingCollection<ICommand>();
                var gq       = new Dictionary<string,BlockingCollection<ICommand>>();
                new InterpretCommand(
                    "{\"type\":\"move\",\"gameId\":\"unknown\",\"gameItemId\":\"ship-1\"}",
                    gq, gs, fallback).Execute();
                Assert.Equal(1, fallback.Count);
            });
            t.Run("InterpretCommand_InvalidType_Silent", () =>
            {
                IoCInitializer.Initialize();
                var gs = Helpers.MakeGs();
                var q  = new BlockingCollection<ICommand>();
                var gq = new Dictionary<string,BlockingCollection<ICommand>>{["g"]=q};
                new InterpretCommand(
                    "{\"type\":\"no_such_cmd\",\"gameId\":\"g\",\"gameItemId\":\"ship-1\"}",
                    gq, gs).Execute();
                Assert.Equal(0, q.Count);
            });
            t.Run("InterpretCommand_StopMovement", () =>
            {
                IoCInitializer.Initialize();
                var gs = Helpers.MakeGs();
                var q  = new BlockingCollection<ICommand>();
                var gq = new Dictionary<string,BlockingCollection<ICommand>>{["g"]=q};
                new InterpretCommand(
                    "{\"type\":\"start_movement\",\"gameId\":\"g\",\"gameItemId\":\"ship-1\",\"parameters\":{\"vx\":5,\"vy\":0}}",
                    gq, gs).Execute();
                q.Take().Execute();
                new InterpretCommand(
                    "{\"type\":\"stop_movement\",\"gameId\":\"g\",\"gameItemId\":\"ship-1\"}",
                    gq, gs).Execute();
                q.Take().Execute();
                var bridge = (BridgeCommand)gs.GetObject("ship-1").GetProperty("_bridge");
                int moveCount = 0;
                gs.GetObject("ship-1").SetProperty("Position", new Vector(0,0));
                bridge.Execute(); // EmptyCommand
                Assert.Equal(new Vector(0,0), (Vector)gs.GetObject("ship-1").GetProperty("Position"));
            });

            // ── AdapterGenerator: IRotatable ───────────────────────────────
            t.Run("AdapterGenerator_IRotatable", () =>
            {
                var o = new UObject();
                o.SetProperty("Angle", 30);
                o.SetProperty("AngularVelocity", 15);
                var a = AdapterGenerator.CreateAdapter<IRotatable>(o);
                Assert.Equal(30, a.Angle);
                a.Angle = 90;
                Assert.Equal(90, (int)o.GetProperty("Angle"));
            });

            // ── FireCommand: тип торпеды ────────────────────────────────────
            t.Run("Fire_TorpedoType_IsProjectile", () =>
            {
                var gs   = new GameSpace(800,600);
                var ship = Helpers.MakeShip(100,100,0);
                gs.AddObject("ship",ship);
                new FireCommand(ship,gs).Execute();
                var torpedo = gs.GetAllObjects().Values.FirstOrDefault(o =>
                    { try { return (string)o.GetProperty("Type")=="Projectile"; } catch { return false; } });
                Assert.NotNull(torpedo);
            });

            // ── CollisionCommand: симметрия и снимок ────────────────────────
            t.Run("Collision_Snapshot_CalledOnce", () =>
            {
                IoCInitializer.Initialize();
                int cnt = 0;
                IoC.Resolve<ICommand>("IoC.Register","Collision.A.B",
                    (Func<object[],object>)(_=>new ActionCommand(()=>cnt++))).Execute();
                var gs = new GameSpace(100,100);
                var a  = new UObject(); a.SetProperty("Type","A"); a.SetProperty("Position",new Vector(5,5));
                var b  = new UObject(); b.SetProperty("Type","B"); b.SetProperty("Position",new Vector(5,5));
                gs.AddObject("a",a); gs.AddObject("b",b);
                new CollisionCommand(gs).Execute();
                Assert.Equal(1, cnt);
            });

            t.Summary();
        }
    }

    [IoCAutoRegister("TestDummy.Create")]
    internal class TestDummy { }
}
