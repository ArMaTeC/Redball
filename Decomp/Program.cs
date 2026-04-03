using System;
using System.Reflection;
using System.IO;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@""C:\Users\karll\.nuget\packages\inputinterceptor\2.2.1\lib\netstandard2.0\InputInterceptor.dll"");
        var bytes = File.ReadAllBytes(asm.Location);
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @""[\x20-\x7E]{6,}"");
        foreach(System.Text.RegularExpressions.Match m in matches) {
            if (m.Value.Contains("".exe"") || m.Value.Contains(""install"")) {
                Console.WriteLine(""String: "" + m.Value);
            }
        }
    }
}
