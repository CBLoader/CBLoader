using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CharacterBuilderLoader
{
    public class Log
    {
        public static bool VerboseMode { get; set; }
        public static void Debug(string msg)
        {
            if(VerboseMode)
                Console.WriteLine("Debug: " + msg);
        }
    }
}
