using System;
using System.Collections.Generic;
using System.Management;

namespace AeroControl.Hardware;

/// <summary>
/// Thin wrapper over GIGABYTE's ACPI-WMI bridge (root\WMI: GB_WMIACPI_Get / GB_WMIACPI_Set).
/// This is the same interface GIGABYTE Control Center uses; every call needs administrator rights.
/// </summary>
public static class GbWmi
{
    private const string Scope = @"root\WMI";
    public const string GetClass = "GB_WMIACPI_Get";
    public const string SetClass = "GB_WMIACPI_Set";

    private static readonly object Gate = new();
    private static ManagementScope _scope;
    private static readonly Dictionary<string, ManagementObject> Instances = new();
    private static readonly Dictionary<string, ManagementClass> Classes = new();

    private static ManagementScope ScopeObj
    {
        get
        {
            if (_scope == null)
            {
                _scope = new ManagementScope(Scope, new ConnectionOptions
                {
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate,
                });
                _scope.Connect();
            }
            return _scope;
        }
    }

    private static ManagementClass Class(string cls)
    {
        if (!Classes.TryGetValue(cls, out var mc))
        {
            mc = new ManagementClass(ScopeObj, new ManagementPath(cls), null);
            Classes[cls] = mc;
        }
        return mc;
    }

    private static ManagementObject Instance(string cls)
    {
        if (Instances.TryGetValue(cls, out var mo)) return mo;
        foreach (ManagementObject o in Class(cls).GetInstances())
        {
            Instances[cls] = o;
            return o;
        }
        throw new InvalidOperationException($"No instance of {cls}. Is this a GIGABYTE laptop running as administrator?");
    }

    public static bool HasMethod(string cls, string method)
    {
        try
        {
            foreach (MethodData m in Class(cls).Methods)
                if (m.Name == method) return true;
        }
        catch { }
        return false;
    }

    public static IEnumerable<MethodData> Methods(string cls)
    {
        foreach (MethodData m in Class(cls).Methods) yield return m;
    }

    /// <summary>Invoke a method; input values are converted to each parameter's CIM type. Returns all out-parameters.</summary>
    public static Dictionary<string, object> Invoke(string cls, string method, IDictionary<string, object> args = null)
    {
        lock (Gate)
        {
            var mc = Class(cls);
            ManagementBaseObject inParams = null;
            var md = mc.Methods[method];
            if (md.InParameters != null)
            {
                inParams = mc.GetMethodParameters(method);
                if (args != null)
                {
                    foreach (var kv in args)
                    {
                        var prop = inParams.Properties[kv.Key];
                        inParams[kv.Key] = ConvertTo(prop.Type, kv.Value);
                    }
                }
            }
            var outParams = Instance(cls).InvokeMethod(method, inParams, null);
            var result = new Dictionary<string, object>();
            if (outParams != null)
                foreach (PropertyData p in outParams.Properties)
                    if (p.Name != "ReturnValue" || p.Value != null) result[p.Name] = p.Value;
            return result;
        }
    }

    /// <summary>Read a single-value getter (out parameter "Data").</summary>
    public static int Get(string method)
    {
        var r = Invoke(GetClass, method);
        return r.TryGetValue("Data", out var v) && v != null ? Convert.ToInt32(v) : -1;
    }

    public static bool TryGet(string method, out int value)
    {
        try { value = Get(method); return value >= 0; }
        catch { value = -1; return false; }
    }

    /// <summary>Call a single-value setter (in parameter "Data").</summary>
    public static void Set(string method, int data)
    {
        Invoke(SetClass, method, new Dictionary<string, object> { ["Data"] = data });
    }

    public static bool TrySet(string method, int data)
    {
        try { Set(method, data); return true; }
        catch (Exception ex) { Log.Write($"WMI {method}({data}) failed: {ex.Message}"); return false; }
    }

    private static object ConvertTo(CimType t, object v) => t switch
    {
        CimType.UInt8 => Convert.ToByte(v),
        CimType.UInt16 => Convert.ToUInt16(v),
        CimType.UInt32 => Convert.ToUInt32(v),
        CimType.UInt64 => Convert.ToUInt64(v),
        CimType.SInt8 => Convert.ToSByte(v),
        CimType.SInt16 => Convert.ToInt16(v),
        CimType.SInt32 => Convert.ToInt32(v),
        CimType.Boolean => Convert.ToBoolean(v),
        CimType.String => Convert.ToString(v),
        _ => v,
    };
}
