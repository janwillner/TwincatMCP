# Runtime setup for tests (work package 3)

The runtime tests (activation, PLC login/start) need a reachable, **licensed** TwinCAT runtime. On this development PC the
target is a local **TwinCAT 3 Usermode Runtime** (TC1700, XAR 4026.21.1) because Windows VBS/Hyper-V prevents the
real-time kernel from entering Run mode.

## What is on this PC

| Runtime | Folder | AMS Net ID | How it runs | Used by tests? |
|---|---|---|---|---|
| `UmRT_Default` | `C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default` | `169.254.123.103.1.1` (= "local") | Windows service **TcSysSrv** (`TcSystemServiceUm.exe -s TcSysSrv ...`) | **No** - it is the PC's own TwinCAT system and runs your application (PLC port 852). |
| `UmRT_McpTest` | `C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_McpTest` | **`199.4.42.250.1.1`** | user process `TcSystemServiceUm.exe -t bin -i path -n UmRT_McpTest -c .\3.1` (minimized console) | **Yes** - the dedicated test target. |

`UmRT_McpTest` was created by copying `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Template` (what
`rt_umrt_create` does). On its first start the runtime wrote the documented default Net ID `199.4.42.250.1.1` into
`3.1\TcRegistry.xml` (`AmsNetId` BIN `C7042AFA0101`). It logs on to the local system router, so the XAE and ADS clients on
this PC reach it directly (it shows up as route `UmRT_McpTest` in `system_list_targets`). It starts in **Config** mode.

Start / stop it with the MCP tools `rt_umrt_start` / `rt_umrt_stop` (`name = "UmRT_McpTest"`), or double-click
`C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_McpTest\Start.bat` (console keys: `r` = Run, `c` = Config, `s` = state,
`x` = exit). The TwinCAT system tray also lists all ProgramData instances.

## Verified so far (2026-11-07)

- `system_set_target("UmRT_McpTest")` -> `199.4.42.250.1.1`, `system_set_target_platform("TwinCAT OS (x64)")`, `build_solution` OK.
- `system_activate_configuration` wrote the boot configuration to the instance (`3.1\Boot\CurrentConfig*.xml`, `Boot\Plc`).
- The restart in Run mode then failed exactly at the license: the XAE showed the modal box
  *"Target system reports a fatal error - 'TwinCAT System' (10000): Error: >> license not found << checking TwinCAT Licenses!"*.
  This box appears even in SilentMode and blocks every Automation-Interface call (`RPC_E_CALL_REJECTED`) until a human
  clicks OK. Therefore `system_activate_configuration` now refuses to restart a local Usermode Runtime without a valid
  license file (`skipLicenseCheck` overrides) and, for other targets, stops waiting and reports the box text (`xaeDialog`).
- The instance stayed in Config mode; `UmRT_Default` (your application) was not touched.
- With `TWINCAT_MCP_RUNTIME_TESTS=1`: `RuntimeUmrtToolsTests` passes (stop, start, ADS state/info of `UmRT_McpTest`);
  `RuntimeActivationTests` stops at the license pre-check:
  *"The target is the local Usermode Runtime 'UmRT_McpTest' (199.4.42.250.1.1) and it has no license file in
  3.1\Target\License."* - this is the point where your manual step is needed.

## The one manual step: 7-day trial license for `UmRT_McpTest`

TwinCAT only lets a human generate trial licenses (captcha). Licenses are bound to the runtime's system ID; the trial
license of `UmRT_Default` (`3.1\Target\License\TrialLicense.tclrs`) does not belong to `UmRT_McpTest`.

1. Make sure the instance runs: `rt_umrt_start` with `name = "UmRT_McpTest"` (or its `Start.bat`).
2. Open a TwinCAT project **that contains a PLC project** (so TC1200 is part of the required licenses), either
   - in the XAE yourself (any solution, e.g. the one created by the steps below), or
   - via the MCP server: `xae_start` (`visible = true`), `solution_create`, `tree_create_child`
     (`parentPath = "TIPC"`, `name = "Plc1"`, `info = "Standard PLC Template"`).
3. Select the target: `system_set_target` with `target = "UmRT_McpTest"` (or in the XAE toolbar *Choose Target System* ->
   `UmRT_McpTest (199.4.42.250.1.1)`).
4. Call `license_activate_trial`. It makes the XAE window visible, selects **SYSTEM > License** and returns the steps:
   double-click **License**, tab **Order Information (Runtime)**, check the target `199.4.42.250.1.1`, click
   **7 Days Trial License...**, type the security code, **OK**.
5. Verify: `rt_umrt_list` shows a `licenseFiles` entry (TC1000/TC1200 ..., `expired = false`) for `UmRT_McpTest`, or
   `license_list` shows `targetLicenses`.

The trial is valid for 7 days and can be renewed the same way.

## Running the runtime tests

```powershell
$env:TWINCAT_MCP_RUNTIME_TESTS = '1'
# optional: other target / instance
# $env:TWINCAT_MCP_RUNTIME_NETID  = '199.4.42.250.1.1'
# $env:TWINCAT_MCP_UMRT_INSTANCE  = 'UmRT_McpTest'
dotnet test tests/TwinCatMcp.Tests --filter "Category=Runtime"
```

`RuntimeActivationTests` starts the instance (`rt_umrt_start`), sets the target and the matching platform
(`system_get_info` -> `TwinCAT OS (x64)`), builds, activates with restart in Run mode, logs in with download, starts,
stops, resets and logs out the PLC and finally switches the runtime back to **Config** mode.
`RuntimeUmrtToolsTests` stops and restarts the instance and checks it via ADS.

Without `TWINCAT_MCP_RUNTIME_TESTS=1` these tests are reported as *skipped*; the offline tests
(`TaskToolsTests`, `RuntimeConfigToolsTests`) always run.

> Do not point `TWINCAT_MCP_RUNTIME_NETID` at `169.254.123.103.1.1` unless you accept that the activation replaces the
> application currently running on the PC's own TwinCAT system (`UmRT_Default`).
