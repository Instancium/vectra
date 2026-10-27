using System;
using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom("C:\Users\Instancian\.nuget\packages\instantaigate.core\1.3.1\lib\net10.0\InstantAIGate.Core.dll");
foreach (var type in asm.GetTypes().Where(t => t.Name.Contains("Inference") || t.Name.Contains("Chat") || t.Name.Contains("Settings") || t.Name.Contains("Config")).OrderBy(t => t.FullName))
{
    Console.WriteLine(type.FullName);
}
