// Headless regression fixtures only: production code uses the real Unity API.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;

namespace UnityEngine
{
    public static class Application
    {
        public static string persistentDataPath;
        public static string identifier = "test.escape.credentials";
    }
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public static void Reset() => Values.Clear();
        public static bool HasKey(string key) => Values.ContainsKey(key);
        public static string GetString(string key, string fallback = "") => Values.TryGetValue(key, out var value) ? (string)value : fallback;
        public static int GetInt(string key, int fallback = 0) => Values.TryGetValue(key, out var value) ? (int)value : fallback;
        public static void SetString(string key, string value) => Values[key] = value;
        public static void SetInt(string key, int value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() { }
    }
    public static class JsonUtility
    {
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(),
            new JsonSerializerOptions { IncludeFields = true });
        public static T FromJson<T>(string json)
        {
            // Unity JSON deserialization does not run constructors/field initializers.
            var result = (T)FormatterServices.GetUninitializedObject(typeof(T));
            using var document = JsonDocument.Parse(json);
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!document.RootElement.TryGetProperty(field.Name, out var element)) continue;
                object value = field.FieldType == typeof(string) ? element.GetString()
                    : field.FieldType == typeof(bool) ? (object)element.GetBoolean() : element.GetInt32();
                field.SetValue(result, value);
            }
            return result;
        }
    }
}
