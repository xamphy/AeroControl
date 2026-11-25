using System.Security.Principal;

namespace AeroControl;

public static class Elevation
{
    public static bool IsAdmin =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
