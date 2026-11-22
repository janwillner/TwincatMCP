# Structured Text conventions (Beckhoff style)

| Object | Prefix | Example |
|---|---|---|
| Program | `PRG_` or `MAIN` | `PRG_Conveyor` |
| Function block | `FB_` | `FB_Motor` |
| Function | `F_` | `F_Scale` |
| Interface | `I_` | `I_Drive` |
| Struct | `ST_` | `ST_MotorData` |
| Enum | `E_` | `E_MachineState` |
| Union / Alias | `U_` / `T_` | `T_Speed` |
| GVL | `GVL_` | `GVL_IO` |
| Method / Property | `M_` / `P_` (optional) | `M_Start`, `P_IsReady` |

Variable prefixes: `b` BOOL, `n` integers (BYTE/WORD/INT/DINT/UDINT...), `f` REAL/LREAL, `s` STRING, `ws` WSTRING,
`t` TIME, `d` DATE/DT, `st` struct, `e` enum, `fb` FB instance, `a` array, `p` POINTER, `ref` REFERENCE, `i` interface, `ax` AXIS_REF.
Prefixes combine from the outside in: `ARRAY OF ST_Data` is `ast`, `POINTER TO INT` is `pn`.
Scope: `g` globals optional in GVL; inputs/outputs carry no extra prefix.

- Use `{attribute 'qualified_only'}` on GVLs and `{attribute 'strict'}` on enums.
- Hardware I/O: `bSensor AT %I* : BOOL;` / `bValve AT %Q* : BOOL;` then link with `mapping_link`.
- Prefer state machines with `CASE eState OF` and an enum.
- Methods return explicit values; avoid side effects in properties.
