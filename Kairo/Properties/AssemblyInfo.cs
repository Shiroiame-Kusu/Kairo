using System.Reflection;
using System.Runtime.InteropServices;
using Kairo; // to access Global constants

// Centralized assembly metadata; values pulled from Global constants so updating Global updates assembly info.
// IMPORTANT: Global.Version must stay a three-part string (e.g. "3.1.0"). We append ".0" for four-part Assembly/File version.

[assembly: AssemblyTitle("Kairo")]
[assembly: AssemblyProduct("Kairo")]
[assembly: AssemblyDescription("A tool which can launch LocyanFrp's proxies.")]
[assembly: AssemblyCompany(Global.Developer)] // using Developer string as company/author line
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// Version attributes (AssemblyVersion & FileVersion must be compile-time constants)
[assembly: AssemblyVersion(Global.Version + ".0")]      // becomes e.g. 3.1.0.0
[assembly: AssemblyFileVersion(Global.Version + ".0")] // keeps file version aligned
// Informational version (can include marketing name). Must remain a constant expression (string concatenation only)
[assembly: AssemblyInformationalVersion(Global.Version + " " + Global.VersionName)]

