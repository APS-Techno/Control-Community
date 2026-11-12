using System.Xml.Serialization;

namespace APS.SystemControl.XML
{
    internal class Serializer<T>
    {
        static readonly XmlSerializer _serializer = new(typeof(T));
        public static void Save(T value, string path)
        {
            using var ms = File.CreateText(path);
            _serializer.Serialize(ms, value);
        }
        public static T? Load(string path)
        {
            using var ms = File.OpenText(path);
            return (T)_serializer.Deserialize(ms);
        }
    }
}
