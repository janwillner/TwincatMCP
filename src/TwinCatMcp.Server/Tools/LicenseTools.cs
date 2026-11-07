using System.ComponentModel;
using System.Xml.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TCatSysManagerLib;
using TwinCatMcp.Core;
using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tools;

/// <summary>TwinCAT licenses (work package 3): license catalog, licenses on local runtimes, manual licenses, 7-day trial.</summary>
[McpServerToolType]
public sealed class LicenseTools(XaeSession session)
{
    [McpServerTool(Name = "license_list", ReadOnly = true), Description(
        "Shows license information: (1) licenses present on the target when it is a Usermode Runtime of this PC (license files incl. trial " +
        "expiry), (2) the license request settings of the project (TIRC^License), (3) licenses added manually to PLC projects, and " +
        "(4) with 'search', matching entries of the TwinCAT license catalog (e.g. 'TC1200' -> TC3 PLC with its license GUID).")]
    public Task<CallToolResult> List(
        [Description("Optional search text for the license catalog (order number like 'TC1200' or name like 'PLC').")] string? search = null)
        => session.RunAsync(s =>
        {
            var netId = XaeRuntime.TargetNetId(s);
            var local = XaeRuntime.Try(() => UmrtManager.List().FirstOrDefault(i => string.Equals(i.NetId, netId, StringComparison.OrdinalIgnoreCase)));
            var request = XaeRuntime.Try(() => XDocument.Parse(s.Lookup("TIRC^License").ProduceXml(false)).Root?.Element("LicenseDef")?.Element("LicenseRequest"));
            return new
            {
                targetNetId = netId,
                targetLicenses = local is null
                    ? null
                    : new { usermodeRuntime = local.Name, files = local.LicenseFiles },
                targetLicensesNote = local is null ? "Target is not a Usermode Runtime of this PC: its licenses are shown in the XAE (System > License > Online Licenses)." : null,
                licenseRequest = request is null ? null : new
                {
                    provider = request.Element("Provider")?.Value,
                    licenseId = NullIfEmpty(request.Element("LicenseId")?.Value),
                    customerId = NullIfEmpty(request.Element("CustomerId")?.Value),
                },
                plcProjectLicenses = PlcProjectLicenses(s),
                catalog = string.IsNullOrWhiteSpace(search) ? null : Catalog(s, search!),
            };
        });

    [McpServerTool(Name = "license_add"), Description(
        "Adds (or with remove=true removes) a license manually to/from a PLC project (IECProjectDef/LicenseSettings), e.g. for function " +
        "licenses used by libraries (TF6250 Modbus TCP, TF6310 TCP/IP). The license becomes part of the project's required licenses and " +
        "therefore of the 7-day trial / license request. Accepts an order number ('TF6250') or a license GUID. Licenses the configuration " +
        "needs anyway (TC1200 PLC, TC1000 ADS, ...) are detected automatically by TwinCAT.")]
    public Task<CallToolResult> Add(
        [Description("Order number ('TF6250') or license GUID ('{...}').")] string license,
        [Description("PLC project name (e.g. 'Plc1'). Empty = first PLC project.")] string? plcProject = null,
        [Description("true = remove the license from the PLC project instead.")] bool remove = false)
        => session.RunAsync(s =>
        {
            var desc = ResolveLicense(s, license);
            var path = XaeRuntime.NestedProjectPath(s, plcProject ?? "");
            var item = s.Lookup(path);
            var error = XaeRuntime.Consume(item, LicenseXml.PlcLicenseCommand(desc.LicenseId, remove));
            var now = LicenseXml.ParsePlcLicenses(item.ProduceXml(false));
            return new
            {
                plcProject = path,
                license = new { desc.LicenseId, desc.DisplayName, orderNo = NullIfEmpty((desc as ITcLicenseDesc2)?.OrderId) },
                removed = remove,
                present = now.Contains(desc.LicenseId, StringComparer.OrdinalIgnoreCase),
                plcProjectLicenses = now.Select(id => Describe(s, id)).ToArray(),
                xmlError = error,
            };
        });

