using Newtonsoft.Json;
using System;
using System.IO;

namespace KVMClient.Utils
{
    public class Preferences
    {
        public void SetObj<T>(T obj)
        {
            File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + "/settings.json", JsonConvert.SerializeObject(obj, Formatting.Indented));
        }
        public T GetObj<T>() where T : class, new()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "/settings.json";
            if (File.Exists(path))
            {
                var stream = File.ReadAllText(path);
                var s = JsonConvert.DeserializeObject<T>(stream);

                if (s == null)
                {
                    return new T();
                }
                else
                {
                    return s;
                }
            }
            else
            {
                return new T();
            }
        }
    }
}
