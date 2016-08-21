﻿using System;

namespace Watts
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Watts.exe <\"Path/To/WebApplication.dll\"> <\"Path/To/OutputFolder\"> [\"Path/To/TypeMappings.json\"]");
                return 1;
            }

            var watts = new WebApiToTypeScript.WebApiToTypeScript()
            {
                WebApiApplicationAssembly = args[0],
                OutputDirectory = args[1]
            };

            if (args.Length >= 3)
                watts.TypeMappingsFileName = args[2];

            watts.Execute();

            return 0;
        }
    }
}