    [McpServerTool(Name = "license_activate_trial"), Description(
        "Prepares the 7-day trial license for the current target. TwinCAT protects trial generation with a captcha, so the final click must be " +
        "done by a human: this tool checks whether the target already has a valid trial (local Usermode Runtimes), makes the XAE window visible, " +
        "selects System > License and returns the exact steps. Afterwards call license_list to verify.")]
    public Task<CallToolResult> ActivateTrial()
        => session.RunAsync(s =>
        {
            var netId = XaeRuntime.TargetNetId(s);
            var local = XaeRuntime.Try(() => UmrtManager.List().FirstOrDefault(i => string.Equals(i.NetId, netId, StringComparison.OrdinalIgnoreCase)));
            var valid = local?.LicenseFiles.Where(f => !f.Expired).ToList();
            if (valid is { Count: > 0 })
            {
                return new
                {
                    targetNetId = netId,
                    trialPresent = true,
                    licenseFiles = valid,
                    message = "The target already has a valid license file. No manual step needed.",
                };
            }

            var visible = XaeRuntime.Try(() => { s.Dte.MainWindow.Visible = true; s.Dte.MainWindow.Activate(); return true; });
            var selected = XaeRuntime.Try(() => { s.SysManager.SelectTreeItem("TIRC^License"); return true; });
            return new
            {
                targetNetId = netId,
                trialPresent = false,
                xaeVisible = visible,
                licenseNodeSelected = selected,
                manualSteps = new[]
                {
                    "In the TwinCAT XAE window (now visible): Solution Explorer > SYSTEM > License (double-click to open the license editor).",
                    "Tab 'Order Information (Runtime)': check that the target system shown is " + netId + ".",
                    "Click '7 Days Trial License...', type the security code shown in the dialog and press OK.",
                    "Then call license_list (target licenses) and system_activate_configuration.",
                },
                note = "Trial generation needs the captcha dialog; the Automation Interface offers no API for it.",
            };
        });

    // ------------------------------------------------------------------ helpers

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static ITcLicenseManager2 Manager(XaeSession s)
        => s.SysManager.GetModuleManager() as ITcLicenseManager2
           ?? throw new TcException("The license catalog (ITcLicenseManager2) is not available.");

    private static object[] Catalog(XaeSession s, string search)
    {
        var result = new List<object>();
        foreach (ITcLicenseDesc d in Manager(s).Licenses)
        {
            var order = (d as ITcLicenseDesc2)?.OrderId;
            if ((d.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (order?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                string.Equals(d.LicenseId, search, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new { d.LicenseId, d.DisplayName, orderNo = NullIfEmpty(order), vendor = d.VendorName });
            }
        }

        return result.Take(50).ToArray();
    }

    private static ITcLicenseDesc ResolveLicense(XaeSession s, string license)
    {
        var key = (license ?? "").Trim();
        if (key.Length == 0)
        {
            throw new TcException("license must not be empty.") { Hint = "Pass an order number such as 'TF6250' or a license GUID." };
        }

        var mgr = Manager(s);
        if (key.StartsWith('{') && mgr.Licenses.TryLookup(key, out var byId) && byId is not null)
        {
            return byId;
        }

        foreach (ITcLicenseDesc d in mgr.Licenses)
        {
            if (string.Equals((d as ITcLicenseDesc2)?.OrderId, key, StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }
        }

        throw new TcException($"License '{license}' not found in the TwinCAT license catalog.") { Hint = "Search it with license_list(search: 'TF62')." };
    }

    private static object[] PlcProjectLicenses(XaeSession s)
    {
        var result = new List<object>();
        foreach (var nested in XaeRuntime.NestedProjects(s))
        {
            var ids = XaeRuntime.Try(() => LicenseXml.ParsePlcLicenses(nested.ProduceXml(false))) ?? [];
            result.Add(new { plcProject = nested.PathName, licenses = ids.Select(id => Describe(s, id)).ToArray() });
        }

        return result.ToArray();
    }

    private static object Describe(XaeSession s, string licenseId)
    {
        var d = XaeRuntime.Try(() => Manager(s).Licenses.TryLookup(licenseId, out var desc) ? desc : null);
        return new { licenseId, displayName = d?.DisplayName, orderNo = NullIfEmpty((d as ITcLicenseDesc2)?.OrderId) };
    }
}
