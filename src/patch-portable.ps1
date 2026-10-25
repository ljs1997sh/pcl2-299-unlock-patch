# PCL 2.99.0.0 (413) MC-download unlock patcher
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File patch-portable.ps1 [input.exe] [output.exe]
# Defaults: input  = Plain Craft Launcher 2.exe in the current directory
#           output = <input base name>.patched.exe
# Requires: dnlib.dll next to this script (included), .NET Framework 4.x (ships with Windows)
# The output exe contains no user data: accounts/settings live in the PCL\ folder, not in the exe.

param(
    [string]$SrcPath = '',
    [string]$DstPath = ''
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($SrcPath)) { $SrcPath = Join-Path (Get-Location) 'Plain Craft Launcher 2.exe' }
if (-not (Test-Path -LiteralPath $SrcPath)) { throw "Input not found: $SrcPath" }
$SrcPath = (Resolve-Path -LiteralPath $SrcPath).Path
if ([string]::IsNullOrWhiteSpace($DstPath)) {
    $DstPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($SrcPath),
        ([System.IO.Path]::GetFileNameWithoutExtension($SrcPath) + '.patched.exe'))
}

$dnlib = Join-Path $scriptDir 'dnlib.dll'
if (-not (Test-Path -LiteralPath $dnlib)) { throw "dnlib.dll not found next to this script: $dnlib" }
# clear the "downloaded from the internet" mark, otherwise LoadFrom refuses to run it
try { Unblock-File -LiteralPath $dnlib -ErrorAction SilentlyContinue } catch { }
$loaded = $false
try { Add-Type -Path $dnlib -ErrorAction Stop; $loaded = $true } catch { }
if (-not $loaded) {
    $bytes = [System.IO.File]::ReadAllBytes($dnlib)
    [System.Reflection.Assembly]::Load($bytes) | Out-Null
    Write-Output 'dnlib: loaded from memory'
}

$OD  = [dnlib.DotNet.Emit.OpCodes]
$INS = [dnlib.DotNet.Emit.Instruction]

function Get-InsText($ins) {
    $o = $ins.Operand
    if ($o -eq $null) { return '' }
    if ($o -is [string]) { return 'STRING(' + $o.Length + ')' }
    try { return $o.ToString() } catch { return '' }
}

function Replace-Ins([object]$body, [object]$target, [object]$replacement) {
    foreach ($x in $body.Instructions) { if ([object]::ReferenceEquals($x.Operand, $target)) { $x.Operand = $replacement } }
    foreach ($h in $body.ExceptionHandlers) {
        if ([object]::ReferenceEquals($h.TryStart, $target))     { $h.TryStart = $replacement }
        if ([object]::ReferenceEquals($h.TryEnd, $target))       { $h.TryEnd = $replacement }
        if ([object]::ReferenceEquals($h.HandlerStart, $target)) { $h.HandlerStart = $replacement }
        if ([object]::ReferenceEquals($h.HandlerEnd, $target))   { $h.HandlerEnd = $replacement }
        if ([object]::ReferenceEquals($h.FilterStart, $target))  { $h.FilterStart = $replacement }
    }
    $idx = $body.Instructions.IndexOf($target)
    if ($idx -lt 0) { throw 'Replace-Ins: target not found' }
    $body.Instructions.RemoveAt($idx)
    $body.Instructions.Insert($idx, $replacement)
}

$mod = [dnlib.DotNet.ModuleDefMD]::Load($SrcPath)
$ver = $mod.Assembly.Version
Write-Output ('input : ' + $SrcPath)
Write-Output ('version: ' + $ver.ToString())
if ($ver.ToString() -ne '2.99.0.0') {
    throw ('This patcher only supports PCL 2.99.0.0 (413); found ' + $ver.ToString() + '. Nothing was written.')
}

