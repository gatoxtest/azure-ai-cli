﻿using System.Reflection;
using Newtonsoft.Json;
using Azure.AI.OpenAI;
using Newtonsoft.Json.Linq;
using System.Collections;

namespace Azure.AI.Details.Common.CLI.Extensions.HelperFunctions
{
    public class HelperFunctionFactory
    {
        public HelperFunctionFactory()
        {
        }

        public HelperFunctionFactory(Assembly assembly)
        {
            AddFunctions(assembly);
        }

        public HelperFunctionFactory(Type type1, params Type[] types)
        {
            AddFunctions(type1, types);
        }

        public HelperFunctionFactory(IEnumerable<Type> types)
        {
            AddFunctions(types);
        }

        public HelperFunctionFactory(Type type)
        {
            AddFunctions(type);
        }

        public void AddFunctions(Assembly assembly)
        {
            AddFunctions(assembly.GetTypes());
        }

        public void AddFunctions(Type type1, params Type[] types)
        {
            AddFunctions(new List<Type> { type1 });
            AddFunctions(types);
        }

        public void AddFunctions(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                AddFunctions(type);
            }
        }

        public void AddFunctions(Type type)
        {
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
            foreach (var method in methods)
            {
                AddFunction(method);
            }
        }

        public void AddFunction(MethodInfo method)
        {
            var attributes = method.GetCustomAttributes(typeof(HelperFunctionDescriptionAttribute), false);
            if (attributes.Length > 0)
            {
                var funcDescriptionAttrib = attributes[0] as HelperFunctionDescriptionAttribute;
                var funcDescription = funcDescriptionAttrib!.Description;

                string json = GetMethodParametersJsonSchema(method);
                if (Program.Debug)
                {
                    System.Console.WriteLine($"\nFunction: {method.Name}");
                    System.Console.WriteLine($"Description: {funcDescription}");
                    System.Console.WriteLine($"Parameters: {json}");
                }
                _functions.TryAdd(method, new FunctionDefinition(method.Name)
                {
                    Description = funcDescription,
                    Parameters = new BinaryData(json)
                });
            }
        }

        public IEnumerable<FunctionDefinition> GetFunctionDefinitions()
        {
            return _functions.Values;
        }

        public bool TryCallFunction(ChatCompletionsOptions options, HelperFunctionCallContext context, out string? result)
        {
            result = null;
            if (!string.IsNullOrEmpty(context.FunctionName) && !string.IsNullOrEmpty(context.Arguments))
            {
                var function = _functions.FirstOrDefault(x => x.Value.Name == context.FunctionName);
                if (function.Key != null)
                {
                    result = CallFunction(function.Key, function.Value, context.Arguments);
                    options.Messages.Add(new ChatRequestAssistantMessage("") { FunctionCall = new FunctionCall(context.FunctionName, context.Arguments) });
                    options.Messages.Add(new ChatRequestFunctionMessage(context.FunctionName, result));
                    return true;
                }
            }
            return false;
        }

        // operator to add to FunctionFactories together
        public static HelperFunctionFactory operator +(HelperFunctionFactory a, HelperFunctionFactory b)
        {
            var newFactory = new HelperFunctionFactory();
            a._functions.ToList().ForEach(x => newFactory._functions.Add(x.Key, x.Value));
            b._functions.ToList().ForEach(x => newFactory._functions.Add(x.Key, x.Value));
            return newFactory;
        }

        private static string? CallFunction(MethodInfo methodInfo, FunctionDefinition functionDefinition, string argumentsAsJson)
        {
            var dbg = $"Calling function {methodInfo.Name} with arguments {argumentsAsJson}";
            if (Program.Debug) Console.WriteLine(dbg);
            AI.DBG_TRACE_VERBOSE(dbg);

            var jObject = JObject.Parse(argumentsAsJson);
            var arguments = new List<object>();

            var parameters = methodInfo.GetParameters();
            foreach (var parameter in parameters)
            {
                var parameterName = parameter.Name;
                if (parameterName == null) continue;

                var parameterValue = jObject[parameterName]?.ToString();
                if (parameterValue == null) continue;

                var parsed = ParseParameterValue(parameterValue, parameter.ParameterType);
                arguments.Add(parsed);
            }

            var args = arguments.ToArray();
            var result = CallFunction(methodInfo, args);
            return ConvertFunctionResultToString(result);
        }

