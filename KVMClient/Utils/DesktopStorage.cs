using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