# --- P1: restore the real client-list loader on the download page ---
$t1 = $mod.Find('PCL.PageDownloadInstall', $false)
if ($t1 -eq $null) { throw 'P1: type PCL.PageDownloadInstall not found' }
$m1 = $t1.FindMethod('LoaderInit')
if ($m1 -eq $null -or -not $m1.HasBody) { throw 'P1: LoaderInit not found' }
$b1 = $m1.Body
$iStr = -1; $iNew = -1
for ($i = 0; $i -lt $b1.Instructions.Count; $i++) {
    $x = $b1.Instructions[$i]
    if ($x.OpCode.Code -eq 'Ldstr' -and $x.Operand -is [string] -and $x.Operand.Length -eq 4) { $iStr = $i }
    if ($x.OpCode.Code -eq 'Newobj') {
        $op = $x.Operand; $decl = ''; $nm = ''
        try { $decl = $op.DeclaringType.FullName; $nm = $op.Name } catch { }
        if ($decl.StartsWith('PCL.ModLoader/LoaderTask') -and $nm -eq '.ctor') { $iNew = $i }
    }
}
if ($iStr -lt 0 -or $iNew -le $iStr) { throw 'P1: gate pattern not found (already patched, or different build)' }
$victims = @()
for ($k = $iStr; $k -le $iNew; $k++) { $victims += $b1.Instructions[$k] }
for ($j = 0; $j -lt $b1.Instructions.Count; $j++) {
    if ($j -ge $iStr -and $j -le $iNew) { continue }
    foreach ($v in $victims) {
        if ([object]::ReferenceEquals($b1.Instructions[$j].Operand, $v)) { throw ('P1: branch from IL index ' + $j + ' targets removed code') }
    }
}
$field = $mod.Find('PCL.ModDownload', $false).FindField('DlClientListLoader')
if ($field -eq $null) { throw 'P1: DlClientListLoader missing' }
$ldsfld = $INS::Create($OD::Ldsfld, [dnlib.DotNet.IField]$field)
for ($k = $iNew; $k -ge $iStr; $k--) { $b1.Instructions.RemoveAt($k) }
$b1.Instructions.Insert($iStr, $ldsfld)
Write-Output 'P1 ok: download page loads the real version list'

# --- P2: neutralise the modpack-install throw ---
$t2 = $mod.Find('PCL.ModModpack', $false)
$m2 = $null
foreach ($c in $t2.Methods) { if ($c.Name -eq 'ModpackInstall' -and $c.ParamDefs.Count -eq 3) { $m2 = $c } }
if ($m2 -eq $null) { throw 'P2: ModpackInstall(String,String,String) not found' }
$b2 = $m2.Body
$hit = -1
for ($i = 2; $i -lt $b2.Instructions.Count; $i++) {
    $a = $b2.Instructions[$i - 2]; $b = $b2.Instructions[$i - 1]; $c = $b2.Instructions[$i]
    if ($a.OpCode.Code -eq 'Ldstr' -and $a.Operand -is [string] -and $a.Operand.Length -eq 39 -and
        $b.OpCode.Code -eq 'Newobj' -and (Get-InsText $b) -like '*System.Exception::.ctor(System.String)*' -and
        $c.OpCode.Code -eq 'Throw') { $hit = $i }
}
if ($hit -lt 0) { throw 'P2: modpack gate pattern not found' }
foreach ($idx in @(($hit - 2), ($hit - 1), $hit)) {
    Replace-Ins $b2 $b2.Instructions[$idx] ($INS::Create($OD::Nop))
}
Write-Output 'P2 ok: modpack install unblocked'

# --- P3: show the Minecraft card on the modify page ---
$t3 = $mod.Find('PCL.PageDownloadModify', $false)
$m3 = $t3.FindMethod('LoadInstanceFinished')
if ($m3 -eq $null -or -not $m3.HasBody) { throw 'P3: LoadInstanceFinished not found' }
$b3 = $m3.Body
$done3 = $false
for ($i = 0; $i -lt $b3.Instructions.Count - 2; $i++) {
    $a = $b3.Instructions[$i]; $b = $b3.Instructions[$i + 1]; $c = $b3.Instructions[$i + 2]
    if ((Get-InsText $a) -like '*get_CardMinecraft*' -and $b.OpCode.Code -eq 'Ldc_I4_2' -and (Get-InsText $c) -like '*set_Visibility*') {
        Replace-Ins $b3 $b ($INS::Create($OD::Ldc_I4_0))
        $done3 = $true; break
    }
}
if (-not $done3) { throw 'P3: CardMinecraft visibility pattern not found' }
Write-Output 'P3 ok: Minecraft component card visible'

