using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

sealed class Tee : TextWriter
{
    private readonly TextWriter _a, _b;
    public Tee(TextWriter a, TextWriter b) { _a = a; _b = b; }
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char c) { _a.Write(c); _b.Write(c); }
    public override void Write(string? s) { _a.Write(s); _b.Write(s); }
    public override void WriteLine(string? s) { _a.WriteLine(s); _b.WriteLine(s); }
}