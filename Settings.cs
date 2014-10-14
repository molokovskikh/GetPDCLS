using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace GetPDCLS
{

    [Serializable]
    public class Settings
    {
        private const string FILE_SETTINGS = "settings";
        public string Path { get; set; }
        public int Interval { get; set; }

        public static Settings Read()
        {
            BinaryFormatter formatter = new BinaryFormatter();
            string fileName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_SETTINGS);
            if(File.Exists(fileName))
            using (FileStream fs = File.OpenRead(fileName))
            {
                return formatter.Deserialize(fs) as Settings; 
            }
            return null;
        }

        public static void Write(Settings settings)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (FileStream fs = File.OpenWrite(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FILE_SETTINGS)))
            {
                formatter.Serialize(fs,settings);                
            }
        }


    }

}