# --- P4: restore the "install" button on modpack resource pages ---
$t4 = $mod.Find('PCL.PageDownloadResourceDetail', $false)
$m4 = $t4.FindMethod('InitializeStackResourceVersions')
if ($m4 -eq $null -or -not $m4.HasBody) { throw 'P4: InitializeStackResourceVersions not found' }
$b4 = $m4.Body
$iToList = -1
for ($i = 0; $i -lt $b4.Instructions.Count; $i++) {
    if ($b4.Instructions[$i].OpCode.Code -eq 'Callvirt' -and (Get-InsText $b4.Instructions[$i]) -like '*ResourceVersion::ToListItem*') { $iToList = $i }
}
if ($iToList -lt 0) { throw 'P4: ToListItem call not found' }
$cSave = $b4.Instructions[$iToList - 3]
$cNull = $b4.Instructions[$iToList - 2]
if ($cSave.OpCode.Code -ne 'Newobj') { throw 'P4: expected newobj handler' }
if ($cNull.OpCode.Code -ne 'Ldnull') { throw 'P4: expected ldnull' }
$ctorRef = $cSave.Operand
$mInstall = $null
foreach ($cand in $t4.Methods) { if ($cand.Name -eq 'Install_Click' -and $cand.ParamDefs.Count -eq 2) { $mInstall = $cand } }
if ($mInstall -eq $null) { throw 'P4: Install_Click not found' }
$iSaveArg = $iToList - 5
$n1 = $INS::Create($OD::Ldarg_0)
$n2 = $INS::Create($OD::Ldftn, [dnlib.DotNet.IMethod]$mInstall)
$n3 = $INS::Create($OD::Newobj, [dnlib.DotNet.IMethod]$ctorRef)
Replace-Ins $b4 $cNull ($INS::Create($OD::Nop))
$b4.Instructions.Insert($iSaveArg, $n1)
$b4.Instructions.Insert($iSaveArg + 1, $n2)
$b4.Instructions.Insert($iSaveArg + 2, $n3)
Write-Output 'P4 ok: modpack "install" entry restored'

# --- P5: replace the compiler-gutted hint with a usable instruction ---
$t5 = $mod.Find('PCL.ModModpack', $false)
$m5 = $null
foreach ($c in $t5.Methods) { if ($c.Name -eq 'ModpackInstall' -and $c.ParamDefs.Count -eq 0) { $m5 = $c } }
if ($m5 -eq $null -or -not $m5.HasBody) { throw 'P5: ModpackInstall() not found' }
$b5 = $m5.Body
$msg5 = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('5oqK5pW05ZCI5YyFIHppcCDnm7TmjqXmi5bov5sgUENMIOeql+WPo+WwseiDveWuieijhe+8m+i/meS4quaMiemSrueahOiHquWKqOmAieaLqeWKn+iDveW3suiiq+WumOaWueenu+mZpOS6hg=='))
$oldStr = $null
$doneCol = $false
for ($i = 0; $i -lt $b5.Instructions.Count; $i++) {
    $x = $b5.Instructions[$i]
    if ($x.OpCode.Code -eq 'Ldstr') { $oldStr = $x }
    if ($x.OpCode.Code -eq 'Ldc_I4_2' -and $i + 2 -lt $b5.Instructions.Count -and (Get-InsText $b5.Instructions[$i + 2]) -like '*Hint(System.String*') {
        Replace-Ins $b5 $x ($INS::Create($OD::Ldc_I4_0)); $doneCol = $true
    }
}
if ($oldStr -eq $null) { throw 'P5: hint string not found' }
Replace-Ins $b5 $oldStr ($INS::Create($OD::Ldstr, [string]$msg5))
Write-Output ('P5 ok: hint rewritten (color patched: ' + $doneCol + ')')

# --- P6: default the update-notice setting to "off" so a re-update cannot lock it again ---
$t6 = $mod.Find('PCL.Settings', $false)
$m6 = $null
foreach ($c in $t6.Methods) { if ($c.Name -eq '.cctor') { $m6 = $c } }
if ($m6 -eq $null -or -not $m6.HasBody) { throw 'P6: Settings::.cctor not found' }
$b6 = $m6.Body
$done6 = $false
for ($i = 0; $i -lt $b6.Instructions.Count - 1; $i++) {
    $x = $b6.Instructions[$i]
    if ($x.OpCode.Code -eq 'Ldstr' -and $x.Operand -is [string] -and $x.Operand -eq 'SystemSystemUpdate') {
        $nxt = $b6.Instructions[$i + 1]
        if ($nxt.OpCode.Code -eq 'Ldc_I4_0') {
            Replace-Ins $b6 $nxt ($INS::Create($OD::Ldc_I4_3)); $done6 = $true
        }
    }
}
if (-not $done6) { throw 'P6: SystemSystemUpdate default pattern not found' }
Write-Output 'P6 ok: update notice defaults to off'

$mod.Write($DstPath)
Write-Output ('written: ' + $DstPath + '  size=' + (Get-Item -LiteralPath $DstPath).Length)
Write-Output 'done. now close PCL, back up your old exe, and overwrite it with the patched one.'