        private static object? CallFunction(MethodInfo methodInfo, object[] args)
        {
            var t = methodInfo.ReturnType;
            return t == typeof(Task)
                ? CallVoidAsyncFunction(methodInfo, args)
                : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)
                    ? CallAsyncFunction(methodInfo, args)
                    : t.Name != "Void"
                        ? CallSyncFunction(methodInfo, args)
                        : CallVoidFunction(methodInfo, args);
        }

        private static object? CallVoidAsyncFunction(MethodInfo methodInfo, object[] args)
        {
            var task = methodInfo.Invoke(null, args) as Task;
            task!.Wait();
            return true;
        }

        private static object? CallAsyncFunction(MethodInfo methodInfo, object[] args)
        {
            var task = methodInfo.Invoke(null, args) as Task;
            task!.Wait();
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        private static object? CallSyncFunction(MethodInfo methodInfo, object[] args)
        {
            return methodInfo.Invoke(null, args);
        }

        private static object? CallVoidFunction(MethodInfo methodInfo, object[] args)
        {
            methodInfo.Invoke(null, args);
            return true;
        }

        private static string? ConvertFunctionResultToString(object? result)
        {
            if (result is IEnumerable enumerable && !(result is string))
            {
                var array = new JArray();
                foreach (var item in enumerable)
                {
                    var str = item.ToString();
                    array.Add(str);
                }
                return array.ToString();
            }
            return result?.ToString();
        }

        private static object ParseParameterValue(string parameterValue, Type parameterType)
        {
            if (IsArrayType(parameterType))
            {
                Type elementType = parameterType.GetElementType()!;
                return CreateGenericCollectionFromJsonArray(parameterValue, typeof(Array), elementType);
            }

            if (IsTuppleType(parameterType))
            {
                Type elementType = parameterType.GetGenericArguments()[0];
                return CreateTuppleTypeFromJsonArray(parameterValue, elementType);
            }

            if (IsGenericListOrEquivalentType(parameterType))
            {
                Type elementType = parameterType.GetGenericArguments()[0];
                return CreateGenericCollectionFromJsonArray(parameterValue, typeof(List<>), elementType);
            }

            switch (Type.GetTypeCode(parameterType))
            {
                case TypeCode.Boolean: return bool.Parse(parameterValue!);
                case TypeCode.Byte: return byte.Parse(parameterValue!);
                case TypeCode.Decimal: return decimal.Parse(parameterValue!);
                case TypeCode.Double: return double.Parse(parameterValue!);
                case TypeCode.Single: return float.Parse(parameterValue!);
                case TypeCode.Int16: return short.Parse(parameterValue!);
                case TypeCode.Int32: return int.Parse(parameterValue!);
                case TypeCode.Int64: return long.Parse(parameterValue!);
                case TypeCode.SByte: return sbyte.Parse(parameterValue!);
                case TypeCode.UInt16: return ushort.Parse(parameterValue!);
                case TypeCode.UInt32: return uint.Parse(parameterValue!);
                case TypeCode.UInt64: return ulong.Parse(parameterValue!);
                case TypeCode.String: return parameterValue!;
                default: return Convert.ChangeType(parameterValue!, parameterType);
            }
        }

        private static object CreateGenericCollectionFromJsonArray(string parameterValue, Type collectionType, Type elementType)
        {
            var array = JArray.Parse(parameterValue);

            if (collectionType == typeof(Array))
            {
                var collection = Array.CreateInstance(elementType, array.Count);
                for (int i = 0; i < array.Count; i++)
                {
                    var parsed = ParseParameterValue(array[i].ToString(), elementType);
                    if (parsed != null) collection.SetValue(parsed, i);
                }
                return collection;
            }
            else if (collectionType == typeof(List<>))
            {
                var collection = Activator.CreateInstance(collectionType.MakeGenericType(elementType));
                var list = collection as IList;
                foreach (var item in array)
                {
                    var parsed = ParseParameterValue(item.ToString(), elementType);
                    if (parsed != null) list!.Add(parsed);
                }
                return collection!;
            }

            return array;
        }

        private static object CreateTuppleTypeFromJsonArray(string parameterValue, Type elementType)
        {
            var list = new List<object>();

            var array = JArray.Parse(parameterValue);
            foreach (var item in array)
            {
                var parsed = ParseParameterValue(item.ToString(), elementType);
                if (parsed != null) list!.Add(parsed);
            }

            var collection = list.Count() switch
            {
                1 => Activator.CreateInstance(typeof(Tuple<>).MakeGenericType(elementType), list[0]),
                2 => Activator.CreateInstance(typeof(Tuple<,>).MakeGenericType(elementType, elementType), list[0], list[1]),
                3 => Activator.CreateInstance(typeof(Tuple<,,>).MakeGenericType(elementType, elementType, elementType), list[0], list[1], list[2]),
                4 => Activator.CreateInstance(typeof(Tuple<,,,>).MakeGenericType(elementType, elementType, elementType, elementType), list[0], list[1], list[2], list[3]),
                5 => Activator.CreateInstance(typeof(Tuple<,,,,>).MakeGenericType(elementType, elementType, elementType, elementType, elementType), list[0], list[1], list[2], list[3], list[4]),
                6 => Activator.CreateInstance(typeof(Tuple<,,,,,>).MakeGenericType(elementType, elementType, elementType, elementType, elementType, elementType), list[0], list[1], list[2], list[3], list[4], list[5]),
                7 => Activator.CreateInstance(typeof(Tuple<,,,,,,>).MakeGenericType(elementType, elementType, elementType, elementType, elementType, elementType, elementType), list[0], list[1], list[2], list[3], list[4], list[5], list[6]),
                _ => throw new Exception("Tuples with more than 7 elements are not supported")
            };
            return collection!;
        }

        private static string GetMethodParametersJsonSchema(MethodInfo method)
        {
            var schema = new JObject();
            schema["type"] = "object";

            var properties = new JObject();
            schema["properties"] = properties;

            var required = new JArray();
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.Name == null) continue;

                properties[parameter.Name] = GetJsonSchemaForParameterWithDescription(parameter);
                if (!parameter.IsOptional)
                {
                    required.Add(parameter.Name);
                }
            }

            schema["required"] = required;

            return schema.ToString(Formatting.None);
        }

        private static JToken GetJsonSchemaForParameterWithDescription(ParameterInfo parameter)
        {
            var schema = GetJsonSchemaForType(parameter.ParameterType);
            schema["description"] = GetParameterDescription(parameter);
            return schema;
        }

        private static string GetParameterDescription(ParameterInfo parameter)
        {
            var attributes = parameter.GetCustomAttributes(typeof(HelperFunctionParameterDescriptionAttribute), false);
            var paramDescriptionAttrib = attributes.Length > 0 ? (attributes[0] as HelperFunctionParameterDescriptionAttribute) : null;
            return  paramDescriptionAttrib?.Description ?? $"The {parameter.Name} parameter";
        }

        private static JObject GetJsonSchemaForType(Type t)
        {
            return IsJsonArrayEquivalentType(t)
                ? GetJsonArraySchemaFromType(t)
                : GetJsonPrimativeSchemaFromType(t);
        }

        private static JObject GetJsonArraySchemaFromType(Type containerType)
        {
            var schema = new JObject();
            schema["type"] = "array";
            schema["items"] = GetJsonArrayItemSchemaFromType(containerType);
            return schema;
        }

        private static JObject GetJsonArrayItemSchemaFromType(Type containerType)
        {
            var itemType = containerType.IsArray
                ? containerType.GetElementType()!
                : containerType.GetGenericArguments()[0];
            return GetJsonSchemaForType(itemType);
        }

        private static JObject GetJsonPrimativeSchemaFromType(Type primativeType)
        {
            var schema = new JObject();
            schema["type"] = GetJsonTypeFromPrimitiveType(primativeType);
            return schema;
        }

        private static string GetJsonTypeFromPrimitiveType(Type primativeType)
        {
            return Type.GetTypeCode(primativeType) switch
            {
                TypeCode.Boolean => "boolean",
                TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
                TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => "integer",
                TypeCode.Decimal or TypeCode.Double or TypeCode.Single => "number",
                TypeCode.String => "string",
                _ => "string"
            };
        }

        private static bool IsJsonArrayEquivalentType(Type t)
        {
            return IsArrayType(t) || IsTuppleType(t) || IsGenericListOrEquivalentType(t);
        }

        private static bool IsArrayType(Type t)
        {
            return t.IsArray;
        }

        private static bool IsTuppleType(Type parameterType)
        {
            return parameterType.IsGenericType && parameterType.GetGenericTypeDefinition().Name.StartsWith("Tuple");
        }

        private static bool IsGenericListOrEquivalentType(Type t)
        {
            return t.IsGenericType &&
               (t.GetGenericTypeDefinition() == typeof(List<>) ||
                t.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                t.GetGenericTypeDefinition() == typeof(IList<>) ||
                t.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
                t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        }

        private Dictionary<MethodInfo, FunctionDefinition> _functions = new();
    }
}
