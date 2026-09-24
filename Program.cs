using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

Console.WriteLine("=== XGraphicsPdfPageOptions ===");
foreach (var v in Enum.GetValues(typeof(XGraphicsPdfPageOptions)))
    Console.WriteLine(v);

Console.WriteLine("=== FromPdfPage overloads ===");
foreach (var m in typeof(XGraphics).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "FromPdfPage"))
    Console.WriteLine(m);

Console.WriteLine("=== PdfPage.Contents member ===");
var contentsProp = typeof(PdfPage).GetProperty("Contents");
Console.WriteLine(contentsProp?.PropertyType);

Console.WriteLine("=== PdfContent members ===");
foreach (var m in typeof(PdfContent).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
{
    if (m is MethodInfo mi && mi.IsSpecialName) continue;
    Console.WriteLine($"  {m.MemberType} {m}");
}

Console.WriteLine("=== OpCode / CInteger / CName ===");
var ns = typeof(CSequence).Namespace;
var asm = typeof(CSequence).Assembly;
foreach (var t in asm.GetTypes().Where(t => t.Namespace == ns))
    Console.WriteLine(t.FullName);
