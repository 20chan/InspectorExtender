using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[CanEditMultipleObjects]
[CustomEditor(typeof(MonoBehaviour), true)]
public class InspectorExtender : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        var properties = target
            .GetType()
            .GetProperties()
            .Where(p => p.CanRead && p.DeclaringType.IsSubclassOf(typeof(MonoBehaviour)))
            .ToArray();

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };


        if (properties.Length != 0) {
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Properties", style);
        }
        foreach (var p in properties) {
            var type = p.PropertyType;
            try {
                object value;
                var byref = false;
                if (type.IsByRef) {
                    byref = true;
                    type = type.GetElementType();
                    if (!type.IsValueType) {
                        var classType = target.GetType();
                        var funcType = typeof(Func<,>);
                        var genericFuncType = funcType.MakeGenericType(classType, type);
                        value = GetValueOfRefProperty(p, target, type, classType, genericFuncType);
                    }
                    else {
                        var classType = target.GetType();
                        var funcType = typeof(Func<,>);
                        var genericFuncType = funcType.MakeGenericType(classType, typeof(IntPtr));
                        value = GetValueOfRefProperty(p, target, type, classType, genericFuncType);
                    }
                }
                else {
                    value = p.GetValue(target);
                }
                if (type == typeof(int)) {
                    value = EditorGUILayout.IntField(p.Name, (int)value);
                }
                else if (type == typeof(float)) {
                    value = EditorGUILayout.FloatField(p.Name, (float)value);
                }
                else if (type == typeof(bool)) {
                    value = EditorGUILayout.Toggle(p.Name, (bool)value);
                }
                else if (type == typeof(string)) {
                    value = EditorGUILayout.TextField(p.Name, (string)value);
                }
                else if (type.IsEnum) {
                    value = EditorGUILayout.EnumPopup(p.Name, (System.Enum)value);
                }
                else if (type == typeof(Vector2Int)) {
                    value = EditorGUILayout.Vector2IntField(p.Name, (Vector2Int)value);
                }
                else if (type.IsSubclassOf(typeof(Object))) {
                    value = EditorGUILayout.ObjectField(p.Name, (Object)value, type, true);
                }
                else if (type.IsInterface) {
                    var v = EditorGUILayout.ObjectField(p.Name, (Object)value, type, true);
                    if (v.GetType().IsAssignableFrom(type)) {
                        value = v;
                    }
                }
                else {
                    continue;
                }

                if (GUI.changed) {
                    if (p.CanWrite) {
                        p.SetValue(target, value);
                    }
                    else if (byref) {
                        var classType = target.GetType();
                        var funcType = typeof(Action<,>);
                        var genericFuncType = funcType.MakeGenericType(classType, type);

                        if (type == typeof(int)) {
                            SetValueOfRefProperty(p, target, classType, genericFuncType, OpCodes.Stind_I4, (int)value);
                        }
                        else if (type == typeof(float)) {
                            SetValueOfRefProperty(p, target, classType, genericFuncType, OpCodes.Stind_R4,
                                (float)value);
                        }
                        else if (type == typeof(bool)) {
                            SetValueOfRefProperty(p, target, classType, genericFuncType, OpCodes.Stind_I1, (bool)value);
                        }
                        else {
                            SetValueOfRefProperty(p, target, classType, genericFuncType, OpCodes.Stind_Ref, value);
                        }
                    }
                }
            }
            catch {
                continue;
            }
        }

        var methods = target
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 0 && !m.IsSpecialName)
            .ToArray();

        if (methods.Length != 0) {
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Methods", style);
        }

        var go = ((MonoBehaviour)target).gameObject;

        foreach (var m in methods) {
            if (GUILayout.Button($"{m.ReturnType.Name} {m.Name}()")) {
                m.Invoke(target, new object[0]);

                if (!EditorApplication.isPlaying) {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
                }
            }
        }
    }

    private static object GetValueOfRefProperty(PropertyInfo p, object instance, Type type, Type targetType, Type funcType) {
        var getter = p.GetGetMethod();
        var name = $"TempGetter{getter.Name}";
        var dm = new DynamicMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            CallingConventions.Standard,
            type.IsValueType ? typeof(IntPtr) : type,
            new[] { targetType },
            targetType, true);

        var ilg = dm.GetILGenerator();
        ilg.Emit(OpCodes.Ldarg_0);
        ilg.Emit(OpCodes.Call, getter);
        if (!type.IsValueType) ilg.Emit(OpCodes.Ldind_Ref);
        ilg.Emit(OpCodes.Ret);
        var del = dm.CreateDelegate(funcType);

        if (!type.IsValueType) {
            return del.DynamicInvoke(instance);
        }
        else {
            var ret = (IntPtr)del.DynamicInvoke(instance);
            return Marshal.PtrToStructure(ret, type);
        }
    }
    private static void SetValueOfRefProperty<T>(PropertyInfo p, object instance, Type targetType, Type funcType, OpCode stCode, T value) {
        var getter = p.GetGetMethod();
        var name = $"TempSetter{getter.Name}";
        var dm = new DynamicMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            CallingConventions.Standard,
            typeof(void),
            new[] { targetType, typeof(T) },
            targetType, true);

        var ilg = dm.GetILGenerator();
        ilg.Emit(OpCodes.Ldarg_0);
        ilg.Emit(OpCodes.Call, getter);
        ilg.Emit(OpCodes.Ldarg_1);
        ilg.Emit(stCode);
        ilg.Emit(OpCodes.Ret);

        var del = dm.CreateDelegate(funcType);
        del.DynamicInvoke(instance, value);
    }
}