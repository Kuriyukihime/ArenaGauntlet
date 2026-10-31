using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using RoR2;

namespace BazaarGauntlet
{
    internal static class NativeAccess
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<(Type, string), FieldInfo> fields = new Dictionary<(Type, string), FieldInfo>();
        internal static FieldInfo Field(Type type, string name)
        {
            var key = (type, name);
            if (fields.TryGetValue(key, out var cached)) return cached;
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Fields | BindingFlags.DeclaredOnly);
                if (field != null) { fields[key] = field;return field; }
            }
            throw new MissingFieldException("Native game field missing: " + name);
        }
        internal static T Get<T>(object value, string name) => (T)Field(value.GetType(), name).GetValue(value);
        internal static void Set(object value, string name, object content) => Field(value.GetType(), name).SetValue(value, content);
        internal static void Call(object value, string name, params object[] arguments)
        {
            var method = value.GetType().GetMethod(name, Fields);
            if (method == null) throw new MissingMethodException(value.GetType().FullName, name);
            method.Invoke(value, arguments);
        }
        // Invoke the native base-game calculation rather than copying a formula
        // or forcing Monsoon. Native Run hooks/custom DifficultyDefs still apply.
        internal static readonly Action<Run> BaseDifficulty = CreateBaseDifficulty();
        private static Action<Run> CreateBaseDifficulty()
        {
            var method = typeof(Run).GetMethod("RecalculateDifficultyCoefficentInternal", Fields);
            if (method == null) throw new MissingMethodException(typeof(Run).FullName, "RecalculateDifficultyCoefficentInternal");
            var thunk = new DynamicMethod("BazaarGauntlet_NativeDifficulty", typeof(void), new[] { typeof(Run) }, typeof(NativeAccess), true);
            var il = thunk.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call, method);il.Emit(OpCodes.Ret);
            return (Action<Run>)thunk.CreateDelegate(typeof(Action<Run>));
        }
        private static readonly PropertyInfo Ambient = typeof(Run).GetProperty("ambientLevel");
        private static readonly PropertyInfo AmbientFloor = typeof(Run).GetProperty("ambientLevelFloor");
        private static readonly MethodInfo AmbientChanged = typeof(Run).GetMethod("OnAmbientLevelUp", Fields);
        internal static void RaiseEnemyLevel(Run run, int level, int previousLevel = -1)
        {
            int before = run.ambientLevelFloor;
            if (level <= before) return;
            Ambient.SetValue(run, Math.Max(run.ambientLevel, (float)level), null);AmbientFloor.SetValue(run, level, null);
            // The native formula is recalculated every tick. Reapplying an
            // unchanged floor must not emit a fresh level-up on every tick.
            if (level > (previousLevel < 0 ? before : previousLevel))
            {
                AmbientChanged.Invoke(run, null);
                foreach (var body in CharacterBody.readOnlyInstancesList)
                    if (body && (body.bodyFlags & CharacterBody.BodyFlags.UsesAmbientLevel) != 0) body.OnTeamLevelChanged();
            }
        }
    }
}
