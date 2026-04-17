using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpaceBattle.Lib
{
    // ─── ЛР №10. Генерация адаптеров — два подхода ──────────────────────────

    /// <summary>
    /// ЛР №10. Генерирует адаптеры для произвольных интерфейсов,
    /// делегирующих все свойства и методы к IUObject.GetProperty/SetProperty.
    ///
    /// Два режима (выбирается автоматически):
    ///   1. PRIMARY: Roslyn (Microsoft.CodeAnalysis.CSharp) —
    ///      генерирует настоящий C#-код, компилирует его в памяти.
    ///      Это «динамическая компиляция C#» согласно ЛР №10.
    ///   2. FALLBACK: System.Reflection.Emit —
    ///      генерирует IL-код напрямую без C#-исходника.
    ///
    /// Скомпилированные типы кэшируются по имени интерфейса.
    /// </summary>
    public static class AdapterGenerator
    {
        private static readonly ConcurrentDictionary<string, Type> _cache = new();

        // Reflection.Emit fallback модуль
        private static readonly AssemblyBuilder _asmBuilder =
            AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("SpaceBattle.Generated"),
                AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder _modBuilder =
            _asmBuilder.DefineDynamicModule("GeneratedAdapters");

        /// <summary>Возвращает адаптер для interfaceType, делегирующий к target.</summary>
        public static object CreateAdapter(Type interfaceType, IUObject target)
        {
            if (!interfaceType.IsInterface)
                throw new ArgumentException($"{interfaceType.Name} не является интерфейсом.");

            var adapterType = _cache.GetOrAdd(
                interfaceType.FullName!,
                _ => TryRoslynCompile(interfaceType) ?? BuildWithEmit(interfaceType));

            return Activator.CreateInstance(adapterType, target)!;
        }

        /// <summary>Обобщённая перегрузка для удобства.</summary>
        public static T CreateAdapter<T>(IUObject target) where T : class
            => (T)CreateAdapter(typeof(T), target);

        // ── PRIMARY: Roslyn (динамическая компиляция C#) ─────────────────────

        /// <summary>
        /// Генерирует C#-исходник адаптера и компилирует его через Roslyn.
        /// Возвращает null при ошибке компиляции (используется Emit-fallback).
        /// </summary>
        private static Type? TryRoslynCompile(Type iface)
        {
            try
            {
                var src  = GenerateCSharpSource(iface);
                var tree = CSharpSyntaxTree.ParseText(src);

                var refs = new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(IUObject).Assembly.Location),
                    MetadataReference.CreateFromFile(iface.Assembly.Location),
                    MetadataReference.CreateFromFile(
                        Assembly.Load("System.Runtime").Location),
                };

                var compilation = CSharpCompilation.Create(
                    $"Adapter_{iface.Name}_{Guid.NewGuid():N}",
                    new[] { tree },
                    refs,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                using var ms = new System.IO.MemoryStream();
                var result  = compilation.Emit(ms);

                if (!result.Success) return null; // fallback to Emit

                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var asm = Assembly.Load(ms.ToArray());
                return asm.GetType($"GeneratedAdapters.{iface.Name}Adapter");
            }
            catch
            {
                return null; // любая ошибка → Emit fallback
            }
        }

        /// <summary>
        /// Генерирует C#-код адаптера для заданного интерфейса.
        /// Каждое свойство/метод интерфейса делегируется к _obj.GetProperty/SetProperty.
        /// </summary>
        private static string GenerateCSharpSource(Type iface)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using SpaceBattle.Lib;");
            sb.AppendLine("namespace GeneratedAdapters {");
            sb.AppendLine($"  public class {iface.Name}Adapter : {iface.FullName} {{");
            sb.AppendLine("    private readonly SpaceBattle.Lib.IUObject _obj;");
            sb.AppendLine($"    public {iface.Name}Adapter(SpaceBattle.Lib.IUObject obj) => _obj = obj;");

            foreach (var prop in iface.GetProperties())
            {
                var tn = CsTypeName(prop.PropertyType);
                sb.AppendLine($"    public {tn} {prop.Name} {{");
                if (prop.CanRead)
                    sb.AppendLine($"      get => ({tn})_obj.GetProperty(\"{prop.Name}\");");
                if (prop.CanWrite)
                    sb.AppendLine($"      set => _obj.SetProperty(\"{prop.Name}\", value);");
                sb.AppendLine("    }");
            }

            foreach (var m in iface.GetMethods().Where(m => !m.IsSpecialName))
            {
                var rt   = CsTypeName(m.ReturnType);
                var prms = string.Join(", ", m.GetParameters()
                               .Select(p => $"{CsTypeName(p.ParameterType)} {p.Name}"));
                var args = string.Join(", ", m.GetParameters().Select(p => p.Name));
                if (m.ReturnType == typeof(void))
                    sb.AppendLine($"    public void {m.Name}({prms}) " +
                                  $"{{ _obj.SetProperty(\"{m.Name}\", new object[]{{ {args} }}); }}");
                else
                    sb.AppendLine($"    public {rt} {m.Name}({prms}) " +
                                  $"=> ({rt})_obj.GetProperty(\"{m.Name}\");");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string CsTypeName(Type t)
        {
            if (t == typeof(void))   return "void";
            if (t == typeof(int))    return "int";
            if (t == typeof(string)) return "string";
            if (t == typeof(bool))   return "bool";
            if (t == typeof(double)) return "double";
            if (t == typeof(float))  return "float";
            return t.FullName ?? t.Name;
        }

        // ── FALLBACK: System.Reflection.Emit (IL-код) ────────────────────────

        private static readonly MethodInfo _getProperty =
            typeof(IUObject).GetMethod(nameof(IUObject.GetProperty))!;
        private static readonly MethodInfo _setProperty =
            typeof(IUObject).GetMethod(nameof(IUObject.SetProperty))!;

        private static Type BuildWithEmit(Type iface)
        {
            var tb = _modBuilder.DefineType(
                $"{iface.Name}EmitAdapter_{Guid.NewGuid():N}",
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(object),
                new[] { iface });

            var fld = tb.DefineField("_obj", typeof(IUObject), FieldAttributes.Private);

            var ctor = tb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard,
                new[] { typeof(IUObject) });
            var ctorIL = ctor.GetILGenerator();
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_1);
            ctorIL.Emit(OpCodes.Stfld, fld);
            ctorIL.Emit(OpCodes.Ret);

            foreach (var prop in iface.GetProperties())
                EmitProperty(tb, fld, prop);

            foreach (var method in iface.GetMethods().Where(m => !m.IsSpecialName))
                EmitMethod(tb, fld, method);

            return tb.CreateType()!;
        }

        private static void EmitProperty(TypeBuilder tb, FieldBuilder fld, PropertyInfo prop)
        {
            var pb = tb.DefineProperty(prop.Name, PropertyAttributes.None, prop.PropertyType, null);

            if (prop.CanRead)
            {
                var getter = tb.DefineMethod($"get_{prop.Name}",
                    MethodAttributes.Public | MethodAttributes.Virtual |
                    MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    prop.PropertyType, Type.EmptyTypes);
                var il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fld);
                il.Emit(OpCodes.Ldstr, prop.Name); il.Emit(OpCodes.Callvirt, _getProperty);
                if (prop.PropertyType.IsValueType) il.Emit(OpCodes.Unbox_Any, prop.PropertyType);
                else il.Emit(OpCodes.Castclass, prop.PropertyType);
                il.Emit(OpCodes.Ret);
                pb.SetGetMethod(getter);
            }

            if (prop.CanWrite)
            {
                var setter = tb.DefineMethod($"set_{prop.Name}",
                    MethodAttributes.Public | MethodAttributes.Virtual |
                    MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    null, new[] { prop.PropertyType });
                var il = setter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fld);
                il.Emit(OpCodes.Ldstr, prop.Name); il.Emit(OpCodes.Ldarg_1);
                if (prop.PropertyType.IsValueType) il.Emit(OpCodes.Box, prop.PropertyType);
                il.Emit(OpCodes.Callvirt, _setProperty); il.Emit(OpCodes.Ret);
                pb.SetSetMethod(setter);
            }
        }

        private static void EmitMethod(TypeBuilder tb, FieldBuilder fld, MethodInfo method)
        {
            var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var mb = tb.DefineMethod(method.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                method.ReturnType, paramTypes);
            var il = mb.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fld);
            il.Emit(OpCodes.Ldstr, method.Name);
            if (method.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Ldc_I4, paramTypes.Length);
                il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < paramTypes.Length; i++)
                {
                    il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldarg, i + 1);
                    if (paramTypes[i].IsValueType) il.Emit(OpCodes.Box, paramTypes[i]);
                    il.Emit(OpCodes.Stelem_Ref);
                }
                il.Emit(OpCodes.Callvirt, _setProperty); il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Callvirt, _getProperty);
                if (method.ReturnType.IsValueType) il.Emit(OpCodes.Unbox_Any, method.ReturnType);
                else il.Emit(OpCodes.Castclass, method.ReturnType);
                il.Emit(OpCodes.Ret);
            }
            tb.DefineMethodOverride(mb, method);
        }
    }

    // ─── ЛР №9. AutoWiring ──────────────────────────────────────────────────

    /// <summary>
    /// ЛР №9. Создаёт объект типа T, автоматически разрешая
    /// все параметры его конструктора через IoC.
    /// </summary>
    public static class AutoWiring
    {
        public static T Create<T>()
        {
            var ctor  = typeof(T).GetConstructors()
                                 .OrderByDescending(c => c.GetParameters().Length)
                                 .First();
            var args  = ctor.GetParameters().Select(p =>
            {
                try   { return IoC.Resolve<object>(p.ParameterType.Name); }
                catch { return p.HasDefaultValue ? p.DefaultValue!
                                                 : Activator.CreateInstance(p.ParameterType)!; }
            }).ToArray();
            return (T)Activator.CreateInstance(typeof(T), args)!;
        }
    }
}
