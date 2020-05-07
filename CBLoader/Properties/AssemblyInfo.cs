using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CBLoader")]
[assembly: AssemblyDescription("A homebrew rules loader for the D&D Insider Character Builder")]
[assembly: AssemblyProduct("CBLoader")]
[assembly: AssemblyCopyright("Released under the GNU General Public License v2.0")]
[assembly: AssemblyVersion("1.4.5.*")]

[assembly: ComVisible(false)]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
